namespace FlashSale.Contracts.Dto;

/// <summary>
/// Response for /order/cas and /order/mq endpoints.
/// success=true  → order placed, OrderNumber/Token returned
/// success=false → OOS or error, Code carries reason.
/// </summary>
public sealed record PlaceOrderResponse(
    bool Success,
    string? OrderNumber,
    string? Code,
    string? Message)
{
    public static PlaceOrderResponse Failed(string code, string message) =>
        new(false, null, code, message);

    public static PlaceOrderResponse Ok(string orderNumber) =>
        new(true, orderNumber, null, null);
}