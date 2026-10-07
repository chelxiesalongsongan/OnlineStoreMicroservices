using OrderService.Clients.Generated;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.Clients;

public record ChargeOutcome(bool Success, int? PaymentId, string? Reason);

public interface IPaymentGateway
{
    Task<ChargeOutcome> ChargeAsync(int orderId, decimal amount, bool forceFailure, CancellationToken ct);
    Task RefundAsync(int paymentId, CancellationToken ct);
}

public class PaymentGateway(IPaymentClient client, ILogger<PaymentGateway> log) : IPaymentGateway
{
    public async Task<ChargeOutcome> ChargeAsync(int orderId, decimal amount, bool forceFailure, CancellationToken ct)
    {
        try
        {
            var p = await client.ChargePaymentAsync(
                new ChargeRequest { OrderId = orderId, Amount = (double)amount, ForceFailure = forceFailure }, ct);
            return new(true, p.Id, null);
        }
        catch (ApiException ex) when (ex.StatusCode == 402)
        {
            return new(false, null, "Payment declined");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException
                                      or TimeoutRejectedException or BrokenCircuitException)
        {
            log.LogError(ex, "Payment service unavailable for order {OrderId}", orderId);
            return new(false, null, "Payment service unavailable");   // treated as failure => compensate
        }
    }

    public async Task RefundAsync(int paymentId, CancellationToken ct) =>
        await client.RefundPaymentAsync(paymentId, ct);
}
