namespace OrderApi.Shared
{
    public record OrderCreated(Guid CorrelationId, Guid OrderId);
    public record OrderCompensated(Guid CorrelationId, Guid OrderId);
    public record ProcessPayment(Guid CorrelationId, Guid OrderId);
    public record PaymentProcessed(Guid CorrelationId, bool Success);
    public record UpdateInventory(Guid CorrelationId, Guid OrderId);
    public record InventoryUpdated(Guid CorrelationId, bool Success);
    public record CompensatePayment(Guid CorrelationId, Guid OrderId);
    public record CompensateInventory(Guid CorrelationId, Guid OrderId);
}
