namespace RunningPerformance.Api.Features;

internal static class RequestValidation
{
    private static readonly HashSet<string> HealthContextTypes =
        ["injury_history", "discomfort", "restriction", "other"];

    private static readonly HashSet<string> HealthContextStatuses =
        ["active", "resolved", "monitoring"];

    private static readonly HashSet<string> RacePriorities = ["A", "B", "C"];

    private static readonly HashSet<string> RaceStatuses =
        ["planned", "completed", "cancelled", "archived"];

    private static readonly HashSet<string> GoalConfidences = ["low", "medium", "high"];

    private static readonly HashSet<string> ProfileSexValues = ["female", "male", "unspecified"];

    public static Dictionary<string, string[]> Profile(UpdateAthleteProfileRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequiredText(errors, nameof(request.DisplayName), request.DisplayName, 120);
        RequiredText(errors, nameof(request.TimezoneName), request.TimezoneName, 100);
        RequiredText(errors, nameof(request.Locale), request.Locale, 20);

        if (!ProfileSexValues.Contains(request.Sex))
        {
            errors[nameof(request.Sex)] = ["El sexo debe ser femenino, masculino o sin especificar."];
        }

        if (!string.Equals(request.UnitSystem, "metric", StringComparison.Ordinal))
        {
            errors[nameof(request.UnitSystem)] = ["El sistema de unidades debe ser métrico."];
        }

        PositiveOptional(errors, nameof(request.HeightCm), request.HeightCm);
        PositiveOptional(errors, nameof(request.WeightKg), request.WeightKg);
        if (request.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors[nameof(request.BirthDate)] = ["La fecha de nacimiento no puede estar en el futuro."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Health(SaveHealthContextRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!HealthContextTypes.Contains(request.ContextType))
        {
            errors[nameof(request.ContextType)] = ["El tipo de antecedente no es válido."];
        }

        if (!HealthContextStatuses.Contains(request.Status))
        {
            errors[nameof(request.Status)] = ["El estado del antecedente no es válido."];
        }

        RequiredText(errors, nameof(request.Description), request.Description, 2000);
        OptionalText(errors, nameof(request.BodyLocation), request.BodyLocation, 120);
        if (request.StartedOn is not null && request.EndedOn < request.StartedOn)
        {
            errors[nameof(request.EndedOn)] = ["La fecha final debe ser igual o posterior a la inicial."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Race(SaveTargetRaceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequiredText(errors, nameof(request.Name), request.Name, 180);
        Positive(errors, nameof(request.DistanceM), request.DistanceM);
        OptionalText(errors, nameof(request.Location), request.Location, 180);
        OptionalText(errors, nameof(request.TimezoneName), request.TimezoneName, 100);

        if (!RacePriorities.Contains(request.Priority))
        {
            errors[nameof(request.Priority)] = ["La prioridad debe ser A, B o C."];
        }

        if (!RaceStatuses.Contains(request.Status))
        {
            errors[nameof(request.Status)] = ["El estado de la carrera no es válido."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Goal(CreateRaceGoalRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        PositiveOptional(errors, nameof(request.GoalTimeSeconds), request.GoalTimeSeconds);
        PositiveOptional(errors, nameof(request.GoalPaceSecondsPerKm), request.GoalPaceSecondsPerKm);
        RequiredText(errors, nameof(request.Rationale), request.Rationale, 2000);

        if (request.Confidence is not null && !GoalConfidences.Contains(request.Confidence))
        {
            errors[nameof(request.Confidence)] = ["La confianza debe ser low, medium o high."];
        }

        if (request.GoalTimeSeconds is null && request.GoalPaceSecondsPerKm is null)
        {
            errors[nameof(request.GoalTimeSeconds)] =
                ["Captura un tiempo objetivo, un ritmo objetivo o ambos."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> PlanDraft(CloneTrainingPlanDraftRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.SourceVersionId == Guid.Empty)
        {
            errors[nameof(request.SourceVersionId)] = ["Selecciona una versión de origen."];
        }

        RequiredText(errors, nameof(request.Rationale), request.Rationale, 2000);
        return errors;
    }

    public static Dictionary<string, string[]> PlannedSession(UpdatePlannedSessionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        RequiredText(errors, nameof(request.Objective), request.Objective, 2000);
        return errors;
    }

    private static void RequiredText(
        IDictionary<string, string[]> errors,
        string name,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = ["Este campo es obligatorio."];
        }
        else if (value.Trim().Length > maximumLength)
        {
            errors[name] = [$"No puede exceder {maximumLength} caracteres."];
        }
    }

    private static void OptionalText(
        IDictionary<string, string[]> errors,
        string name,
        string? value,
        int maximumLength)
    {
        if (value?.Trim().Length > maximumLength)
        {
            errors[name] = [$"No puede exceder {maximumLength} caracteres."];
        }
    }

    private static void Positive(
        IDictionary<string, string[]> errors,
        string name,
        decimal value)
    {
        if (value <= 0)
        {
            errors[name] = ["Debe ser mayor que cero."];
        }
    }

    private static void PositiveOptional(
        IDictionary<string, string[]> errors,
        string name,
        decimal? value)
    {
        if (value <= 0)
        {
            errors[name] = ["Debe ser mayor que cero cuando se especifica."];
        }
    }
}
