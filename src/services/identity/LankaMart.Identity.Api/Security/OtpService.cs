using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace LankaMart.Identity.Api.Security;

public interface IOtpSender
{
    Task SendAsync(string channel, string destination, string code);
}

/// <summary>DEV fallback sender (<c>Otp:Sender=log</c>): writes the code to the log. The default in Development is now
/// <see cref="NotificationOtpSender"/> (real email/SMS via the Notification service).
/// ⚠ Never log OTPs in production — anyone with log access could take over accounts.</summary>
public sealed class LogOtpSender(ILogger<LogOtpSender> log) : IOtpSender
{
    public Task SendAsync(string channel, string destination, string code)
    {
        log.LogWarning("[DEV OTP] {Channel} → {Destination}: {Code}", channel, destination, code);
        return Task.CompletedTask;
    }
}

/// <summary>Scoped (Phase 2): it depends on the scoped <see cref="IOtpSender"/>, whose HTTP client is scoped.</summary>
public sealed class OtpService(IConnectionMultiplexer redis, IOtpSender sender, IHostEnvironment env, ILogger<OtpService> log)
{
    private IDatabase Db => redis.GetDatabase();
    public const int MaxAttempts = 5;

    public async Task SendAsync(Guid userId, string channel, string destination)
    {
        // 60-second cooldown: stops someone using your endpoint to spam a victim's phone (SMS costs money, too)
        if (!await Db.StringSetAsync($"otp:cd:{userId}:{channel}", "1", TimeSpan.FromSeconds(60), When.NotExists))
        {
            return;
        }

        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);   // cryptographic RNG, never `new Random()`
        var key = $"otp:{userId}:{channel}";
        await Db.HashSetAsync(key, [new HashEntry("hash", Hash(code)), new HashEntry("attempts", 0)]);
        await Db.KeyExpireAsync(key, TimeSpan.FromMinutes(5));
        if (env.IsDevelopment())
        {
            await Db.StringSetAsync($"dev:otp:{destination}", code, TimeSpan.FromMinutes(5));   // dev lookup works even if delivery fails
        }
        try
        {
            await sender.SendAsync(channel, destination, code);
        }
        catch (Exception ex)
        {
            // NON-FATAL: the account/verification flow continues (registration still answers 202). Lift the cooldown so the user
            // can press "resend" immediately once the Notification service is back. The code itself is never logged.
            await Db.KeyDeleteAsync($"otp:cd:{userId}:{channel}");
            log.LogError(ex, "OTP delivery failed ({Channel})", channel);
        }
    }

    public async Task<bool> VerifyAsync(Guid userId, string channel, string code)
    {
        var key = $"otp:{userId}:{channel}";
        if (!await Db.KeyExistsAsync(key))
        {
            return false;
        }
        if (await Db.HashIncrementAsync(key, "attempts") > MaxAttempts)   // brute-force limit: 1,000,000 codes / 5 tries
        {
            await Db.KeyDeleteAsync(key);
            return false;
        }
        string? stored = await Db.HashGetAsync(key, "hash");
        if (stored is null)
        {
            return false;
        }
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(code)), Encoding.UTF8.GetBytes(stored));   // constant-time compare
        if (ok)
        {
            await Db.KeyDeleteAsync(key);
        }
        return ok;
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
