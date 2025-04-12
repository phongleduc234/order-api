// SharedContracts/Events/OrderEvents.cs
namespace SharedContracts.Events
{
    // ========== Order Service Events/Commands ==========

    /// <summary>Event published when a new order is created</summary>
    public record OrderCreated(Guid CorrelationId, Guid OrderId);

    /// <summary>Event published when an order is successfully compensated (canceled)</summary>
    public record OrderCompensated(Guid CorrelationId, Guid OrderId);

    /// <summary>Event published when an order is confirmed as complete</summary>
    public record OrderConfirmed(Guid CorrelationId, Guid OrderId);

    /// <summary>Command sent to request order cancellation</summary>
    public record CompensateOrder(Guid CorrelationId, Guid OrderId);

    /// <summary>Event published when order is fulfilled (all steps complete)</summary>
    public record OrderFulfilled(Guid CorrelationId, Guid OrderId);

    // ========== Payment Service Events/Commands ==========

    /// <summary>Command sent to request payment processing</summary>
    public record ProcessPaymentRequest(Guid CorrelationId, Guid OrderId, decimal Amount);

    /// <summary>Event published when payment is processed</summary>
    public record PaymentProcessed(Guid CorrelationId, Guid OrderId, bool Success);

    /// <summary>Command sent to request payment refund</summary>
    public record CompensatePayment(Guid CorrelationId, Guid OrderId);

    /// <summary>Event published when payment is compensated (refunded)</summary>
    public record PaymentCompensated(Guid CorrelationId, Guid OrderId, bool Success);

    // ========== Inventory Service Events/Commands ==========

    /// <summary>Command sent to request inventory reservation</summary>
    public record UpdateInventory(Guid CorrelationId, Guid OrderId, List<OrderItem> Items);

    /// <summary>Event published when inventory is updated</summary>
    public record InventoryUpdated(Guid CorrelationId, Guid OrderId, bool Success);

    /// <summary>Command sent to request inventory release</summary>
    public record CompensateInventory(Guid CorrelationId, Guid OrderId);

    /// <summary>Event published when inventory is compensated (released)</summary>
    public record InventoryCompensated(Guid CorrelationId, Guid OrderId, bool Success);

    /// <summary>Represents an item in an order with quantity information</summary>
    public class OrderItem
    {
        /// <summary>Product identifier</summary>
        public string ProductId { get; set; }

        /// <summary>Quantity of the product</summary>
        public int Quantity { get; set; }
    }
}