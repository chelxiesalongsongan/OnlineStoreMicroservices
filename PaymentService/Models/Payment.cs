using System.ComponentModel.DataAnnotations;

namespace PaymentService.Models;

public enum PaymentStatus { Pending, Charged, Failed, Refunded }

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
}

public class ChargeRequest
{
    [Range(1, int.MaxValue)] public int OrderId { get; set; }
    [Range(0.01, 1_000_000)] public decimal Amount { get; set; }
    public bool ForceFailure { get; set; }
}

public class UpdatePaymentRequest
{
    [EnumDataType(typeof(PaymentStatus))] public PaymentStatus Status { get; set; }
}
