using RunningPerformance.Application.Dashboard;
using Xunit;

namespace RunningPerformance.UnitTests;

public sealed class DashboardRulesTests
{
    [Theory]
    [InlineData(4, true)]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(6, false)]
    public void SupportsOnlyDocumentedWindows(int weeks, bool expected) =>
        Assert.Equal(expected, DashboardRules.IsSupportedWindow(weeks));

    [Theory]
    [InlineData("treadmill", "treadmill")]
    [InlineData("outdoor", "outdoor")]
    [InlineData("indoor", "other")]
    [InlineData(null, "other")]
    public void KeepsRunningModalitiesSeparate(string? value, string expected) =>
        Assert.Equal(expected, DashboardRules.NormalizeRunningModality(value));

    [Fact]
    public void PaceUsesTotalTimeOverTotalDistance() =>
        Assert.Equal(360m, DashboardRules.WeightedPaceSecondsPerKm(5000, 1800));

    [Fact]
    public void PaceRemainsMissingWithoutDistance() =>
        Assert.Null(DashboardRules.WeightedPaceSecondsPerKm(null, 1800));

    [Theory]
    [InlineData("all", null, true)]
    [InlineData("all", "11111111-1111-4111-8111-111111111111", false)]
    [InlineData("activity", "11111111-1111-4111-8111-111111111111", true)]
    [InlineData("activity", null, false)]
    [InlineData("credentials", null, false)]
    public void LifecycleScopeIsExplicit(string scope, string? id, bool expected) =>
        Assert.Equal(expected, AthleteExportRules.IsValidLifecycleScope(
            scope,
            id is null ? null : Guid.Parse(id)));
}
