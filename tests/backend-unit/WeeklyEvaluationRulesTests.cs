using RunningPerformance.Application.Evaluations;
using Xunit;

namespace RunningPerformance.UnitTests;

public sealed class WeeklyEvaluationRulesTests
{
    [Fact]
    public void WorstSafetySignalAlwaysPrevails()
    {
        Assert.Equal("red", WeeklyEvaluationRules.WorstTrafficLight(["green", "red", "yellow"]));
        Assert.Equal("yellow", WeeklyEvaluationRules.WorstTrafficLight(["green", "yellow"]));
        Assert.Equal("green", WeeklyEvaluationRules.WorstTrafficLight([]));
    }

    [Fact]
    public void UnknownTrafficSignalIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeeklyEvaluationRules.WorstTrafficLight(["blue"]));
    }

    [Theory]
    [InlineData("adapt", true)]
    [InlineData("reduce", true)]
    [InlineData("execute_plan", false)]
    [InlineData("stop_and_assess", false)]
    public void AdjustmentRequirementFollowsHumanDecision(string decision, bool expected)
    {
        Assert.Equal(expected, WeeklyEvaluationRules.RequiresPlanAdjustment(decision));
    }

    [Fact]
    public void WeeklySnapshotsStartOnMonday()
    {
        Assert.True(WeeklyEvaluationRules.IsMonday(new DateOnly(2026, 8, 10)));
        Assert.False(WeeklyEvaluationRules.IsMonday(new DateOnly(2026, 8, 13)));
    }
}
