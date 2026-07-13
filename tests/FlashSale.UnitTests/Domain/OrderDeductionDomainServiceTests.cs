using FlashSale.Domain.Services;
using FlashSale.Domain.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests.Domain;

public class OrderDeductionDomainServiceTests
{
    private readonly IOrderDeductionDomainService _sut = new OrderDeductionDomainService();

    [Theory]
    [InlineData("OKX-SGN-7-42-1718246100123", "202406")]
    [InlineData("OKX-SGN-7-42-1716093600000", "202405")]
    [InlineData("whatever-123-456-1609459200000", "202101")]
    public void ExtractYearMonth_returns_yyyyMM_for_trailing_ms_timestamp(string orderNumber, string expected)
    {
        var ym = _sut.ExtractYearMonth(orderNumber);
        Assert.Equal(expected, ym);
    }

    [Fact]
    public void ExtractYearMonth_throws_on_null()
        => Assert.Throws<ArgumentException>(() => _sut.ExtractYearMonth(""));

    [Fact]
    public void ExtractYearMonth_throws_on_no_dash()
        => Assert.Throws<ArgumentException>(() => _sut.ExtractYearMonth("nodelimiterhere"));

    [Fact]
    public void ExtractYearMonth_throws_on_non_numeric_trailing_segment()
        => Assert.Throws<ArgumentException>(() => _sut.ExtractYearMonth("OKX-SGN-7-42-abc"));
}
