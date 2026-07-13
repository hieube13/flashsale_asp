namespace FlashSale.IntegrationTests;

/// <summary>
/// Placeholder for Testcontainers harness — concrete tests added per TASK-011..020.
/// </summary>
public class HealthCheckSmokeTests
{
    [Fact(Skip = "Testcontainers harness lands in TASK-005 detail")]
    public void Health_RespondsOk()
    {
        // Will use WebApplicationFactory + MySql/Redis/Kafka containers
    }

    [Fact(Skip = "Implemented in TASK-013")]
    public void PostOrderCas_ConcurrentRequests_StockNeverNegative()
    {
        // 1000 concurrent POST /order/cas, verify total decrements ≤ initial
    }
}