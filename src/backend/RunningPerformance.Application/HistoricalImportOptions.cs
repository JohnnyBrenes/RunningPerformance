namespace RunningPerformance.Application.Ingestion;

public sealed class HistoricalImportOptions
{
    public const string SectionName = "HistoricalImport";

    public int ExpectedRowCount { get; init; } = 460;

    public int MaxCsvBytes { get; init; } = 5 * 1024 * 1024;

    public int LeaseSeconds { get; init; } = 120;

    public int PollSeconds { get; init; } = 5;

    public int MaxAttempts { get; init; } = 3;

    public void Validate()
    {
        if (ExpectedRowCount <= 0)
        {
            throw new InvalidOperationException("The historical CSV row count must be positive.");
        }

        if (MaxCsvBytes is < 1024 or > 50 * 1024 * 1024)
        {
            throw new InvalidOperationException("The historical CSV byte limit is invalid.");
        }

        if (LeaseSeconds < 30 || PollSeconds < 1 || MaxAttempts < 1)
        {
            throw new InvalidOperationException("Historical ingestion queue settings are invalid.");
        }
    }
}
