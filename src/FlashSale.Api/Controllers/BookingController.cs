using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using Microsoft.AspNetCore.Mvc;

namespace FlashSale.Api.Controllers;

/// <summary>
/// Booking controller — TASK-020 (port of Java <c>BookingController</c>).
///
/// Java exposes a single POST endpoint under <c>/api/bookings</c> (no GET).
/// Validation rules mirror Java <c>BookingDomainServiceImpl.createBooking</c>
/// lines 22-29: <c>ticketId</c> &gt; 0, <c>quantity</c> in 1..10.
/// Every overload returns <see cref="ResultMessage{T}"/> with HTTP 200 (matches our
/// cross-cutting convention + Java's <c>ResultUtil.data/error</c> wrapper).
/// </summary>
[ApiController]
[Route("api/bookings")]
public sealed class BookingController : ControllerBase
{
    private readonly IBookingAppService _service;
    private readonly ILogger<BookingController> _log;

    public BookingController(IBookingAppService service, ILogger<BookingController> log)
    {
        _service = service;
        _log = log;
    }

    /// <summary>POST /api/bookings — create one booking record (CONFIRMED).</summary>
    [HttpPost]
    public async Task<ResultMessage<BookingDto>> CreateAsync(
        [FromBody] CreateBookingRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return ResultMessage<BookingDto>.Error(400, "request body required");

        try
        {
            var dto = await _service.CreateAsync(request, ct);
            return ResultMessage<BookingDto>.Data(dto, "Booking created");
        }
        catch (ArgumentException ex)
        {
            _log.LogWarning(ex, "CreateBooking invalid input");
            return ResultMessage<BookingDto>.Error(400, ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CreateBooking unhandled");
            return ResultMessage<BookingDto>.Error(500, "internal");
        }
    }
}
