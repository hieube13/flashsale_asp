using FlashSale.Contracts.Dto;

namespace FlashSale.Application.Services;

/// <summary>
/// Order service — mirrors Java TicketOrderAppService.
/// L1/L2/L3 = MySQL pessimistic / MySQL CAS / Redis Lua atomic.
/// </summary>
public interface ITicketOrderAppService
{
    Task<bool> DecreaseStockLevel1Async(long ticketId, int quantity, CancellationToken ct = default);
    Task<bool> DecreaseStockLevel3CasAsync(long ticketId, int quantity, CancellationToken ct = default);
    Task<PlaceOrderResponse> PlaceOrderCasAsync(long ticketId, int quantity, CancellationToken ct = default);
    Task<bool> DecreaseStockQueueAsync(long userId, long ticketId, int quantity, CancellationToken ct = default);
    Task<int> GetStockAvailableAsync(long ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<TicketOrderDto>> FindAllAsync(string yearMonth, CancellationToken ct = default);
    Task<PagedOrdersDto> FindPageAsync(string yearMonth, long lastId, int limit, CancellationToken ct = default);
    Task<TicketOrderDto?> FindByOrderNumberAsync(string yearMonth, string orderNumber, CancellationToken ct = default);
    Task<bool> CancelOrderAsync(long userId, string orderNumber, CancellationToken ct = default);
}