using FlashSale.Contracts.Dto;
using FlashSale.Contracts.Messages;

namespace FlashSale.UnitTests;

/// <summary>
/// Smoke tests for shared contracts — DTOs, Messages, Result envelope.
/// </summary>
public class ContractsSmokeTests
{
    [Fact]
    public void PlaceOrderResponse_Ok_FactorySetsSuccessTrue()
    {
        var r = PlaceOrderResponse.Ok("OKX-SGN-5-1-1718246100123");

        Assert.True(r.Success);
        Assert.Equal("OKX-SGN-5-1-1718246100123", r.OrderNumber);
        Assert.Null(r.Code);
        Assert.Null(r.Message);
    }

    [Fact]
    public void PlaceOrderResponse_Failed_FactorySetsSuccessFalse()
    {
        var r = PlaceOrderResponse.Failed("OUT_OF_STOCK", "Hết vé");

        Assert.False(r.Success);
        Assert.Null(r.OrderNumber);
        Assert.Equal("OUT_OF_STOCK", r.Code);
        Assert.Equal("Hết vé", r.Message);
    }

    [Fact]
    public void ResultMessageT_Data_FactoryWrapsValue()
    {
        var r = ResultMessage<string>.Data("hello");

        Assert.True(r.Success);
        Assert.Equal(200, r.Code);
        Assert.Equal("hello", r.Result);
        Assert.True(r.Timestamp > 0);
    }

    [Fact]
    public void PlaceOrderMqMessage_RecordCanBeInstantiated()
    {
        var msg = new PlaceOrderMqMessage(
            Token: "MQ-abc123",
            TicketId: 4,
            Quantity: 2,
            UserId: 5,
            UnitPrice: 10000L,
            CreatedAt: 1718246100123);

        Assert.Equal("MQ-abc123", msg.Token);
        Assert.Equal(4, msg.TicketId);
        Assert.Equal(10000L, msg.UnitPrice);
    }

    [Fact]
    public void CreateBookingRequest_ParsesBasicJson()
    {
        var req = new CreateBookingRequest(4, 2);

        Assert.Equal(4, req.TicketId);
        Assert.Equal(2, req.Quantity);
    }
}