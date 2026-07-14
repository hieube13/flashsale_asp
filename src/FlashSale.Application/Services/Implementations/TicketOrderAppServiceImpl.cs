using System.Globalization;
using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Order app service.
/// <list type="bullet">
///   <item>TASK-012 — findAll / findPage / findByOrderNumber (Dapper dynamic table).</item>
///   <item>TASK-013 — placeOrderCAS / decreaseStockLevel3CAS / decreaseStockLevel1 / getStockAvailable.
///     Redis Lua atomic decrement + MySQL FOR UPDATE safety net + Dapper insert into
///     <c>ticket_order_{yyyyMM}</c>.</item>
///   <item>TASK-014 — cancelOrder: distributed lock (key per orderNumber) +
///     ownership check + idempotent status update + DB stock restore (EF Core)
///     + Redis stock restore (best-effort). Mirrors Java TicketOrderAppServiceImpl
///     lines 439-511.</item>
/// </list>
/// Methods still owned by later tasks throw <see cref="NotImplementedException"/>:
/// <c>DecreaseStockQueueAsync</c> → TASK-015.
/// </summary>
public sealed class TicketOrderAppServiceImpl : ITicketOrderAppService
{
    private readonly ITickerOrderRepository _orders;
    private readonly IOrderDeductionDomainService _domain;
    private readonly ITicketDetailRepository _details;
    private readonly IStockOrderCacheService _stockCache;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<TicketOrderAppServiceImpl> _log;

    private static int _orderSeq;

    public TicketOrderAppServiceImpl(
        ITickerOrderRepository orders,
        IOrderDeductionDomainService domain,
        ITicketDetailRepository details,
        IStockOrderCacheService stockCache,
        IDistributedLockProvider lockProvider,
        ILogger<TicketOrderAppServiceImpl> log)
    {
        _orders = orders;
        _domain = domain;
        _details = details;
        _stockCache = stockCache;
        _lockProvider = lockProvider;
        _log = log;
    }

    // ============== TASK-012: read slice ==============

    public async Task<IReadOnlyList<TicketOrderDto>> FindAllAsync(string yearMonth, CancellationToken ct = default)
    {
        var rows = await _orders.FindAllAsync(yearMonth, ct);
        _log.LogInformation("FindAll yearMonth={YearMonth} count={Count}", yearMonth, rows.Count);
        return rows.Select(MapRowToDto).ToList();
    }

    public async Task<PagedOrdersDto> FindPageAsync(string yearMonth, long lastId, int limit, CancellationToken ct = default)
    {
        var safeLimit = limit <= 0 ? 20 : Math.Min(limit, 200);
        var rows = await _orders.FindPageAsync(yearMonth, lastId, safeLimit, ct);
        var items = rows.Select(MapRowToDto).ToList();
        var nextCursor = items.Count == safeLimit ? items[^1].Id : (int?)null;
        _log.LogInformation("FindPage yearMonth={YearMonth} lastId={LastId} count={Count}", yearMonth, lastId, items.Count);
        return new PagedOrdersDto(items, nextCursor, nextCursor.HasValue);
    }

    public async Task<TicketOrderDto?> FindByOrderNumberAsync(string yearMonth, string orderNumber, CancellationToken ct = default)
    {
        var resolvedYearMonth = _domain.ExtractYearMonth(orderNumber);
        var row = await _orders.FindByOrderNumberAsync(resolvedYearMonth, orderNumber, ct);
        if (row is null)
        {
            _log.LogWarning("Order not found: {OrderNumber}", orderNumber);
            return null;
        }
        return MapRowToDto(row);
    }

    // ============== TASK-013: order CAS slice ==============

