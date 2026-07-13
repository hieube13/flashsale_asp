using FlashSale.Domain.Entities;

namespace FlashSale.Domain.Repositories;

public interface ITicketRepository
{
    Task<Ticket?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Ticket>> GetActiveAsync(CancellationToken ct = default);
    Task<Ticket> AddAsync(Ticket ticket, CancellationToken ct = default);
    Task UpdateAsync(Ticket ticket, CancellationToken ct = default);
    Task SoftDeleteAsync(long id, CancellationToken ct = default);
}

public interface ITicketDetailRepository
{
    Task<TicketDetail?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<TicketDetail?> GetForUpdateAsync(long id, CancellationToken ct = default);
    Task<TicketDetail> AddAsync(TicketDetail detail, CancellationToken ct = default);
    Task UpdateAsync(TicketDetail detail, CancellationToken ct = default);
    Task IncreaseStockAsync(long id, int quantity, CancellationToken ct = default);
    Task<bool> TryDecreaseStockAsync(long id, int quantity, CancellationToken ct = default);
    Task<int> GetStockAvailableAsync(long id, CancellationToken ct = default);
}

public interface ITickerOrderRepository
{
    /// <summary>Dapper dynamic table — table = ticket_order_{yearMonth}</summary>
    Task InsertAsync(string yearMonth, TickerOrder order, CancellationToken ct = default);
    Task<object[]> FindByOrderNumberAsync(string yearMonth, string orderNumber, CancellationToken ct = default);
    Task<IReadOnlyList<object[]>> FindAllAsync(string yearMonth, CancellationToken ct = default);
    Task<IReadOnlyList<object[]>> FindPageAsync(string yearMonth, long lastId, int limit, CancellationToken ct = default);
    Task<bool> UpdateStatusAsync(string yearMonth, string orderNumber, int status, CancellationToken ct = default);
}

public interface IOrderQueueRepository
{
    Task<OrderQueue> AddAsync(OrderQueue queue, CancellationToken ct = default);
    Task<OrderQueue?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task UpdateStatusAsync(string token, int status, string? orderNumber, string? message, CancellationToken ct = default);
}

public interface IOutboxEventRepository
{
    Task<OutboxEvent> AddAsync(OutboxEvent ev, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxEvent>> FindPendingBatchAsync(int batchSize, CancellationToken ct = default);
    Task MarkPublishedAsync(long id, DateTime publishedAt, CancellationToken ct = default);
    Task MarkPublishedBatchAsync(IReadOnlyList<long> ids, DateTime publishedAt, CancellationToken ct = default);
}

public interface IIdempotencyKeyRepository
{
    /// <summary>Insert IGNORE — returns true if new, false if duplicate.</summary>
    Task<bool> TryInsertAsync(string token, DateTime expiresAt, CancellationToken ct = default);
}

public interface IPaymentRepository
{
    Task<PaymentTransaction> CreateAsync(PaymentTransaction tx, CancellationToken ct = default);
    Task UpdateInProgressAsync(string paymentId, string paymentUrl, CancellationToken ct = default);
    Task<PaymentTransaction?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default);
    Task UpdateStatusAsync(string paymentId, int status, string? gatewayTxId, CancellationToken ct = default);
}

public interface IBookingRepository
{
    Task<Booking> AddAsync(Booking booking, CancellationToken ct = default);
    Task<Booking?> GetByIdAsync(long id, CancellationToken ct = default);
}