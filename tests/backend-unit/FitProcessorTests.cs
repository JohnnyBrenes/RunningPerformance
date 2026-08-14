using RunningPerformance.Fit;
using Xunit;

namespace RunningPerformance.UnitTests;

public sealed class FitProcessorTests
{
    [Fact]
    public void Synthetic_fit_is_valid_deterministic_and_normalized()
    {
        var path = SyntheticFitFixture.Create();
        try
        {
            var first = CanonicalFitProcessor.Process("90000000001", path);
            var second = CanonicalFitProcessor.Process("90000000001", path);
            var normalized = FitActivityNormalizer.Normalize(first);

            Assert.Equal(CanonicalFitProcessor.Serialize(first), CanonicalFitProcessor.Serialize(second));
            Assert.True(first.Validation.IsFit);
            Assert.True(first.Validation.IntegrityValid);
            Assert.True(first.Validation.ReadSuccessful);
            Assert.Equal(11, first.Counts.RecordCount);
            Assert.Equal(1, first.Counts.SessionCount);
            Assert.Equal(1, first.Counts.LapCount);
            Assert.Equal("running", normalized.Summary.ActivityCategory);
            Assert.Equal("running", normalized.Summary.ActivityType);
            Assert.Equal(new DateTime(2026, 1, 15, 6, 0, 0), normalized.Summary.StartedAtLocal);
            Assert.Equal(1000m, normalized.Summary.DistanceM);
            Assert.Equal(170m, normalized.Summary.AverageCadenceSpm);
            Assert.Equal(11, normalized.Samples.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_fit_is_rejected_by_integrity_validation()
    {
        var path = SyntheticFitFixture.Create(424243);
        try
        {
            var bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidDataException>(() =>
                CanonicalFitProcessor.Process("90000000002", path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
