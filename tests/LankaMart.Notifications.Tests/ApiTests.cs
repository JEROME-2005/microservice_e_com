using System.Net;
using System.Net.Http.Json;
using LankaMart.Contracts.V1.Internal;
using LankaMart.Notifications.Api.Delivery;
using LankaMart.Notifications.Api.Domain;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LankaMart.Notifications.Tests;

/// <summary>The real HTTP pipeline (routing, auth, validation, internal-key filter) against real Postgres. JWTs are replaced by a
/// header-based test scheme; RabbitMQ by MassTransit's in-memory test harness; SMTP by a recording fake.</summary>
public sealed class ApiTests(PostgresFixture pg) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string InternalKey = "test-internal-key";
    private readonly FakeEmailSender _email = new();
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Testing");                                  // no appsettings.Development.json → only the settings below
        b.UseSetting("ConnectionStrings:Default", pg.ConnectionString);
        b.UseSetting("Database:AutoMigrate", "true");
        b.UseSetting("Workers:Enabled", "false");
        b.UseSetting("Internal:ApiKey", InternalKey);
        b.ConfigureTestServices(s =>
        {
            s.AddAuthentication(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            s.AddScoped<IEmailSender>(_ => _email);
            s.AddMassTransitTestHarness();                            // in-memory transport instead of RabbitMQ
        });
    });

    private HttpClient ClientFor(Guid? user = null, string? roles = null, bool internalKey = false)
    {
        var c = Factory.CreateClient();
        if (user is not null)
        {
            c.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        }
        if (roles is not null)
        {
            c.DefaultRequestHeaders.Add("X-Test-Roles", roles);
        }
        if (internalKey)
        {
            c.DefaultRequestHeaders.Add("X-Internal-Key", InternalKey);
        }
        return c;
    }

    [Fact]
    public async Task Otp_endpoint_rejects_calls_without_the_internal_key()
    {
        var res = await ClientFor().PostAsJsonAsync("/api/v1/internal/notifications/otp", new SendOtpRequest("email", "a@test.local", "123456", "en"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.DoesNotContain(_email.Sent, s => s.To == "a@test.local");
    }

    [Fact]
    public async Task Otp_endpoint_with_the_internal_key_sends_the_code_immediately()
    {
        var res = await ClientFor(internalKey: true).PostAsJsonAsync("/api/v1/internal/notifications/otp", new SendOtpRequest("email", "otp@test.local", "654321", "en"));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var mail = Assert.Single(_email.Sent, s => s.To == "otp@test.local");
        Assert.Contains("654321", mail.Body);

        await using var db = pg.NewDb();                                // the code is never stored
        Assert.DoesNotContain(db.Messages, m => m.DataJson.Contains("654321"));
    }

    [Fact]
    public async Task Otp_endpoint_validates_its_input()
    {
        var res = await ClientFor(internalKey: true).PostAsJsonAsync("/api/v1/internal/notifications/otp", new SendOtpRequest("fax", "x", "12", "en"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_user_can_neither_read_nor_mark_someone_elses_inbox_items()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var itemId = Guid.CreateVersion7();
        await using (var db = pg.NewDb())
        {
            db.Inbox.Add(new InboxItem { Id = itemId, UserId = alice, Title = "Order confirmed", Body = "secret", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var bobsInbox = await ClientFor(bob).GetFromJsonAsync<Page>("/api/v1/me/notifications");
        Assert.NotNull(bobsInbox);
        Assert.Equal(0, bobsInbox.Total);

        var mark = await ClientFor(bob).PostAsync($"/api/v1/me/notifications/{itemId}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, mark.StatusCode);         // idempotent no-op, no existence leak …
        await using (var db = pg.NewDb())
        {
            Assert.Null(db.Inbox.Single(i => i.Id == itemId).ReadAt);   // … and Alice's item is untouched
        }

        var alicesCount = await ClientFor(alice).GetFromJsonAsync<Count>("/api/v1/me/notifications/unread-count");
        Assert.Equal(1, alicesCount!.count);
        Assert.Equal(HttpStatusCode.NoContent, (await ClientFor(alice).PostAsync($"/api/v1/me/notifications/{itemId}/read", null)).StatusCode);
        Assert.Equal(0, (await ClientFor(alice).GetFromJsonAsync<Count>("/api/v1/me/notifications/unread-count"))!.count);
    }

    [Fact]
    public async Task Inbox_requires_a_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor().GetAsync("/api/v1/me/notifications")).StatusCode);
    }

    [Fact]
    public async Task Security_preferences_cannot_be_switched_off_but_orders_can()
    {
        var user = Guid.NewGuid();
        var c = ClientFor(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/me/notification-preferences", new { category = "security", email = false, sms = false, push = false, inApp = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/v1/me/notification-preferences", new { category = "orders", email = false, sms = false, push = true, inApp = true })).StatusCode);

        var prefs = await c.GetFromJsonAsync<List<Pref>>("/api/v1/me/notification-preferences");
        Assert.Equal(["orders", "promos", "system"], prefs!.Select(p => p.category));
        Assert.False(prefs!.Single(p => p.category == "orders").email);
        Assert.False(prefs!.Single(p => p.category == "promos").email);   // default: marketing is opt-in
    }

    [Fact]
    public async Task Dead_letter_queue_is_staff_only()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(Guid.NewGuid(), roles: "customer").GetAsync("/api/v1/admin/notifications/dead-letter")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(Guid.NewGuid(), roles: "customer_manager").GetAsync("/api/v1/admin/notifications/dead-letter")).StatusCode);
    }

    public void Dispose() => _factory?.Dispose();

    private sealed record Page(long Total);
    private sealed record Count(int count);
    private sealed record Pref(string category, bool email, bool sms, bool push, bool inApp);
}
