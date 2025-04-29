using Microsoft.EntityFrameworkCore;
using OrderApi.Models;

namespace OrderApi.Data
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
        {
        }

        // Define your DbSets here
        public DbSet<Order> Orders { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<DeadLetterMessage> DeadLetterMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OutboxMessage>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.EventType).HasMaxLength(100);
            });

            modelBuilder.Entity<Order>().ToTable("Orders");
            modelBuilder.Entity<OutboxMessage>().ToTable("OutboxMessages");

            modelBuilder.Entity<Order>()
                .HasKey(o => o.Id);

            modelBuilder.Entity<DeadLetterMessage>()
                .HasKey(d => d.Id);
        }
    }

    public class Order
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string EventType { get; set; }
        public string EventData { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Processed { get; set; }
        public int RetryCount { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
