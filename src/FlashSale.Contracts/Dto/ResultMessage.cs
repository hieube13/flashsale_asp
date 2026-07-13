namespace FlashSale.Contracts.Dto;

/// <summary>
/// Standard response envelope — mirrors Java ResultMessage&lt;T&gt;.
/// </summary>
public sealed record ResultMessage<T>(
    bool Success,
    string? Message,
    int Code,
    long Timestamp,
    T? Result)
{
    public static ResultMessage<T> Data(T data) =>
        new(true, "OK", 200, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data);

    public static ResultMessage<T> Data(T data, string message) =>
        new(true, message, 200, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data);

    public static ResultMessage<T> Error(int code, string message) =>
        new(false, message, code, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), default);

    public static ResultMessage<T> FromCode(int code, string message) =>
        new(code is >= 200 and < 300, message, code, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), default);
}

public sealed record ResultMessage(
    bool Success,
    string? Message,
    int Code,
    long Timestamp)
{
    public static ResultMessage Data() =>
        new(true, "OK", 200, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public static ResultMessage Error(int code, string message) =>
        new(false, message, code, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}