using System.Net.Http.Json;

namespace LankaMart.Orders.Api.Clients;

// Interfaces make CheckoutService testable: tests swap in fakes, no HTTP needed.
public interface ICatalogClient
{
    Task<IReadOnlyList<VariantInfo>> LookupAsync(IEnumerable<Guid> variantIds, CancellationToken ct);
}

public interface IInventoryClient
{
    Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest req, CancellationToken ct);
}

public interface IPaymentsClient
{
    Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct);
}

public sealed class CatalogClient(HttpClient http) : ICatalogClient
{
    public async Task<IReadOnlyList<VariantInfo>> LookupAsync(IEnumerable<Guid> variantIds, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync("/api/v1/internal/variants/lookup", new VariantLookupRequest(variantIds.Distinct().ToList()), ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<VariantLookupResponse>(ct))!.Variants;
    }
}

public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public async Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest req, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync("/api/v1/internal/inventory/reserve", req, ct);   // safe to retry: reservation id makes it idempotent
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ReserveStockResponse>(ct))!;
    }
}

public sealed class PaymentsClient(HttpClient http) : IPaymentsClient
{
    public async Task<CreatePaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest req, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync("/api/v1/internal/payments/intent", req, ct);     // safe to retry: Payments is idempotent on orderId
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CreatePaymentIntentResponse>(ct))!;
    }
}
