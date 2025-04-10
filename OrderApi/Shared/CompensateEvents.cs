namespace OrderApi.Shared
{
    public record OrderCreated(Guid CorrelationId, Guid OrderId);
    public record OrderCompensated(Guid CorrelationId, Guid OrderId);
}
