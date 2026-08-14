using RunningPerformance.Application.FreeTier;
using Xunit;

namespace RunningPerformance.UnitTests;

public sealed class FreeTierQuotaGuardTests
{
    private readonly FreeTierQuotaGuard guard = new(new());

    [Theory]
    [InlineData(0, QuotaState.Allowed)]
    [InlineData(299, QuotaState.Allowed)]
    [InlineData(300, QuotaState.Warning)]
    [InlineData(399, QuotaState.Warning)]
    [InlineData(400, QuotaState.Blocked)]
    public void DatabaseThresholdsNeverEnableBilling(int usedMb, QuotaState expected)
    {
        var decision = guard.EvaluateDatabase(usedMb);

        Assert.Equal(expected, decision.State);
        Assert.False(decision.BillingEnabled);
    }

    [Theory]
    [InlineData(699, QuotaState.Allowed)]
    [InlineData(700, QuotaState.Warning)]
    [InlineData(850, QuotaState.Blocked)]
    public void StorageThresholdsBlockBeforeProviderLimit(int usedMb, QuotaState expected)
    {
        Assert.Equal(expected, guard.EvaluateStorage(usedMb).State);
    }

    [Theory]
    [InlineData(null, QuotaState.NotAvailable)]
    [InlineData(3.9, QuotaState.Allowed)]
    [InlineData(4.0, QuotaState.Warning)]
    [InlineData(5.0, QuotaState.Blocked)]
    public void EgressPreservesMissingUsageAndBlocksAtFreeLimit(
        double? usedGb,
        QuotaState expected)
    {
        var decision = guard.EvaluateEgress(usedGb is null ? null : (decimal)usedGb.Value);

        Assert.Equal(expected, decision.State);
        Assert.False(decision.BillingEnabled);
        Assert.Equal(expected is QuotaState.Allowed or QuotaState.Warning, decision.AllowsWrite);
    }
}
