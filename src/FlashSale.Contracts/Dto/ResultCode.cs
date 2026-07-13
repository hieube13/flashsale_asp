namespace FlashSale.Contracts.Dto;

/// <summary>
/// Result codes — mirrors Java ResultCode enum.
/// </summary>
public enum ResultCode
{
    SUCCESS = 200,
    BAD_REQUEST = 400,
    UNAUTHORIZED = 401,
    FORBIDDEN = 403,
    NOT_FOUND = 404,
    CONFLICT = 409,
    INTERNAL_ERROR = 500,

    // Domain-specific
    OUT_OF_STOCK = 1001,
    STOCK_CONFLICT = 1002,
    PRICE_NOT_FOUND = 1003,
    TICKET_NOT_FOUND = 1004,
    INVALID_SIGNATURE = 1100,
    TOO_MANY_REQUEST = 429
}