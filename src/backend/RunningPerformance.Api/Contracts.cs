namespace RunningPerformance.Api.Features;

public sealed record AthleteProfileResponse(
    string DisplayName,
    DateOnly? BirthDate,
    decimal? HeightCm,
    decimal? WeightKg,
    string Sex,
    string TimezoneName,
    string Locale,
    string UnitSystem,
    DateTime UpdatedAt);

public sealed record UpdateAthleteProfileRequest(
    string DisplayName,
    DateOnly? BirthDate,
    decimal? HeightCm,
    decimal? WeightKg,
    string Sex,
    string TimezoneName,
    string Locale,
    string UnitSystem);

public sealed record HealthContextResponse(
    Guid Id,
    string ContextType,
    string? BodyLocation,
    DateOnly? StartedOn,
    DateOnly? EndedOn,
    string Status,
    string Description,
    DateTime UpdatedAt);

public sealed record SaveHealthContextRequest(
    string ContextType,
    string? BodyLocation,
    DateOnly? StartedOn,
    DateOnly? EndedOn,
    string Status,
    string Description);

public sealed record RaceGoalResponse(
    Guid Id,
    int VersionNumber,
    decimal? GoalTimeSeconds,
    decimal? GoalPaceSecondsPerKm,
    string? Confidence,
    string Rationale,
    Guid? SupersedesId,
    DateTime EffectiveAt);

public sealed record TargetRaceResponse(
    Guid Id,
    string Name,
    DateOnly RaceDate,
    decimal DistanceM,
    string? Location,
    string Priority,
    string Status,
    string? TimezoneName,
    DateTime UpdatedAt,
    RaceGoalResponse? CurrentGoal);

public sealed record SaveTargetRaceRequest(
    string Name,
    DateOnly RaceDate,
    decimal DistanceM,
    string? Location,
    string Priority,
    string Status,
    string? TimezoneName);

public sealed record CreateRaceGoalRequest(
    decimal? GoalTimeSeconds,
    decimal? GoalPaceSecondsPerKm,
    string? Confidence,
    string Rationale);

public sealed record ExerciseMediaResponse(
    Guid Id,
    int Position,
    string AssetUri,
    string AltText,
    string MimeType,
    string Source,
    string? Author,
    string License,
    string? Sha256,
    string PresentationSex,
    int WidthPx,
    int HeightPx);

public sealed record ExerciseRevisionResponse(
    Guid Id,
    int VersionNumber,
    string DisplayName,
    string BriefDescription,
    string Setup,
    string Execution,
    string SafetyCues,
    IReadOnlyList<ExerciseMediaResponse> Media);

public sealed record ExerciseResponse(
    Guid Id,
    string Slug,
    string CanonicalName,
    string? MovementPattern,
    string? Equipment,
    string Status,
    ExerciseRevisionResponse Revision);

public sealed record TrainingPlanVersionSummaryResponse(
    Guid Id,
    int VersionNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string Rationale,
    Guid? SupersedesId,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    int SessionCount);

public sealed record TrainingPlanSummaryResponse(
    Guid Id,
    string Name,
    string Purpose,
    DateOnly? TargetStart,
    DateOnly? TargetEnd,
    string Status,
    IReadOnlyList<TrainingPlanVersionSummaryResponse> Versions);

public sealed record PlannedExerciseResponse(
    Guid Id,
    int Position,
    int? Sets,
    int? RepetitionsMin,
    int? RepetitionsMax,
    decimal? DurationSeconds,
    decimal? RestSeconds,
    decimal? LoadValue,
    string? LoadUnit,
    decimal? TargetRpe,
    decimal? TargetRir,
    string? Tempo,
    string? Side,
    string? Note,
    ExerciseResponse Exercise);

public sealed record PlannedSessionBlockResponse(
    Guid Id,
    int Position,
    string BlockType,
    int RepeatCount,
    string Instructions,
    IReadOnlyList<PlannedExerciseResponse> Exercises);

public sealed record PlannedSessionResponse(
    Guid Id,
    DateOnly ScheduledDate,
    string SessionType,
    string? Modality,
    string Obligation,
    string Objective,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? TargetRpeMin,
    decimal? TargetRpeMax,
    string? Terrain,
    string? Warmup,
    string? MainSet,
    string? Recoveries,
    string? Cooldown,
    IReadOnlyList<PlannedSessionBlockResponse> Blocks);

public sealed record TrainingPlanDetailResponse(
    Guid Id,
    string Name,
    string Purpose,
    string PlanStatus,
    TrainingPlanVersionSummaryResponse Version,
    IReadOnlyList<PlannedSessionResponse> Sessions);

public sealed record CloneTrainingPlanDraftRequest(
    Guid SourceVersionId,
    string Rationale);

public sealed record UpdatePlannedSessionRequest(
    DateOnly ScheduledDate,
    string Objective);
