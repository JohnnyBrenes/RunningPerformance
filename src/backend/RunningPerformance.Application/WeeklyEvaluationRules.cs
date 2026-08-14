namespace RunningPerformance.Application.Evaluations;

public static class WeeklyEvaluationRules
{
    public const string FormatVersion = "TRN-003-v1-2026-08-11";

    public static readonly IReadOnlySet<string> SnapshotStatuses =
        new HashSet<string>(StringComparer.Ordinal) { "provisional", "final" };

    public static readonly IReadOnlySet<string> DecisionValues =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "execute_plan", "adapt", "reduce", "stop_and_assess"
        };

    public static string WorstTrafficLight(IEnumerable<string> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var worst = 0;
        foreach (var signal in signals)
        {
            var severity = signal switch
            {
                "green" => 0,
                "yellow" => 1,
                "red" => 2,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(signals), signal, "Traffic light must be green, yellow or red.")
            };
            worst = Math.Max(worst, severity);
        }

        return worst switch { 2 => "red", 1 => "yellow", _ => "green" };
    }

    public static bool IsMonday(DateOnly value) => value.DayOfWeek == DayOfWeek.Monday;

    public static bool RequiresPlanAdjustment(string decision) =>
        decision is "adapt" or "reduce";
}
