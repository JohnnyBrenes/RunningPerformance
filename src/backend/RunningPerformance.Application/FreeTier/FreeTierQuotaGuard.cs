namespace RunningPerformance.Application.FreeTier;

public enum QuotaState
{
    Allowed,
    Warning,
    Blocked,
    NotAvailable
}

public sealed record QuotaDecision(QuotaState State, string Code, bool BillingEnabled)
{
    public bool AllowsWrite => State is QuotaState.Allowed or QuotaState.Warning;
}

public sealed class FreeTierQuotaGuard(FreeTierQuotaOptions options)
{
    public QuotaDecision EvaluateDatabase(int usedMb) =>
        Evaluate(usedMb, options.DatabaseWarningMb, options.DatabaseBlockMb, "database");

    public QuotaDecision EvaluateStorage(int usedMb) =>
        Evaluate(usedMb, options.StorageWarningMb, options.StorageBlockMb, "storage");

    public QuotaDecision EvaluateEgress(decimal? usedGb) =>
        EvaluateOptional(usedGb, options.EgressWarningGb, options.EgressBlockGb, "egress");

    public QuotaDecision EvaluateCiMinutes(decimal? usedMinutes) =>
        EvaluateOptional(usedMinutes, options.CiWarningMinutes, options.CiBlockMinutes, "ci");

    public QuotaDecision EvaluateBackendHours(decimal? usedHours) =>
        EvaluateOptional(usedHours, options.BackendWarningHours, options.BackendBlockHours, "backend");

    private static QuotaDecision Evaluate(int usedMb, int warningMb, int blockMb, string resource)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usedMb);

        return usedMb >= blockMb
            ? new(QuotaState.Blocked, $"free_{resource}_quota_block", false)
            : usedMb >= warningMb
                ? new(QuotaState.Warning, $"free_{resource}_quota_warning", false)
                : new(QuotaState.Allowed, $"free_{resource}_quota_ok", false);
    }

    private static QuotaDecision EvaluateOptional(
        decimal? used,
        decimal warning,
        decimal block,
        string resource)
    {
        if (used is null)
        {
            return new(QuotaState.NotAvailable, $"free_{resource}_quota_nd", false);
        }

        if (used < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(used));
        }

        return used >= block
            ? new(QuotaState.Blocked, $"free_{resource}_quota_block", false)
            : used >= warning
                ? new(QuotaState.Warning, $"free_{resource}_quota_warning", false)
                : new(QuotaState.Allowed, $"free_{resource}_quota_ok", false);
    }
}
