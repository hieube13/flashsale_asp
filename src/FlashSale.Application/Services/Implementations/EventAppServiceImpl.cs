using Microsoft.Extensions.Logging;

namespace FlashSale.Application.Services.Implementations;

/// <summary>
/// Event app service — TASK-020 (port of Java <c>EventAppServiceImpl</c>).
///
/// <para>
/// In Java, <c>EventAppServiceImpl.sayHi(who)</c> delegates to
/// <c>HiDomainService.sayHi</c> which delegates to <c>HiInfrasRepositoryImpl.sayHi(who)</c>
/// which ALWAYS returns the hardcoded literal <c>"Hi Infrastructure"</c> — the
/// <c>who</c> argument is silently ignored (Java <c>HiInfrasRepositoryImpl.java:10</c>).
/// </para>
/// <para>
/// <b>.NET matches Java's observable behavior</b>: we accept the input argument but
/// return the constant string. See <c>KNOWN_DIFFERENCES.md</c> §27.
/// </para>
/// </summary>
public sealed class EventAppServiceImpl : IEventAppService
{
    private const string HardcodedGreeting = "Hi Infrastructure";
    private readonly ILogger<EventAppServiceImpl> _log;

    public EventAppServiceImpl(ILogger<EventAppServiceImpl> log) => _log = log;

    public string SayHi(string name)
    {
        _log.LogInformation("[Event] sayHi called with name={Name}", name);
        return HardcodedGreeting;
    }
}