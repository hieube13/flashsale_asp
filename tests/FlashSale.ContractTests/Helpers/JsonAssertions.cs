using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FluentAssertions.Equivalency;

namespace FlashSale.ContractTests.Helpers;

/// <summary>
/// Assertion helpers for JSON contract testing.
///
/// <para>
/// Deep-equality comparison that excludes known-float fields (<c>timestamp</c>,
/// <c>createdAt</c>, <c>updatedAt</c>, <c>saleStartTime</c>, <c>saleEndTime</c>,
/// <c>version</c>) because they are non-deterministic between captures.
///
/// All other fields must match exactly — including <c>success</c>, <c>message</c>,
/// <c>code</c>, and nested <c>result</c> object shapes.
/// </para>
/// </summary>
public static class JsonAssertions
{
    /// <summary>
    /// Fields excluded from comparison (non-deterministic / environment-dependent).
    /// </summary>
    private static readonly HashSet<string> ExcludedFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "timestamp", "createdAt", "updatedAt", "saleStartTime", "saleEndTime",
            "bookingCode", "orderNumber", "paymentId", "txnRef",
            "version", "at", "processedAt",
        };

    /// <summary>
    /// Parse <paramref name="actualJson"/> and assert deep-equivalence to
    /// <paramref name="expectedJson"/>, skipping excluded fields.
    /// </summary>
    public static void AssertJsonEquivalent(string actualJson, string expectedJson)
    {
        var actual   = JsonNode.Parse(actualJson);
        var expected = JsonNode.Parse(expectedJson);

        actual.Should().NotBeNull("response body must be valid JSON");
        expected.Should().NotBeNull("baseline must be valid JSON");

        var mismatches = new List<string>();
        CompareNodes(actual!, expected!, "", mismatches);

        if (mismatches.Count > 0)
        {
            Assert.Fail(
                $"JSON mismatch (excluded: {string.Join(", ", ExcludedFields)}):\n"
                + string.Join("\n", mismatches));
        }
    }

    /// <summary>
    /// Assert that <paramref name="actualJson"/> has the expected top-level
    /// <c>success</c> and <c>code</c> fields matching the baseline, while
    /// allowing <c>result</c> shape to differ (checked separately by callers).
    /// </summary>
    public static void AssertResultEnvelope(string actualJson, string expectedJson)
    {
        var actual   = JsonNode.Parse(actualJson)!.AsObject();
        var expected = JsonNode.Parse(expectedJson)!.AsObject();

        actual["success"].Should().BeEquivalentTo(expected["success"],
            "success flag must match baseline");
        actual["code"].Should().BeEquivalentTo(expected["code"],
            "code must match baseline");
    }

    /// <summary>
    /// Assert that <paramref name="actualJson"/> is a raw primitive
    /// (string / number / bool) matching <paramref name="expected"/> verbatim.
    /// </summary>
    public static void AssertRaw(string actualJson, string expected)
    {
        actualJson.Trim().Should().Be(expected,
            "raw string response must match baseline verbatim");
    }

    private static void CompareNodes(JsonNode actual, JsonNode expected, string path,
        List<string> mismatches)
    {
        if (expected is JsonValue ev && actual is JsonValue av)
        {
            if (!AreValuesEqual(av, ev))
                mismatches.Add($"[{path}] value mismatch: got {av}, expected {ev}");
            return;
        }

        if (expected is JsonObject eo)
        {
            if (actual is not JsonObject ao)
            {
                mismatches.Add($"[{path}] type mismatch: expected object, got {actual.GetType().Name}");
                return;
            }

            foreach (var (key, expectedVal) in eo)
            {
                if (ExcludedFields.Contains(key))
                    continue;

                if (!ao.TryGetPropertyValue(key, out var actualVal))
                {
                    mismatches.Add($"[{path}] missing key '{key}'");
                    continue;
                }

                CompareNodes(actualVal!, expectedVal!, $"{path}.{key}", mismatches);
            }
            return;
        }

        if (expected is JsonArray ea)
        {
            if (actual is not JsonArray aa)
            {
                mismatches.Add($"[{path}] type mismatch: expected array, got {actual.GetType().Name}");
                return;
            }

            if (aa.Count != ea.Count)
                mismatches.Add($"[{path}] array length mismatch: got {aa.Count}, expected {ea.Count}");
            else
                for (var i = 0; i < ea.Count; i++)
                    CompareNodes(aa[i]!, ea[i]!, $"{path}[{i}]", mismatches);

            return;
        }
    }

    private static bool AreValuesEqual(JsonValue a, JsonValue b)
    {
        // Compare as decimal to avoid floating-point string comparison issues
        if (a.TryGetValue<decimal>(out var ad) && b.TryGetValue<decimal>(out var bd))
            return ad == bd;
        return a.ToString() == b.ToString();
    }
}
