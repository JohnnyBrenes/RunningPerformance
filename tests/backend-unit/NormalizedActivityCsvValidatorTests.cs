using RunningPerformance.Application.Ingestion;
using Xunit;

namespace RunningPerformance.UnitTests;

public sealed class NormalizedActivityCsvValidatorTests
{
    [Fact]
    public async Task SyntheticFixtureValidatesAll460RowsAndPreservesNulls()
    {
        var validator = new NormalizedActivityCsvValidator(new HistoricalImportOptions());
        await using var fixture = SyntheticNormalizedCsvFixture.Create();

        var result = await validator.ValidateAsync(
            fixture,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(460, result.ObservedRowCount);
        Assert.Equal(460, result.Rows.Count);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Rows, row => row.Modality == "treadmill");
        Assert.Contains(result.Rows, row => row.Modality == "outdoor");
        Assert.Contains(result.Rows, row => row.Calories is null);
        Assert.Contains(result.Rows, row => row.DistanceM is null);
        Assert.Contains(result.Rows, row => row.Title == "Synthetic, quoted \"session\"");
    }

    [Fact]
    public async Task DuplicateProvisionalKeyFailsTheWholeContract()
    {
        var validator = new NormalizedActivityCsvValidator(new HistoricalImportOptions
        {
            ExpectedRowCount = 2
        });
        await using var fixture = SyntheticNormalizedCsvFixture.Create(2, duplicateLastKey: true);

        var result = await validator.ValidateAsync(
            fixture,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Message.Contains("duplicates data row", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingNumericValueStaysNullInsteadOfZero()
    {
        var validator = new NormalizedActivityCsvValidator(new HistoricalImportOptions
        {
            ExpectedRowCount = 2
        });
        await using var fixture = SyntheticNormalizedCsvFixture.Create(2);

        var result = await validator.ValidateAsync(
            fixture,
            TestContext.Current.CancellationToken);

        var strength = Assert.Single(result.Rows, row => row.ActivityType == "strength_training");
        Assert.Null(strength.DistanceM);
        Assert.Null(strength.AveragePaceSecondsPerKm);
        Assert.Equal(902m, strength.DurationSeconds);
    }
}
