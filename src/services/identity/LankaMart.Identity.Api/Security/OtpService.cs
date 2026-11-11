using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace LankaMart.Identity.Api.Security;

public interface IOtpSender
{
    Task SendAsync(string channel, string destination, string code);
}

/// <summary>DEV sender: writes the code to the log. Phase 2 replaces this with a Notification-service call (SMS/email).
/// ⚠ Never log OTPs in production — anyone with log access could take over accounts.</summary>
public sealed class LogOtpSender(ILogger<LogOtpSender> log) : IOtpSender
{
    public Task SendAsync(string channel, string destination, string code)
    {
        log.LogWarning("[DEV OTP] {Channel} → {Destination}: {Code}", channel, destination, code);
        return Task.CompletedTask;
    }
}

public sealed class OtpService(IConnectionMultiplexer redis, IOtpSender sender, IHostEnvironment env)
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
        await sender.SendAsync(channel, destination, code);
        if (env.IsDevelopment())
        {
            await Db.StringSetAsync($"dev:otp:{destination}", code, TimeSpan.FromMinutes(5));
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
