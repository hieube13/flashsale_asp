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
///     <c>ticket_order_{yyyyMM}</c>. Order number format and the random userId quirk
///     are preserved verbatim from Java — see <c>KNOWN_DIFFERENCES.md</c> entries
///     2 (order number prefix) and 3 (ThreadLocalRandom userId).</item>
/// </list>
/// Methods still owned by later tasks throw <see cref="NotImplementedException"/>:
/// <c>DecreaseStockQueueAsync</c> → TASK-015, <c>CancelOrderAsync</c> → TASK-014.
/// </summary>
public sealed class TicketOrderAppServiceImpl : ITicketOrderAppService
{
    private readonly ITickerOrderRepository _orders;
    private readonly IOrderDeductionDomainService _domain;
    private readonly ITicketDetailRepository _details;
    private readonly IStockOrderCacheService _stockCache;
    private readonly ILogger<TicketOrderAppServiceImpl> _log;

    private static int _orderSeq;

    public TicketOrderAppServiceImpl(
        ITickerOrderRepository orders,
        IOrderDeductionDomainService domain,
        ITicketDetailRepository details,
        IStockOrderCacheService stockCache,
        ILogger<TicketOrderAppServiceImpl> log)
    {
        _orders = orders;
        _domain = domain;
        _details = details;
        _stockCache = stockCache;
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

    // ============== Stubs until later tasks port them ==============

    public Task<bool> DecreaseStockQueueAsync(long userId, long ticketId, int quantity, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-015: order MQ producer");
    public Task<bool> CancelOrderAsync(long userId, string orderNumber, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-014: order cancel slice");

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