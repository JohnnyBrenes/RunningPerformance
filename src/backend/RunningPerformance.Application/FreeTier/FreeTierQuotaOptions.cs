using System.Text.Json.Serialization;

namespace RunningPerformance.Application.FreeTier;

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed class FreeTierQuotaOptions
{
    public const string SectionName = "FreeTier";

    [JsonRequired]
    public int DatabaseWarningMb { get; init; } = 300;
    [JsonRequired]
    public int DatabaseBlockMb { get; init; } = 400;
    [JsonRequired]
    public int StorageWarningMb { get; init; } = 700;
    [JsonRequired]
    public int StorageBlockMb { get; init; } = 850;
    [JsonRequired]
    public decimal EgressWarningGb { get; init; } = 4;
    [JsonRequired]
    public decimal EgressBlockGb { get; init; } = 5;
    [JsonRequired]
    public int CiWarningMinutes { get; init; } = 1600;
    [JsonRequired]
    public int CiBlockMinutes { get; init; } = 2000;
    [JsonRequired]
    public decimal BackendWarningHours { get; init; } = 675;
    [JsonRequired]
    public decimal BackendBlockHours { get; init; } = 750;

    public void Validate()
    {
        if (DatabaseWarningMb < 0 || DatabaseBlockMb <= DatabaseWarningMb)
        {
            throw new InvalidOperationException("Database free-tier thresholds are invalid.");
        }

        if (StorageWarningMb < 0 || StorageBlockMb <= StorageWarningMb)
        {
            throw new InvalidOperationException("Storage free-tier thresholds are invalid.");
        }

        if (EgressWarningGb < 0 || EgressBlockGb <= EgressWarningGb)
        {
            throw new InvalidOperationException("Egress free-tier thresholds are invalid.");
        }

        if (CiWarningMinutes < 0 || CiBlockMinutes <= CiWarningMinutes)
        {
            throw new InvalidOperationException("CI free-tier thresholds are invalid.");
        }

        if (BackendWarningHours < 0 || BackendBlockHours <= BackendWarningHours)
        {
            throw new InvalidOperationException("Backend free-tier thresholds are invalid.");
        }
    }
}
