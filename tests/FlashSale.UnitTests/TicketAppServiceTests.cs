using FlashSale.Application.Services;
using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests;

/// <summary>
/// Unit tests for TicketAppServiceImpl — uses Moq to substitute
/// ITicketDomainService, ITicketDetailRepository, ITicketCacheService and
/// ITicketDetailCacheService. No MySQL/Redis/Kafka required.
/// </summary>
public class TicketAppServiceTests
{
    private readonly Mock<ITicketDomainService> _domain = new(MockBehavior.Strict);
    private readonly Mock<ITicketDetailRepository> _details = new(MockBehavior.Strict);
    private readonly Mock<ITicketCacheService> _ticketCache = new(MockBehavior.Strict);
    private readonly Mock<ITicketDetailCacheService> _detailCache = new(MockBehavior.Strict);

    private TicketAppServiceImpl BuildSut() =>
        new(_domain.Object, _details.Object, _ticketCache.Object, _detailCache.Object,
            NullLogger<TicketAppServiceImpl>.Instance);

    private static Ticket SampleTicket(long id = 1) => new()
    {
        Id = id, Name = "Concert", Description = "x",
        StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(2),
        Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static TicketDetail SampleDetail(long id, long activityId, int stock = 100, decimal price = 500_000m) =>
        new()
        {
            Id = id, Name = "VIP", ActivityId = activityId,
            StockInitial = stock, StockAvailable = stock, PriceOriginal = price,
            PriceFlash = price * 0.7m, Status = 1, IsStockPrepared = true,
            SaleStartTime = DateTime.UtcNow, SaleEndTime = DateTime.UtcNow.AddMonths(6),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task GetAllActiveAsync_ReturnsEnrichedTickets()
    {
        _domain.Setup(d => d.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { SampleTicket(1), SampleTicket(2) });
        _details.Setup(r => r.FindByActivityIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { SampleDetail(11, activityId: 1) });
        _details.Setup(r => r.FindByActivityIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { SampleDetail(21, activityId: 2) });

        var sut = BuildSut();
        var result = await sut.GetAllActiveAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(500_000m, result[0].PriceOriginal);
        Assert.Equal(100, result[0].StockInitial);
        Assert.Equal(100, result[0].StockAvailable);
    }

    [Fact]
    public async Task GetByIdAsync_EnrichesFromFirstDetail()
    {
        _domain.Setup(d => d.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleTicket(42));
        _details.Setup(r => r.FindByActivityIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { SampleDetail(99, activityId: 42, stock: 250, price: 1_000_000m) });

        var sut = BuildSut();
        var dto = await sut.GetByIdAsync(42);

        Assert.Equal(42, dto.Id);
        Assert.Equal(1_000_000m, dto.PriceOriginal);
        Assert.Equal(250, dto.StockAvailable);
    }

    [Fact]
    public async Task CreateAsync_WritesBothCaches()
    {
        _domain.Setup(d => d.CreateAsync(It.IsAny<Ticket>(), It.IsAny<TicketDetail>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketDetail, CancellationToken>((t, det, _) => { t.Id = 7; det.Id = 71; det.ActivityId = 7; })
            .ReturnsAsync((Ticket t, TicketDetail d, CancellationToken _) => t);

        _details.Setup(r => r.FindByActivityIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { SampleDetail(71, activityId: 7) });

        _ticketCache.Setup(c => c.SetAsync(7, It.IsAny<TicketCacheSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _detailCache.Setup(c => c.SetAsync(It.IsAny<TicketDetailCacheEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var dto = await sut.CreateAsync(
            new CreateTicketRequest("Show", "desc", DateTime.UtcNow, DateTime.UtcNow.AddHours(2)),
            new CreateTicketDetailRequest("VIP", 100, 100, 500_000m));

        Assert.Equal(7, dto.Id);
        _ticketCache.Verify(c => c.SetAsync(7, It.IsAny<TicketCacheSnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
        _detailCache.Verify(c => c.SetAsync(It.IsAny<TicketDetailCacheEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_EvictsCache()
    {
        _domain.Setup(d => d.DeleteAsync(5, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _ticketCache.Setup(c => c.EvictAsync(5, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await BuildSut().DeleteAsync(5);

        _domain.Verify(d => d.DeleteAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        _ticketCache.Verify(c => c.EvictAsync(5, It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class TicketDetailAppServiceTests
{
    private readonly Mock<ITicketDetailCacheService> _cache = new();

    private TicketDetailAppServiceImpl BuildSut() =>
        new(_cache.Object, NullLogger<TicketDetailAppServiceImpl>.Instance);

    [Fact]
    public async Task GetByIdAsync_ReturnsDto_WhenCacheHit()
    {
        var entry = new TicketDetailCacheEntry(
            Id: 99, Name: "VIP", StockInitial: 200, StockAvailable: 180,
            PriceOriginal: 1_000_000m, PriceFlash: 700_000m,
            SaleStartTime: DateTime.UtcNow, SaleEndTime: DateTime.UtcNow.AddMonths(6),
            Status: 1, ActivityId: 1, Version: 123456789);
        _cache.Setup(c => c.GetAsync(99, null, It.IsAny<CancellationToken>())).ReturnsAsync(entry);

        var dto = await BuildSut().GetByIdAsync(99, version: null);

        Assert.Equal(99, dto.Id);
        Assert.Equal("VIP", dto.Name);
        Assert.Equal(200, dto.StockInitial);
        Assert.Equal(180, dto.StockAvailable);
        Assert.Equal(700_000m, dto.PriceFlash);
    }

    [Fact]
    public async Task GetByIdAsync_Throws_WhenCacheMiss()
    {
        _cache.Setup(c => c.GetAsync(404, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TicketDetailCacheEntry?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildSut().GetByIdAsync(404, version: null));
    }

    [Fact]
    public async Task OrderByUserAsync_ReturnsTrue()
    {
        _cache.Setup(c => c.OrderByUserAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await BuildSut().OrderByUserAsync(99));
    }
}
