using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace LankaMart.Payments.Api.Gateway;

public sealed class MockOptions
{
    public string WebhookSecret { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public int NormalDelaySeconds { get; set; } = 2;
    public int PendingDelaySeconds { get; set; } = 30;
}

public sealed record CardInput(string Number, int ExpMonth, int ExpYear, string Cvc, string Holder)
{
    public override string ToString() => "CardInput(redacted)";          // a record's default ToString prints every field — never let a card reach a log line
}

public enum MockOutcome { Success, Declined, GatewayError, PendingThenSuccess }

public static class MockRules
{
    public static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    public static bool Luhn(string? number)      // the checksum every real card number satisfies (catches typos)
    {
        if (number is null)
        {
            return false;
        }
        var d = Digits(number);
        if (d.Length is < 12 or > 19)
        {
            return false;
        }
        var sum = 0;
        var alt = false;
        for (var i = d.Length - 1; i >= 0; i--)
        {
            var n = d[i] - '0';
            if (alt)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }
            sum += n;
            alt = !alt;
        }
        return sum % 10 == 0;
    }

    public static MockOutcome Decide(string cardNumber, long amountCents)
    {
        var n = Digits(cardNumber);
        if (n == "4000000000000002")
        {
            return MockOutcome.Declined;
        }
        if (n == "4000000000000119")
        {
            return MockOutcome.GatewayError;
        }
        if (amountCents % 100 == 99)
        {
            return MockOutcome.PendingThenSuccess;        // "any amount ending .99 → PENDING"
        }
        return MockOutcome.Success;
    }
}

public sealed record MockWebhook(Guid EventId, string Type, string GatewayRef, Guid PaymentId, long AmountCents, string? FailureCode, DateTimeOffset OccurredAt);
public sealed record MockJob(MockWebhook Event, TimeSpan Delay);

public static class MockJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class MockSigner
{
    public static string Sign(string body, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static bool Verify(string body, string? signature, string secret) =>
        !string.IsNullOrEmpty(secret) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(body, secret)), Encoding.UTF8.GetBytes(signature ?? ""));       // constant-time compare
}

/// <summary>The "bank": decides the outcome and schedules the webhook. Real adapters (PayHere) will implement the same idea: accept → later notify.</summary>
public sealed class MockGateway(Channel<MockJob> queue, IOptions<MockOptions> opt, TimeProvider clock)
{
    public async Task ChargeAsync(Guid paymentId, long amountCents, CardInput card, CancellationToken ct)
    {
        var outcome = MockRules.Decide(card.Number, amountCents);
        var gatewayRef = $"mock_{Guid.NewGuid():N}";
        var normal = TimeSpan.FromSeconds(opt.Value.NormalDelaySeconds);
        var slow = TimeSpan.FromSeconds(opt.Value.PendingDelaySeconds);
        var (type, code, delay) = outcome switch
        {
            MockOutcome.Declined => ("charge.failed", "card_declined", normal),
            MockOutcome.GatewayError => ("charge.failed", "gateway_error", normal),
            MockOutcome.PendingThenSuccess => ("charge.succeeded", (string?)null, slow),
            _ => ("charge.succeeded", (string?)null, normal)
        };
        await queue.Writer.WriteAsync(new MockJob(new MockWebhook(Guid.NewGuid(), type, gatewayRef, paymentId, amountCents, code, clock.GetUtcNow()), delay), ct);
    }
}

/// <summary>Delivers webhooks over real HTTP with a real HMAC signature (so the receiving code path is genuine).</summary>
public sealed class MockWebhookSender(IHttpClientFactory http, IOptions<MockOptions> opt, ILogger<MockWebhookSender> log)
{
    public static readonly ConcurrentDictionary<Guid, MockWebhook> LastSent = new();    // paymentId → last webhook (used by the failure lab)

    public async Task<int> SendAsync(MockWebhook evt, bool validSignature = true, bool remember = true)
    {
        var body = JsonSerializer.Serialize(evt, MockJson.Options);
        var sig = MockSigner.Sign(body, validSignature ? opt.Value.WebhookSecret : "wrong-secret");
        using var req = new HttpRequestMessage(HttpMethod.Post, opt.Value.WebhookUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Mock-Signature", sig);
        if (remember)
        {
            LastSent[evt.PaymentId] = evt;
        }
        var res = await http.CreateClient().SendAsync(req);
        log.LogInformation("Mock webhook {Type} for payment {PaymentId} → HTTP {Status}", evt.Type, evt.PaymentId, (int)res.StatusCode);
        return (int)res.StatusCode;     // a real gateway would retry non-2xx with backoff; the mock just logs
    }
}

public sealed class MockWebhookDispatcher(Channel<MockJob> queue, MockWebhookSender sender, ILogger<MockWebhookDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(async () =>                       // each job waits independently, so a 30 s pending payment doesn't block the others
            {
                try
                {
                    await Task.Delay(job.Delay, stoppingToken);
                    await sender.SendAsync(job.Event);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Mock webhook delivery failed");
                }
            }, stoppingToken);
        }
    }
}
