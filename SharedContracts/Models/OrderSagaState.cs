namespace SharedContracts.Models
{
    using MassTransit;
    public class OrderSagaState : SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; }
        public Guid OrderId { get; set; }

        // Bổ sung thông tin để theo dõi trạng thái của các bước trong saga
        public bool PaymentCompleted { get; set; }
        public bool InventoryUpdated { get; set; }
        public DateTime Created { get; set; }
        public DateTime? Updated { get; set; }
    }
}
