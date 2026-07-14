using FlashSale.Application.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashSale.UnitTests.Application;

public class EventAppServiceImplTests
{
    private readonly EventAppServiceImpl _sut = new(NullLogger<EventAppServiceImpl>.Instance);

    [Theory]
    [InlineData("Hi")]
    [InlineData("World")]
    [InlineData("Foo")]
    [InlineData("")]
    public void SayHi_returns_hardcoded_Infrastructure_string_for_any_input(string name)
    {
        // Mirrors Java HiInfrasRepositoryImpl.sayHi(who) which IGNORES the arg
        // and returns "Hi Infrastructure" verbatim.
        Assert.Equal("Hi Infrastructure", _sut.SayHi(name));
    }
}