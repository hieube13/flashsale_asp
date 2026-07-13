using FlashSale.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Data;

/// <summary>
/// EF Core DbContext — static tables only.
/// Dynamic monthly tables (ticket_order_yyyyMM) are handled by Dapper, not EF.
/// </summary>
public class FlashSaleDbContext : DbContext
{
    public FlashSaleDbContext(DbContextOptions<FlashSaleDbContext> options) : base(options) { }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketDetail> TicketDetails => Set<TicketDetail>();
    public DbSet<OrderQueue> OrderQueues => Set<OrderQueue>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<Ticket>(e =>
        {
            e.ToTable("ticket");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasColumnType("TEXT");
            e.Property(x => x.StartTime).HasColumnType("DATETIME");
            e.Property(x => x.EndTime).HasColumnType("DATETIME");
            e.Property(x => x.Status).HasDefaultValue(0);
            e.Property(x => x.UpdatedAt).HasColumnType("DATETIME").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.CreatedAt).HasColumnType("DATETIME").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        mb.Entity<TicketDetail>(e =>
        {
            e.ToTable("ticket_item");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.StockInitial).HasDefaultValue(0);
            e.Property(x => x.StockAvailable).HasDefaultValue(0);
            e.Property(x => x.PriceOriginal).HasColumnType("BIGINT");
            e.Property(x => x.PriceFlash).HasColumnType("BIGINT");
            e.Property(x => x.SaleStartTime).HasColumnType("DATETIME");
            e.Property(x => x.SaleEndTime).HasColumnType("DATETIME");
            e.Property(x => x.ActivityId);
            e.Property(x => x.Status).HasDefaultValue(0);
            e.Property(x => x.UpdatedAt).HasColumnType("DATETIME").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.CreatedAt).HasColumnType("DATETIME").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        mb.Entity<OrderQueue>(e =>
        {
            e.ToTable("order_queue");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
        });

        mb.Entity<OutboxEvent>(e =>
        {
            e.ToTable("outbox_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.AggregateId).HasMaxLength(64).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Payload).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.Status).HasDefaultValue(0);
            e.HasIndex(x => new { x.Status, x.CreatedAt }, "idx_status_created");
        });

        mb.Entity<IdempotencyKey>(e =>
        {
            e.ToTable("idempotency_key");
            e.HasKey(x => x.Token);
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.ExpiresAt);
        });

        mb.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("payment_transaction");
            e.HasKey(x => x.Id);
            e.Property(x => x.PaymentId).HasMaxLength(64).IsRequired();
            e.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            e.Property(x => x.Amount).HasColumnType("DECIMAL(16,3)");
            e.Property(x => x.PaymentMethod).HasMaxLength(20).IsRequired();
            e.Property(x => x.PaymentUrl).HasColumnType("TEXT");
            e.HasIndex(x => x.PaymentId).IsUnique();
        });

        mb.Entity<Booking>(e =>
        {
            e.ToTable("booking");
            e.HasKey(x => x.Id);
            e.Property(x => x.BookingCode).HasMaxLength(64);
            e.Property(x => x.Status).HasDefaultValue(0);
        });
    }
}