    /// <summary>
    /// Java-level L1: pure MySQL pessimistic decrement.
    /// Used as the DB safety net behind the Redis Lua atomic gate.
    /// </summary>
    public async Task<bool> DecreaseStockLevel1Async(long ticketId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) return false;
        var stock = await _details.GetStockAvailableAsync(ticketId, ct);
        if (stock < quantity)
        {
            _log.LogInformation("L1: stockAvailable < quantity | {Stock} < {Qty}", stock, quantity);
            return false;
        }
        var ok = await _details.TryDecreaseStockAsync(ticketId, quantity, ct);
        _log.LogInformation("L1 decrease ticket={TicketId} qty={Qty} result={Result}", ticketId, quantity, ok);
        return ok;
    }

    /// <summary>
    /// Java <c>decreaseStockLevel3CAS</c> — Redis Lua atomic decrement → DB safety net
    /// → insert order row. Used by the GET /order/{ticketId}/{quantity}/cas demo route.
    /// On any failure after Redis-decrement, restore Redis by <c>IncreaseStockCache</c>.
    /// </summary>
    public async Task<bool> DecreaseStockLevel3CasAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) return false;
        var redisDecremented = false;
        try
        {
            var redisResult = await _stockCache.DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
            if (redisResult == -1)
            {
                _log.LogInformation("L3: cache miss for ticketId={TicketId}, warming up", ticketId);
                await _stockCache.AddStockAvailableToCacheAsync(ticketId, ct);
                redisResult = await _stockCache.DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
            }
            if (redisResult == 0)
            {
                _log.LogInformation("L3: Redis stock insufficient for ticketId={TicketId}", ticketId);
                return false;
            }
            redisDecremented = true;

            var dbOk = await DecreaseStockLevel1Async(ticketId, quantity, ct);
            if (!dbOk)
            {
                await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
                _log.LogWarning("L3: DB update failed, rolled back Redis for ticketId={TicketId}", ticketId);
                return false;
            }

            var unitPrice = await _stockCache.GetEffectivePriceAsync(ticketId, ct);
            if (unitPrice <= 0)
            {
                await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
                _log.LogWarning("L3: price not found for ticketId={TicketId}, rolled back Redis", ticketId);
                return false;
            }

            var order = BuildCasOrder(ticketId, quantity, unitPrice, out var orderNumber);
            var ym = DateTime.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture);
            await _orders.InsertAsync(ym, order, ct);

            _log.LogInformation("L3 CAS ok ticket={TicketId} order={OrderNumber}", ticketId, orderNumber);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "L3: unexpected error for ticketId={TicketId}", ticketId);
            if (redisDecremented) await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
            return false;
        }
    }

    /// <summary>
    /// Java <c>placeOrderCAS</c> — the public order endpoint. Same Redis-Lua + DB-safety-net
    /// flow as L3 but returns a <see cref="PlaceOrderResponse"/> with descriptive failure codes
    /// (TICKET_NOT_FOUND / OUT_OF_STOCK / STOCK_CONFLICT / PRICE_NOT_FOUND / SERVER_ERROR).
    /// </summary>
    public async Task<PlaceOrderResponse> PlaceOrderCasAsync(long ticketId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0)
            return PlaceOrderResponse.Failed("INVALID_QUANTITY", "Quantity must be positive");

        var redisDecremented = false;
        try
        {
            var redisResult = await _stockCache.DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
            if (redisResult == -1)
            {
                _log.LogInformation("placeOrderCAS: cache miss for ticketId={TicketId}, warming up", ticketId);
                var warmed = await _stockCache.AddStockAvailableToCacheAsync(ticketId, ct);
                if (!warmed)
                    return PlaceOrderResponse.Failed("TICKET_NOT_FOUND", "Không tìm thấy sự kiện");
                redisResult = await _stockCache.DecreaseStockCacheByLuaAsync(ticketId, quantity, ct);
            }
            if (redisResult == 0)
            {
                _log.LogInformation("placeOrderCAS: Redis stock insufficient for ticketId={TicketId}", ticketId);
                return PlaceOrderResponse.Failed("OUT_OF_STOCK", "Hết vé, vui lòng thử lại sau");
            }
            redisDecremented = true;

            var dbOk = await DecreaseStockLevel1Async(ticketId, quantity, ct);
            if (!dbOk)
            {
                await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
                _log.LogWarning("placeOrderCAS: DB update failed, rolled back Redis for ticketId={TicketId}", ticketId);
                return PlaceOrderResponse.Failed("STOCK_CONFLICT", "Đặt vé không thành công, vui lòng thử lại");
            }

            var unitPrice = await _stockCache.GetEffectivePriceAsync(ticketId, ct);
            if (unitPrice <= 0)
            {
                await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
                _log.LogWarning("placeOrderCAS: price not found for ticketId={TicketId}, rolled back Redis", ticketId);
                return PlaceOrderResponse.Failed("PRICE_NOT_FOUND", "Không thể xác định giá vé");
            }

            var order = BuildCasOrder(ticketId, quantity, unitPrice, out var orderNumber);
            var ym = DateTime.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture);
            await _orders.InsertAsync(ym, order, ct);

            _log.LogInformation("placeOrderCAS: success ticket={TicketId} order={OrderNumber}", ticketId, orderNumber);
            return PlaceOrderResponse.Ok(orderNumber);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "placeOrderCAS: error for ticketId={TicketId}", ticketId);
            if (redisDecremented) await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
            return PlaceOrderResponse.Failed("SERVER_ERROR", "Lỗi hệ thống, vui lòng thử lại");
        }
    }

    public async Task<int> GetStockAvailableAsync(long ticketId, CancellationToken ct = default)
        => await _details.GetStockAvailableAsync(ticketId, ct);

    // ============== TASK-014: order cancel slice ==============

    /// <summary>
    /// Cancel an order under a distributed lock so two concurrent cancels
    /// cannot double-restore the stock.
    /// <para/>
    /// Pipeline (mirrors Java TicketOrderAppServiceImpl.cancelOrder lines 439-511):
    /// <list type="number">
    ///   <item>Acquire Redis lock at <c>LOCK:CANCEL_ORDER:{orderNumber}</c> (wait 1s, expiry 5s).</item>
    ///   <item>Resolve year-month shard name from order_number trailing ts segment.</item>
    ///   <item>Fetch order row by order_number; ownership check.</item>
    ///   <item>If already CANCELLED (status=2) → idempotent return <c>true</c>.</item>
    ///   <item>Set status=2 via Dapper <c>UpdateStatusAsync</c>.</item>
    ///   <item>Restore DB stock via EF Core <c>IncreaseStockAsync</c>.</item>
    ///   <item>Best-effort Redis stock restore — failure logged not thrown
    ///         (matches Java's inconsistency tolerance).</item>
    /// </list>
    /// </summary>
    public async Task<bool> CancelOrderAsync(long userId, string orderNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(orderNumber))
            throw new ArgumentException("orderNumber is required", nameof(orderNumber));

        const int CancelledStatus = 2;
        var lockKey = $"LOCK:CANCEL_ORDER:{orderNumber}";
        var lockHandle = _lockProvider.GetLock(lockKey);

        // Java: tryLock(1, 5, TimeUnit.SECONDS) → wait=1s, expiry=5s
        var acquired = await lockHandle.TryAcquireAsync(
            expiry: TimeSpan.FromSeconds(5),
            wait: TimeSpan.FromSeconds(1),
            ct: ct);

        try
        {
            if (!acquired)
            {
                _log.LogWarning("cancelOrder: lock busy for {OrderNumber}", orderNumber);
                return false;
            }

            var yearMonth = _domain.ExtractYearMonth(orderNumber);
            var row = await _orders.FindByOrderNumberAsync(yearMonth, orderNumber, ct);
            if (row is null)
            {
                _log.LogWarning("cancelOrder: order not found {OrderNumber}", orderNumber);
                return false;
            }

            var order = MapRowToDto(row);
            if (order.UserId != (int)userId)
            {
                _log.LogError("cancelOrder: order {OrderNumber} does not belong to user {UserId}", orderNumber, userId);
                return false;
            }

            if (order.OrderStatus == CancelledStatus)
            {
                _log.LogInformation("cancelOrder: order already cancelled {OrderNumber}", orderNumber);
                return true;
            }

            var updated = await _orders.UpdateStatusAsync(yearMonth, orderNumber, CancelledStatus, ct);
            if (!updated)
            {
                _log.LogError("cancelOrder: failed to update status to CANCELLED for {OrderNumber}", orderNumber);
                return false;
            }

            var ticketId = (long)order.TicketId;
            var quantity = order.Quantity;

            // Restore DB stock. The TaskOrder .NET path uses EF Core
            // TryDecreaseStockAsync's inverse (IncreaseStockAsync) which does a
            // simple read-modify-write. This is intentionally non-atomic to
            // preserve parity with Java's `tickerOrderDomainService.increaseStock`
            // (also non-atomic by design). The repo does not return a bool —
            // any DB error propagates as an exception which is caught by the
            // outer try and surfaces as a 500 in the controller.
            await _details.IncreaseStockAsync(ticketId, quantity, ct);

            // Restore Redis (best-effort — Java only warns here too).
            var redisRecovered = await _stockCache.IncreaseStockCacheAsync(ticketId, quantity, ct);
            if (!redisRecovered)
            {
                _log.LogWarning("cancelOrder: Redis stock restore FAILED (inconsistency); order={OrderNumber}", orderNumber);
            }

            _log.LogInformation("cancelOrder: success {OrderNumber} ticketId={TicketId} qty={Qty}", orderNumber, ticketId, quantity);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "cancelOrder: unexpected error for {OrderNumber}", orderNumber);
            throw;
        }
        finally
        {
            await lockHandle.ReleaseAsync(ct);
        }
    }

    // ============== Stubs until later tasks port them ==============

    public async Task<bool> DecreaseStockQueueAsync(long userId, long ticketId, int quantity, CancellationToken ct = default)
    {
        _log.LogInformation("DecreaseStockQueue | userId={UserId} | ticketId={TicketId} | quantity={Quantity}",
            userId, ticketId, quantity);

        if (userId <= 0 || ticketId <= 0 || quantity <= 0)
            return false;

        // Mirrors Java TicketOrderAppServiceImpl.decreaseStockQueue lines 230-251:
        // Distributed lock on "TOKEN_LOCK_KEY:{ticketId}" (wait 1 s, expiry 5 s).
        // On success: call IOrderMqAppService.StartOrderByUserAsync(userId, ticketId, quantity).
        // Note: Java's mqPlaceOrderService.startOrderByUser always returns false (stub),
        // so the return value always matches.
        var lck = _lockProvider.GetLock($"TOKEN_LOCK_KEY:{ticketId}");
        var acquired = await lck.TryAcquireAsync(
            expiry: TimeSpan.FromSeconds(5),
            wait: TimeSpan.FromSeconds(1),
            ct);

        if (!acquired)
        {
            _log.LogWarning("DecreaseStockQueue lock not acquired for ticketId={TicketId}", ticketId);
            return false;
        }

        _log.LogInformation("DecreaseStockQueue lock acquired for ticketId={TicketId}, calling orderMQ", ticketId);
        try
        {
            // Java: return mqPlaceOrderService.startOrderByUser(userId, tickerId, quantity);
            // That method is a stub that always returns false — mirror exactly.
            return await _domain.StartOrderByUserAsync(userId, ticketId, quantity, ct);
        }
        finally
        {
            await lck.ReleaseAsync(ct);
        }
    }

    // ============== Helpers ==============

    /// <summary>
    /// Mirrors Java order-row construction in <c>placeOrderCAS</c> / <c>decreaseStockLevel3CAS</c>:
    ///   orderNumber = "OKX-SGN-{userId}-{seq}-{tsMillis}"
    ///   userId      = Random.Shared.Next(1, 10) — preserved quirk (KNOW_DIFF #3)
    ///   status      = 0 (PENDING)
    ///   terminalId  = "OKX-SGN"
    ///   orderNotes  = "Order -> Pending"
    /// </summary>
    private static TickerOrder BuildCasOrder(long ticketId, int quantity, long unitPrice, out string orderNumber)
    {
        var userId = Random.Shared.Next(1, 10);
        var seq = Interlocked.Increment(ref _orderSeq);
        var tsMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        orderNumber = $"OKX-SGN-{userId}-{seq}-{tsMillis}";

        return new TickerOrder
        {
            UserId = userId,
            TicketId = (int)ticketId,
            Quantity = quantity,
            OrderStatus = 0,
            OrderNumber = orderNumber,
            TotalAmount = unitPrice * quantity,
            TerminalId = "OKX-SGN",
            OrderDate = DateTime.UtcNow,
            OrderNotes = "Order -> Pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private static TicketOrderDto MapRowToDto(object?[] row) => new(
        Id: ToInt(row[0]),
        UserId: ToInt(row[1]),
        TicketId: ToInt(row[2]),
        Quantity: ToInt(row[3]),
        OrderStatus: ToInt(row[4]),
        OrderNumber: (string?)row[5] ?? string.Empty,
        TotalAmount: row[6] is null ? 0m : Convert.ToDecimal(row[6]),
        TerminalId: (string?)row[7] ?? string.Empty,
        OrderDate: ToDateTime(row[8]),
        OrderNotes: row[9] as string,
        UpdatedAt: ToDateTime(row[10]),
        CreatedAt: ToDateTime(row[11]));

    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    private static DateTime ToDateTime(object? v) => v switch
    {
        null => default,
        DateTime dt => dt,
        DateTimeOffset dto => dto.LocalDateTime,
        _ => Convert.ToDateTime(v),
    };
}