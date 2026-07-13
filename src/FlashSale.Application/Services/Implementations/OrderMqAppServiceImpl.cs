using System.Text.Json;
using FlashSale.Application.Services;
using FlashSale.Contracts.Messages;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// OrderMQ app service — async place order via Kafka outbox pattern.
/// <para>
/// Pipeline (mirrors Java <c>OrderMQAppServiceImpl.placeOrderMQ</c> lines 38-104):
/// </para>
/// <list type="number">
///   <item>Atomic Redis Lua decrement on <c>PRO_TICKET:{id}:stock_available</c>
///         (returns -1 cache-miss, 0 insufficient, 1 success).</item>
///   <item>Cache miss → warm from MySQL + retry Lua once.</item>
///   <item>Stock insufficient → return failed <c>OUT_OF_STOCK</c> queue.</item>
///   <item>Lookup unit price via <c>GetEffectivePriceAsync</c>; if ≤ 0 compensate Redis.</item>
///   <item>Open EF transaction:
///         <list type="bullet">
///           <item>INSERT <c>order_queue</c> with token, status=PENDING.</item>
///           <item>INSERT <c>outbox_event</c> with payload JSON of
///                 <see cref="PlaceOrderMqMessage"/>.</item>
///         </list>
///   </item>
///   <item>Transaction failure → compensate Redis (return stock) and return
///         failed <c>INTERNAL_ERROR</c> queue.</item>
/// </list>
/// <para>
/// Token format: <c>MQ-</c> + first 16 hex chars of <see cref="Guid.NewGuid"/>
/// with dashes removed — mirrors Java line 63.
/// </para>
/// <para>
/// All MQ writes are committed by the outbox publisher worker (TASK-017);
/// this service never talks to Kafka directly.
/// </para>
/// </summary>
public sealed class OrderMqAppServiceImpl : IOrderMqAppService
{
    // status constants — mirror Java OrderMQAppService line 113.
    public const int Pending = 0;
    public const int Success = 1;
    public const int Failed = 2;

    private readonly IStockOrderCacheService _stockCache;
    private readonly IOrderQueueRepository _orderQueues;
    private readonly IOrderMqTransactionService _txService;
    private readonly ILogger<OrderMqAppServiceImpl> _log;

    public OrderMqAppServiceImpl(
        IStockOrderCacheService stockCache,
        IOrderQueueRepository orderQueues,
        IOrderMqTransactionService txService,
        ILogger<OrderMqAppServiceImpl> log)
    {
        _stockCache = stockCache;
        _orderQueues = orderQueues;
        _txService = txService;
        _log = log;
    }

    public async Task<OrderQueue> PlaceOrderMqAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0)
            return FailedQueue("INVALID_QUANTITY", "Số lượng phải lớn hơn 0");

        // 1. Redis Lua gate.
        var redisResult = await _stockCache.DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
        if (redisResult == -1)
        {
            _log.LogInformation("placeOrderMQ: cache miss ticketId={TicketId}, warming", ticketId);
            var warmed = await _stockCache.AddStockAvailableToCacheAsync(ticketId, ct);
            if (!warmed)
                return FailedQueue("TICKET_NOT_FOUND", "Không tìm thấy sự kiện");
            redisResult = await _stockCache.DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
        }
        if (redisResult == 0)
        {
            _log.LogInformation("placeOrderMQ: Redis OOS ticketId={TicketId}", ticketId);
            return FailedQueue("OUT_OF_STOCK", "Hết vé");
        }

        // 2. Unit price lookup.
        var unitPrice = await _stockCache.GetEffectivePriceAsync(ticketId, ct);
        if (unitPrice <= 0)
        {
            await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
            _log.LogWarning("placeOrderMQ: price not found ticketId={TicketId}", ticketId);
            return FailedQueue("PRICE_NOT_FOUND", "Không thể xác định giá vé");
        }

        // 3. Build token + random userId (matches Java quirk ThreadLocalRandom.nextInt(1,10)).
        var token = GenerateToken();
        var userId = Random.Shared.Next(1, 10);
        var now = DateTime.UtcNow;

        // 4. Atomic INSERT order_queue + INSERT outbox_event in 1 transaction.
        // Java uses TransactionTemplate (line 66). .NET equivalent is the
        // IOrderMqTransactionService abstraction (impl in Infrastructure opens
        // an EF IDbContextTransaction and commits both inserts). Any throw
        // rolls back both rows.
        try
        {
            var queue = new OrderQueue
            {
                Token = token,
                TicketId = (int)ticketId,
                Quantity = quantity,
                UserId = userId,
                Status = Pending,
                CreatedAt = now,
                UpdatedAt = now,
            };

            var message = new PlaceOrderMqMessage(
                Token: token,
                TicketId: ticketId,
                Quantity: quantity,
                UserId: userId,
                UnitPrice: unitPrice,
                CreatedAt: new DateTimeOffset(now).ToUnixTimeMilliseconds());

            var outbox = new OutboxEvent
            {
                AggregateId = token,
                EventType = "ORDER_PLACED",
                Payload = JsonSerializer.Serialize(message),
                Status = Pending,
                CreatedAt = now,
            };
            await _txService.PersistAsync(queue, outbox, ct);

            _log.LogInformation("placeOrderMQ: queued token={Token} ticketId={TicketId} qty={Qty}",
                token, ticketId, quantity);
            return queue;
        }
        catch (Exception ex)
        {
            // Transaction rolled back — compensate Redis so we don't strand stock.
            await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
            _log.LogError(ex, "placeOrderMQ: transaction failed, compensated Redis ticketId={TicketId}", ticketId);
            return FailedQueue("INTERNAL_ERROR", "Lỗi hệ thống, vui lòng thử lại");
        }
    }

    public Task<OrderQueue?> GetOrderStatusAsync(string token, CancellationToken ct = default)
        => _orderQueues.GetByTokenAsync(token, ct);

    // ============== Helpers ==============

    /// <summary>
    /// Build <c>MQ-</c> + first 16 hex chars of a freshly-generated UUID
    /// (dashes stripped). Mirrors Java line 63:
    /// <c>"MQ-" + UUID.randomUUID().toString().replace("-","").substring(0,16)</c>.
    /// Public to allow unit tests to assert the format directly.
    /// </summary>
    public static string GenerateToken()
    {
        var hex = Guid.NewGuid().ToString("N"); // 32 hex chars, no dashes
        return "MQ-" + hex[..16];
    }

    /// <summary>
    /// Build a "failed" response that the controller maps to <c>success:false</c>.
    /// Mirrors Java's <c>failedQueue(code, msg)</c> helper (lines 111-115).
    /// </summary>
    private static OrderQueue FailedQueue(string code, string message) => new()
    {
        Status = Failed,
        Message = $"{code}: {message}",
    };
}