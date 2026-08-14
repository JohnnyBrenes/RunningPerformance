namespace RunningPerformance.Application.Ingestion;

public sealed class FitIngestionOptions
{
    public const string SectionName = "FitIngestion";

    public int MaxFitBytes { get; init; } = 50 * 1024 * 1024;

    public int LeaseSeconds { get; init; } = 180;

    public int MaxAttempts { get; init; } = 3;

    public int SampleBatchSize { get; init; } = 500;

    public int PairingMinutes { get; init; } = 10;

    public int CredentialDays { get; init; } = 90;

    public void Validate()
    {
        if (MaxFitBytes is < 1024 or > 250 * 1024 * 1024)
        {
            throw new InvalidOperationException("The FIT byte limit is invalid.");
        }
        if (LeaseSeconds < 30 || MaxAttempts < 1 || SampleBatchSize is < 50 or > 2000)
        {
            throw new InvalidOperationException("FIT ingestion queue settings are invalid.");
        }
        if (PairingMinutes is < 1 or > 10 || CredentialDays is < 1 or > 90)
        {
            throw new InvalidOperationException("FIT synchronization credential limits are invalid.");
        }
    }
}
