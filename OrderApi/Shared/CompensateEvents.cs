using MassTransit;

namespace OrderApi.Shared
{
    public record OrderCreated(Guid CorrelationId, Guid OrderId);
    public record OrderCompensated(Guid CorrelationId, Guid OrderId);

    public class OrderSagaState : SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; }
        public Guid OrderId { get; set; }
    }
}
