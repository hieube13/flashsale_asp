namespace FlashSale.ContractTests;

/// <summary>
/// Placeholder for TASK-021 — concrete parity tests are added when baselines exist.
/// </summary>
public class BaselineCaptureTests
{
    [Fact(Skip = "Captured baselines land in TASK-021")]
    public void CaptureBaselines_OncePerRelease()
    {
        // Will use HttpClient + golden JSON files in Baselines/
    }

    [Fact(Skip = "Implemented in TASK-021")]
    public void CompareEndpoint_MatchesJava()
    {
        // Will diff JSON minus timestamp
    }
}