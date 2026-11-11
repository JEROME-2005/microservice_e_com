namespace LankaMart.Payments.Api.Domain;

public enum PaymentStatus { Created, Processing, Succeeded, Failed, RefundRequired }

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "LKR";
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public string Gateway { get; set; } = "mock";
    public string? GatewayRef { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One business event that moves money (CHARGE now; REFUND, COMMISSION, PAYOUT in later phases). Contains balanced entries.</summary>
public class LedgerTransaction
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public string Type { get; set; } = "CHARGE";
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "LKR";
    public string GatewayRef { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";   // UNIQUE → the same charge can never be booked twice
    public DateTimeOffset CreatedAt { get; set; }
    public List<LedgerEntry> Entries { get; set; } = [];
}

public class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string Account { get; set; } = "";                // gateway_receivable | escrow | seller:{id} | platform_revenue | gateway_fees
    public long DebitCents { get; set; }
    public long CreditCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class WebhookEvent        // raw evidence, kept 90 days for disputes (architecture doc §4.6)
{
    public Guid Id { get; set; }
    public string Gateway { get; set; } = "";
    public Guid GatewayEventId { get; set; }
    public string Type { get; set; } = "";
    public string RawPayload { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}
