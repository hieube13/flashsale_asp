using FlashSale.Application.Services.Implementations;
using FlashSale.Contracts.Dto;
using FlashSale.Domain.Repositories;
using FlashSale.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlashSale.UnitTests.Application;

public class TicketOrderAppServiceImplTests
{
    private static readonly DateTime OrderDate = new(2024, 7, 15, 10, 0, 0, DateTimeKind.Utc);

    private static object?[] SampleRow(int id = 1) => new object?[]
    {
        id,                  // 0  id
        7L,                  // 1  user_id
        11L,                 // 2  ticket_id
        2,                   // 3  quantity
        1,                   // 4  order_status (1 = SUCCESS)
        "OKX-SGN-7-42-1721035200000", // 5 order_number (with ms timestamp -> 202407)
        20000L,              // 6  total_amount (BIGINT in shard)
        "TERM-001",          // 7  terminal_id
        OrderDate,           // 8  order_date
        null,                // 9  order_notes
        OrderDate,           // 10 updated_at
        OrderDate,           // 11 created_at
    };

    [Fact]
    public async Task FindAllAsync_maps_rows_to_dtos()
    {
        var repo = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        repo.Setup(r => r.FindAllAsync("202407", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<object?[]> { SampleRow(1), SampleRow(2) });

        var sut = new TicketOrderAppServiceImpl(repo.Object, domain.Object,
            NullLogger<TicketOrderAppServiceImpl>.Instance);

        var result = await sut.FindAllAsync("202407");

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(7, result[0].UserId);
        Assert.Equal(11, result[0].TicketId);
        Assert.Equal(2, result[0].Quantity);
        Assert.Equal(1, result[0].OrderStatus);
        Assert.Equal(20000m, result[0].TotalAmount);
        Assert.Equal("TERM-001", result[0].TerminalId);
        Assert.Equal(OrderDate, result[0].OrderDate);
        repo.VerifyAll();
    }

    [Fact]
    public async Task FindPageAsync_caps_limit_to_safe_max()
    {
        var repo = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        repo.Setup(r => r.FindPageAsync("202407", 0L, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<object?[]>());

        var sut = new TicketOrderAppServiceImpl(repo.Object, domain.Object,
            NullLogger<TicketOrderAppServiceImpl>.Instance);

        var result = await sut.FindPageAsync("202407", 0L, 999999);

        Assert.False(result.HasMore);
        Assert.Null(result.NextCursor);
        repo.VerifyAll();
    }

    [Fact]
    public async Task FindPageAsync_returns_nextCursor_and_hasMore_when_full_page()
    {
        var repo = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        repo.Setup(r => r.FindPageAsync("202407", 100L, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<object?[]> { SampleRow(99), SampleRow(98) });

        var sut = new TicketOrderAppServiceImpl(repo.Object, domain.Object,
            NullLogger<TicketOrderAppServiceImpl>.Instance);

        var result = await sut.FindPageAsync("202407", 100L, 2);

        Assert.True(result.HasMore);
        Assert.Equal(98, result.NextCursor);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task FindByOrderNumberAsync_uses_yearMonth_from_domain_then_returns_dto()
    {
        var repo = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        const string on = "OKX-SGN-7-42-1721035200000";
        domain.Setup(d => d.ExtractYearMonth(on)).Returns("202407");
        repo.Setup(r => r.FindByOrderNumberAsync("202407", on, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleRow(42));

        var sut = new TicketOrderAppServiceImpl(repo.Object, domain.Object,
            NullLogger<TicketOrderAppServiceImpl>.Instance);

        var result = await sut.FindByOrderNumberAsync("000000", on);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("OKX-SGN-7-42-1721035200000", result.OrderNumber);
    }

    [Fact]
    public async Task FindByOrderNumberAsync_returns_null_when_repo_returns_null()
    {
        var repo = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        const string on = "OKX-SGN-7-42-1721035200000";
        domain.Setup(d => d.ExtractYearMonth(on)).Returns("202407");
        repo.Setup(r => r.FindByOrderNumberAsync("202407", on, It.IsAny<CancellationToken>()))
            .ReturnsAsync((object?[]?)null);

        var sut = new TicketOrderAppServiceImpl(repo.Object, domain.Object,
            NullLogger<TicketOrderAppServiceImpl>.Instance);

        var result = await sut.FindByOrderNumberAsync("000000", on);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindByOrderNumberAsync_throws_ArgumentException_for_malformed_order_number()
    {
        var repo = new Mock<ITickerOrderRepository>(MockBehavior.Strict);
        var domain = new Mock<IOrderDeductionDomainService>(MockBehavior.Strict);
        domain.Setup(d => d.ExtractYearMonth("INVALID")).Throws<ArgumentException>();

        var sut = new TicketOrderAppServiceImpl(repo.Object, domain.Object,
            NullLogger<TicketOrderAppServiceImpl>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.FindByOrderNumberAsync("000000", "INVALID"));
    }
}
