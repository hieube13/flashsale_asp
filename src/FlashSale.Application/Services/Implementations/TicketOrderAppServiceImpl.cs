using FlashSale.Contracts.Dto;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Order app service — TASK-012 ships the read slice.
/// Mirrors Java TicketOrderAppServiceImpl.findAll / findPage / findByOrderNumber.
/// Each method extracts the yearMonth shard name (caller-supplied or derived
/// from the order_number) then defers row fetching to
/// <see cref="ITickerOrderRepository"/> — column order matches the 12-column
/// projection the Java service was producing. Mutation methods throw
/// <see cref="NotImplementedException"/> until TASK-013/014 port them.
/// </summary>
public sealed class TicketOrderAppServiceImpl : ITicketOrderAppService
{
    private readonly ITickerOrderRepository _orders;
    private readonly IOrderDeductionDomainService _domain;
    private readonly ILogger<TicketOrderAppServiceImpl> _log;

    public TicketOrderAppServiceImpl(
        ITickerOrderRepository orders,
        IOrderDeductionDomainService domain,
        ILogger<TicketOrderAppServiceImpl> log)
    {
        _orders = orders;
        _domain = domain;
        _log = log;
    }

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
        // Java first extracts the shard name from the order_number, then queries
        // with that — caller-supplied yearMonth is ignored once parsed from
        // orderNumber, matching the behavioural quirk in
        // TicketOrderAppServiceImpl.findByOrderNumber.
        var resolvedYearMonth = _domain.ExtractYearMonth(orderNumber);
        var row = await _orders.FindByOrderNumberAsync(resolvedYearMonth, orderNumber, ct);
        if (row is null)
        {
            _log.LogWarning("Order not found: {OrderNumber}", orderNumber);
            return null;
        }
        return MapRowToDto(row);
    }

    // -----------------------------------------------------------------
    // Stubs until their respective tasks port them.
    // -----------------------------------------------------------------

    public Task<bool> DecreaseStockLevel1Async(long ticketId, int quantity, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-013: order CAS slice");
    public Task<bool> DecreaseStockLevel3CasAsync(long ticketId, int quantity, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-013: order CAS slice");
    public Task<PlaceOrderResponse> PlaceOrderCasAsync(long ticketId, int quantity, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-013: order CAS slice");
    public Task<bool> DecreaseStockQueueAsync(long userId, long ticketId, int quantity, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-015: order MQ producer");
    public Task<int> GetStockAvailableAsync(long ticketId, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-013: stock read combined with CAS slice");
    public Task<bool> CancelOrderAsync(long userId, string orderNumber, CancellationToken ct = default)
        => throw new NotImplementedException("TASK-014: order cancel slice");

    // -----------------------------------------------------------------
    // Row → DTO mapping. Column order is locked by Java
    // TicketOrderAppServiceImpl.findAll/findPage/findByOrderNumber.
    //   row[0]  id            (BIGINT)
    //   row[1]  user_id       (BIGINT)
    //   row[2]  ticket_id     (BIGINT)
    //   row[3]  quantity      (INT)
    //   row[4]  order_status  (INT)
    //   row[5]  order_number  (VARCHAR)
    //   row[6]  total_amount  (BIGINT/Decimal)
    //   row[7]  terminal_id   (VARCHAR)
    //   row[8]  order_date    (DATETIME)
    //   row[9]  order_notes   (VARCHAR, nullable)
    //   row[10] updated_at    (DATETIME)
    //   row[11] created_at    (DATETIME)
    // -----------------------------------------------------------------
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
