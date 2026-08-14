namespace RunningPerformance.Application.Dashboard;

public static class DashboardRules
{
    public static readonly int[] SupportedWindows = [4, 8, 12];

    public static bool IsSupportedWindow(int weeks) => SupportedWindows.Contains(weeks);

    public static string NormalizeRunningModality(string? modality) =>
        modality?.Trim().ToLowerInvariant() switch
        {
            "treadmill" => "treadmill",
            "outdoor" => "outdoor",
            _ => "other"
        };

    public static decimal? WeightedPaceSecondsPerKm(decimal? distanceM, decimal? durationSeconds) =>
        distanceM is > 0 && durationSeconds is not null
            ? decimal.Round(durationSeconds.Value / (distanceM.Value / 1000m), 3)
            : null;
}

public static class AthleteExportRules
{
    public const string SchemaVersion = "running-performance-export-v1";
    public const string Format = "json";
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    public const int MaximumBytes = 20 * 1024 * 1024;

    public static bool IsValidIdempotencyKey(string? value) =>
        value is not null && value.Trim().Length is >= 8 and <= 200;

    public static bool IsValidLifecycleRationale(string? value) =>
        value is not null && value.Trim().Length is >= 12 and <= 2000;

    public static bool IsValidLifecycleScope(string? scopeType, Guid? scopeId) =>
        scopeType switch
        {
            "all" => scopeId is null,
            "activity" or "source_file" or "training_plan" or "weekly_evaluation" =>
                scopeId is not null && scopeId != Guid.Empty,
            _ => false
        };
}
