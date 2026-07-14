using System.Reflection;
using FlashSale.Api.Stubs;
using FlashSale.Application.Services;
using FlashSale.Contracts.Dto;
using FlashSale.Infrastructure.Cache;
using FlashSale.Infrastructure.DistributedLock;
using FlashSale.Infrastructure.External;
using FlashSale.Infrastructure.Messaging;
using NetArchTest.Rules;

namespace FlashSale.ArchitectureTests;

/// <summary>
/// Enforces dependency direction: Api → Application → Infrastructure → Domain ← Contracts.
/// See docs/INTERNAL_ARCHITECTURE.md §1 for the full graph.
/// </summary>
public class DependencyDirectionTests
{
    private static readonly Assembly Api = typeof(FlashSale.Api.Controllers.OrderMQController).Assembly;
    private static readonly Assembly Application = typeof(ITicketAppService).Assembly;
    private static readonly Assembly Infrastructure = typeof(IRedisInfrasService).Assembly;
    private static readonly Assembly Domain = typeof(Domain.Entities.Ticket).Assembly;
    private static readonly Assembly Contracts = typeof(ResultMessage<>).Assembly;

    [Fact]
    public void Domain_Should_Not_Depend_On_Any_Other_Project()
    {
        var forbidden = new[]
        {
            Application.GetName().Name!,
            Infrastructure.GetName().Name!,
            Api.GetName().Name!,
            Contracts.GetName().Name!
        };

        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Domain has forbidden dependencies: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Contracts_Should_Not_Depend_On_Any_Other_Project()
    {
        var forbidden = new[]
        {
            Domain.GetName().Name!,
            Application.GetName().Name!,
            Infrastructure.GetName().Name!,
            Api.GetName().Name!
        };

        var result = Types.InAssembly(Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Contracts has forbidden dependencies: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure_Or_Api()
    {
        var forbidden = new[]
        {
            Infrastructure.GetName().Name!,
            Api.GetName().Name!
        };

        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Application has forbidden dependencies: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOn(Api.GetName().Name!)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"Infrastructure depends on Api (forbidden): {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void Api_Can_Depend_On_All_Other_Projects()
    {
        // Positive test — Api references all the others. Verify it can resolve concrete types.
        // This is implicitly tested by build; we add an explicit symbol check.
        var t = typeof(KafkaOrderProducer);
        Assert.NotNull(t);
        Assert.Equal(Infrastructure.GetName().Name, t.Assembly.GetName().Name);
    }
}