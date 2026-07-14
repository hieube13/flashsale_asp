using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;

namespace FlashSale.Api.Stubs;

/// <summary>
/// Stub services — placeholder only.
/// Real implementations are added in subsequent tasks:
///   TASK-011: Catalog (ITicketAppService, ITicketDetailAppService)
///   TASK-013: Order CAS slice (ITicketOrderAppService.PlaceOrderCasAsync, DecreaseStock*)
///   TASK-014: Order cancel slice (ITicketOrderAppService.CancelOrderAsync)
///   TASK-015: OrderMQ producer (IOrderMqAppService)
///   TASK-016: OrderMQ consumer (IOrderMqConsumerHandler)
///   TASK-018: PAYMENT (IPaymentAppService) — DONE: real impl in
///     FlashSale.Application.Services.Implementations.PaymentAppServiceImpl
///   TASK-019: EMPLOYEE TIMSHEET (IEmployeeCacheService) — DONE: real impl in
///     FlashSale.Application.Services.Implementations.EmployeeCacheServiceImpl
///   TASK-020: Booking (IBookingAppService)
/// </summary>

public sealed class TicketAppServiceStub : ITicketAppService
{
    public Task<IReadOnlyList<TicketDto>> GetAllActiveAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TicketDto> GetByIdAsync(long ticketId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TicketDto> CreateAsync(CreateTicketRequest ticket, CreateTicketDetailRequest detail, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TicketDto> UpdateAsync(long ticketId, UpdateTicketRequest req, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TicketDto> ActivateAsync(long ticketId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TicketDto> DeactivateAsync(long ticketId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(long ticketId, CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class TicketDetailAppServiceStub : ITicketDetailAppService
{
    public Task<TicketDetailDto> GetByIdAsync(long detailId, long? version, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> OrderByUserAsync(long detailId, CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class BookingAppServiceStub : IBookingAppService
{
    public Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class EventAppServiceStub : IEventAppService
{
    public string SayHi(string name) => $"Hi {name}";
}