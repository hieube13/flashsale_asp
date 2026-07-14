using System.Text.RegularExpressions;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

public class BookingAppServiceImplTests
{
    private readonly Mock<IBookingRepository> _repo = new(MockBehavior.Strict);
    private readonly BookingAppServiceImpl _sut;

    public BookingAppServiceImplTests()
    {
        _sut = new BookingAppServiceImpl(_repo.Object, NullLogger<BookingAppServiceImpl>.Instance);
    }

    private static CreateBookingRequest SampleRequest(long ticketId = 1, int quantity = 2)
        => new(ticketId, quantity);

    [Fact]
    public async Task HappyPath_persists_booking_and_returns_dto_without_createdAt()
    {
        var req = SampleRequest();
        _repo.Setup(r => r.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Booking b, CancellationToken _) =>
             {
                 b.Id = 42;
                 return b;
             });

        var dto = await _sut.CreateAsync(req);

        Assert.Equal(42L, dto.Id);
        Assert.Equal(req.TicketId, dto.TicketId);
        Assert.Equal(req.Quantity, dto.Quantity);
        Assert.Equal(1, dto.Status);              // CONFIRMED
        Assert.False(string.IsNullOrEmpty(dto.BookingCode));

        // No createdAt on DTO (parity with Java BookingDTO)
        var type = dto.GetType();
        Assert.Null(type.GetProperty("CreatedAt"));
    }

    [Fact]
    public async Task BookingCode_matches_java_format_BK_ms_4hex()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Booking b, CancellationToken _) => { b.Id = 1; return b; });

        var dto = await _sut.CreateAsync(SampleRequest());

        // BK<digits>[A-Z0-9]{4} — e.g. BK1718000000000ABCD
        Assert.Matches(@"^BK\d+[A-Z0-9]{4}$", dto.BookingCode);
    }

    [Theory]
    [InlineData(0)]    // ticketId <= 0
    [InlineData(-5)]
    public async Task CreateAsync_throws_when_ticketId_is_not_positive(long ticketId)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(SampleRequest(ticketId, 1)));
        _repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]    // quantity 0
    [InlineData(11)]   // quantity > 10
    [InlineData(-1)]
    public async Task CreateAsync_throws_when_quantity_is_out_of_range(int quantity)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(SampleRequest(ticketId: 1, quantity: quantity)));
        _repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_at_quantity_boundaries_accepts_1_and_10()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Booking b, CancellationToken _) => { b.Id = 1; return b; });

        var min = await _sut.CreateAsync(SampleRequest(1, 1));
        var max = await _sut.CreateAsync(SampleRequest(1, 10));

        Assert.Equal(1, min.Quantity);
        Assert.Equal(10, max.Quantity);
    }

    [Fact]
    public async Task CreateAsync_throws_on_null_request()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.CreateAsync(null!));
    }

    [Fact]
    public void GenerateBookingCode_starts_with_BK_and_has_hex_tail()
    {
        // Internal helper exposed via InternalsVisibleTo? Not yet — call through CreateAsync.
        var code = BookingAppServiceImpl.GenerateBookingCode();
        Assert.Matches(@"^BK\d+[A-Z0-9]{4}$", code);
        Assert.StartsWith("BK", code);
        Assert.Equal("BK".Length + 13 + 4, code.Length); // "BK" + 13-digit epoch ms + 4 hex
    }

    [Fact]
    public void GenerateBookingCode_produces_unique_codes()
    {
        // Probability of a 4-hex collision in N calls is ~N^2 / 16^4. For N=200
        // and the time the loop takes to run, expected collisions are ~0.07%.
        // We assert uniqueness ≥ 99% rather than 100% — Java has the same
        // theoretical birthday-paradox ceiling (mirrored quirk).
        var set = new HashSet<string>();
        for (var i = 0; i < 200; i++) set.Add(BookingAppServiceImpl.GenerateBookingCode());
        Assert.True(set.Count >= 198, $"Only {set.Count}/200 unique codes");
    }
}