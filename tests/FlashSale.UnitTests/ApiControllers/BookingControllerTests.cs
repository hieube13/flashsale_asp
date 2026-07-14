using FlashSale.Api.Controllers;
using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Api;

public class BookingControllerTests
{
    private readonly Mock<IBookingAppService> _svc = new(MockBehavior.Strict);
    private readonly BookingController _sut;

    public BookingControllerTests()
    {
        _sut = new BookingController(_svc.Object, NullLogger<BookingController>.Instance);
    }

    [Fact]
    public async Task Create_returns_400_when_body_null()
    {
        var result = await _sut.CreateAsync(null, CancellationToken.None);

        Assert.IsType<ResultMessage<BookingDto>>(result);
        Assert.False(result.Success);
        Assert.Equal(400, result.Code);
        _svc.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_returns_400_when_service_throws_ArgumentException()
    {
        _svc.Setup(s => s.CreateAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("quantity must be 1..10"));

        var result = await _sut.CreateAsync(new CreateBookingRequest(1, 0), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(400, result.Code);
        Assert.Equal("quantity must be 1..10", result.Message);
    }

    [Fact]
    public async Task Create_returns_500_when_service_throws_unexpected()
    {
        _svc.Setup(s => s.CreateAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _sut.CreateAsync(new CreateBookingRequest(1, 1), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(500, result.Code);
        Assert.Equal("internal", result.Message);  // body shape mirrors Java ResultUtil.error
    }

    [Fact]
    public async Task Create_returns_data_envelope_on_success()
    {
        var dto = new BookingDto(Id: 7, TicketId: 1, Quantity: 2, BookingCode: "BK1ABCD", Status: 1);
        _svc.Setup(s => s.CreateAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _sut.CreateAsync(new CreateBookingRequest(1, 2), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Same(dto, result.Result);
    }
}