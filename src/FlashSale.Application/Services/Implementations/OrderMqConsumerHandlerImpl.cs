using System.Globalization;
using FlashSale.Application.Services;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// OrderMQ consumer handler — invoked by <c>KafkaOrderConsumerWorker</c> for
/// each dequeued <see cref="PlaceOrderMqMessage"/>.
/// <para>
/// Pipeline (mirrors Java <c>KafkaOrderConsumer.processOrder</c> lines 38-82):
/// </para>
/// <list type="number">
///   <item><b>Idempotency gate</b>: <c>IF NOT EXISTS ... INSERT INTO idempotency_key</c>
///         — returns true if new, false on duplicate (Kafka retry / rebalance).</item>
///   <item><b>DB stock decrement</b>: <c>ITicketDetailRepository.TryDecreaseStockAsync</c>
///         — atomic <c>UPDATE … WHERE stock_available &gt;= ?</c>. On 0 rows the
///         producer's Redis pre-deduct is compensated and the queue row flips to
///         FAILED with "Hết vé".</item>
///   <item><b>Order insert</b>: <c>IOrderDeductionDomainService.InsertOrder(yyyyMM, order)</c>
///         writes into the monthly shard <c>ticket_order_{yyyyMM}</c> via Dapper.</item>
///   <item><b>Status flip</b>: <c>IOrderQueueRepository.UpdateStatusAsync(token, 1, orderNumber, null)</c>.</item>
/// </list>
/// <para>
/// Order number format: <c>MQ-{userId}-{tsMillis}</c> (matches Java line 64).
/// </para>
/// <para>
/// At-least-once delivery + idempotency_key gate = exactly-once side-effect.
/// The Kafka consumer commits the offset only AFTER <c>ProcessAsync</c> returns
/// (successfully or with a handled failure) — see
/// <c>KafkaOrderConsumerWorker</c> in TASK-016.
/// </para>
/// </summary>
public sealed class OrderMqConsumerHandlerImpl : IOrderMqConsumerHandler
{
    public const int OrderPendingStatus = 0;
    public const int OrderSuccessStatus = 1;
    public const int OrderFailedStatus = 2;

    public const int QueuePendingStatus = 0;
    public const int QueueSuccessStatus = 1;
    public const int QueueFailedStatus = 2;

    private readonly IIdempotencyKeyRepository _idempotency;
    private readonly ITicketDetailRepository _details;
    private readonly IStockOrderCacheService _stockCache;
    private readonly IOrderQueueRepository _orderQueues;
    private readonly IOrderDeductionDomainService _deduction;
    private readonly ILogger<OrderMqConsumerHandlerImpl> _log;

    public OrderMqConsumerHandlerImpl(
        IIdempotencyKeyRepository idempotency,
        ITicketDetailRepository details,
        IStockOrderCacheService stockCache,
        IOrderQueueRepository orderQueues,
        IOrderDeductionDomainService deduction,
        ILogger<OrderMqConsumerHandlerImpl> log)
    {
        _idempotency = idempotency;
        _details = details;
        _stockCache = stockCache;
        _orderQueues = orderQueues;
        _deduction = deduction;
        _log = log;
    }

    public async Task ProcessAsync(PlaceOrderMqMessage message, CancellationToken ct = default)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        if (string.IsNullOrEmpty(message.Token))
            throw new ArgumentException("message.Token is required", nameof(message));

        var token = message.Token;
        var ticketId = message.TicketId;
        var quantity = message.Quantity;

        // 1. Idempotency gate (INSERT IGNORE).
        var isNew = await _idempotency.TryInsertAsync(token, DateTime.UtcNow.AddHours(24), ct);
        if (!isNew)
        {
            // Kafka retry / rebalance — silently skip without touching stock.
            _log.LogInformation("[IDEMPOTENCY] duplicate skip token={Token}", token);
            return;
        }

        _log.LogInformation("[MQ] processing token={Token} ticketId={TicketId} qty={Qty}",
            token, ticketId, quantity);

        // 2. Atomic DB stock decrement.
        var stockDecreased = await _details.TryDecreaseStockAsync(ticketId, quantity, ct);
        if (!stockDecreased)
        {
            // Producer pre-deducted Redis — return it so we don't double-deduct the
            // same ticket later, then flip the queue row to FAILED.
            await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
            await _orderQueues.UpdateStatusAsync(token, QueueFailedStatus, null, "Hết vé", ct);
            _log.LogWarning("[MQ] OOS token={Token} ticketId={TicketId}", token, ticketId);
            return;
        }

        // 3. Build the order row.
        var now = DateTime.UtcNow;
        var orderNumber = $"MQ-{message.UserId}-{new DateTimeOffset(now).ToUnixTimeMilliseconds()}";
        var order = new TickerOrder
        {
            UserId = message.UserId,
            TicketId = (int)ticketId,
            Quantity = quantity,
            OrderNumber = orderNumber,
            TotalAmount = (decimal)message.UnitPrice * quantity,
            OrderStatus = OrderPendingStatus, // 0=PENDING — matches "Order -> Pending" Java line 75
            TerminalId = "MQ-SGN",            // matches Java line 74
            OrderNotes = "MQ Order -> Pending", // matches Java line 75
            OrderDate = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // 4. Insert into monthly shard (Dapper dynamic table).
        var yearMonth = now.ToString("yyyyMM", CultureInfo.InvariantCulture);
        _deduction.InsertOrder(yearMonth, order);

        // 5. Flip queue row to SUCCESS + capture orderNumber.
        await _orderQueues.UpdateStatusAsync(token, QueueSuccessStatus, orderNumber, null, ct);

        _log.LogInformation("[MQ] success token={Token} orderNumber={OrderNumber}", token, orderNumber);
    }
}