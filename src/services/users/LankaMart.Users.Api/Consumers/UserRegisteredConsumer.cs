using LankaMart.Users.Api.Data;
using LankaMart.Users.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Users.Api.Consumers;

public sealed class UserRegisteredConsumer(UsersDb db, ILogger<UserRegisteredConsumer> log) : IConsumer<UserRegistered>
{
    public async Task Consume(ConsumeContext<UserRegistered> context)
    {
        var m = context.Message;
        // IDEMPOTENT: RabbitMQ may deliver twice. "If it already exists, do nothing" makes the second delivery harmless.
        if (m.Role == Roles.Seller)
        {
            if (await db.Sellers.AnyAsync(s => s.UserId == m.UserId))
            {
                return;
            }
            db.Sellers.Add(SellerProfile.Create(m.UserId, m.FullName, m.Email, m.Phone, m.OccurredAt));
        }
        else
        {
            if (await db.Customers.AnyAsync(c => c.UserId == m.UserId))
            {
                return;
            }
            db.Customers.Add(new CustomerProfile { UserId = m.UserId, Email = m.Email, Phone = m.Phone, FullName = m.FullName, PreferredLanguage = m.Language, CreatedAt = m.OccurredAt });
        }
        await db.SaveChangesAsync();      // a racing duplicate hits the PK → exception → MassTransit retries → next attempt sees "exists" and returns
        log.LogInformation("Profile created for {UserId} ({Role})", m.UserId, m.Role);
    }
}
