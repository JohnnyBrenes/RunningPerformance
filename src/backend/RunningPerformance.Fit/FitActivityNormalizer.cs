using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace RunningPerformance.Fit;

public static class FitActivityNormalizer
{
    private const double SemicircleToDegrees = 180d / 2147483648d;

    public static FitActivityData Normalize(CanonicalFit source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sessionMessages = GetMessages(source, "session");
        if (sessionMessages.Count == 0)
        {
            throw new InvalidDataException("The FIT activity has no session message.");
        }

        var primary = sessionMessages[0];
        var activity = GetMessages(source, "activity").FirstOrDefault();
        var correlation = GetMessages(source, "timestamp_correlation").FirstOrDefault();
        var sport = GetLong(primary.Fields, "sport");
        var subSport = GetLong(primary.Fields, "sub_sport");
        var summary = new FitActivitySummary(
            GetLocalDateTime(activity?.Fields, "local_timestamp")
                ?? GetLocalDateTime(correlation?.Fields, "local_timestamp"),
            GetUtcDateTime(primary.Fields, "start_time"),
            ActivityType(sport, subSport),
            ActivityCategory(sport),
            Modality(sport, subSport),
            GetString(primary.Fields, "sport_profile_name"),
            GetDecimal(primary.Fields, "total_distance"),
            GetDecimal(primary.Fields, "total_timer_time"),
            GetDecimal(primary.Fields, "total_elapsed_time"),
            GetDecimal(primary.Fields, "enhanced_avg_speed")
                ?? GetDecimal(primary.Fields, "avg_speed"),
            GetDecimal(primary.Fields, "total_calories"),
            GetDecimal(primary.Fields, "avg_heart_rate"),
            GetDecimal(primary.Fields, "max_heart_rate"),
            NormalizeCadence(
                GetDecimal(primary.Fields, "avg_cadence"),
                GetDecimal(primary.Fields, "avg_fractional_cadence"),
                sport),
            GetDecimal(primary.Fields, "avg_power"),
            GetDecimal(primary.Fields, "total_ascent"),
            GetInt(primary.Fields, "num_laps"));

        if (summary.StartedAtLocal is null)
        {
            throw new InvalidDataException(
                "The FIT activity has no local timestamp and cannot be linked safely.");
        }

        var sessions = sessionMessages.Select(message => new FitSessionData(
            message.Sequence,
            SportName(GetLong(message.Fields, "sport")),
            SubSportName(GetLong(message.Fields, "sub_sport")),
            GetUtcDateTime(message.Fields, "start_time"),
            GetDecimal(message.Fields, "total_timer_time"),
            GetDecimal(message.Fields, "total_distance"),
            SerializeFields(message.Fields))).ToArray();

        var laps = GetMessages(source, "lap").Select(message => new FitLapData(
            message.Sequence,
            FindSessionForLap(sessionMessages, message.Sequence),
            GetUtcDateTime(message.Fields, "start_time"),
            GetUtcDateTime(message.Fields, "timestamp"),
            GetDecimal(message.Fields, "total_timer_time"),
            GetDecimal(message.Fields, "total_distance"),
            SerializeFields(message.Fields))).ToArray();

        var events = GetMessages(source, "event").Select(message => new FitEventData(
            message.Sequence,
            GetUtcDateTime(message.Fields, "timestamp"),
            GetStringOrNumber(message.Fields, "event"),
            GetStringOrNumber(message.Fields, "event_type"),
            GetStringOrNumber(message.Fields, "event_group"),
            GetStringOrNumber(message.Fields, "data"),
            SerializeAdditional(message.Fields,
                "timestamp", "event", "event_type", "event_group", "data"))).ToArray();

        var zones = NormalizeZones(source);
        var samples = GetMessages(source, "record").Select(message => new FitSampleData(
            message.Sequence,
            GetUtcDateTime(message.Fields, "timestamp"),
            GetDecimal(message.Fields, "distance"),
            ToDegrees(GetDecimal(message.Fields, "position_lat")),
            ToDegrees(GetDecimal(message.Fields, "position_long")),
            GetDecimal(message.Fields, "enhanced_altitude")
                ?? GetDecimal(message.Fields, "altitude"),
            GetDecimal(message.Fields, "enhanced_speed")
                ?? GetDecimal(message.Fields, "speed"),
            GetDecimal(message.Fields, "heart_rate"),
            NormalizeCadence(
                GetDecimal(message.Fields, "cadence"),
                GetDecimal(message.Fields, "fractional_cadence"),
                sport),
            GetDecimal(message.Fields, "power"),
            GetDecimal(message.Fields, "temperature"),
            SerializeAdditional(message.Fields,
                "timestamp", "distance", "position_lat", "position_long",
                "enhanced_altitude", "altitude", "enhanced_speed", "speed",
                "heart_rate", "cadence", "fractional_cadence", "power", "temperature")))
            .ToArray();

        return new FitActivityData(source, summary, sessions, laps, events, zones, samples);
    }

    private static int? FindSessionForLap(
        IReadOnlyList<CanonicalMessage> sessions,
        int lapIndex)
    {
        foreach (var session in sessions)
        {
            var first = GetInt(session.Fields, "first_lap_index") ?? 0;
            var count = GetInt(session.Fields, "num_laps") ?? 0;
            if (lapIndex >= first && lapIndex < first + count)
            {
                return session.Sequence;
            }
        }
        return sessions.Count == 1 ? sessions[0].Sequence : null;
    }

    private static IReadOnlyList<FitZoneData> NormalizeZones(CanonicalFit source)
    {
        var result = new List<FitZoneData>();
        foreach (var message in GetMessages(source, "time_in_zone"))
        {
            AddZones(result, message, "heart_rate", "time_in_hr_zone", "hr_zone_high_boundary");
            AddZones(result, message, "speed", "time_in_speed_zone", "speed_zone_high_boundary");
            AddZones(result, message, "power", "time_in_power_zone", "power_zone_high_boundary");
            AddZones(result, message, "cadence", "time_in_cadence_zone", "cadence_zone_high_boundary");
        }
        return result;
    }

    private static void AddZones(
        ICollection<FitZoneData> destination,
        CanonicalMessage message,
        string zoneType,
        string durationField,
        string boundaryField)
    {
        var durations = GetDecimals(message.Fields, durationField);
        var boundaries = GetDecimals(message.Fields, boundaryField);
        for (var index = 0; index < durations.Count; index++)
        {
            if (durations[index] is null)
            {
                continue;
            }
            destination.Add(new FitZoneData(
                zoneType,
                index,
                index == 0 || index - 1 >= boundaries.Count ? null : boundaries[index - 1],
                index >= boundaries.Count ? null : boundaries[index],
                durations[index]!.Value,
                $"time_in_zone:{message.Sequence}"));
        }
    }

    private static IReadOnlyList<CanonicalMessage> GetMessages(CanonicalFit source, string name) =>
        source.Messages.TryGetValue(name, out var messages) ? messages : [];

    private static decimal? NormalizeCadence(decimal? cadence, decimal? fractional, long? sport) =>
        cadence is null
            ? null
            : (cadence + (fractional ?? 0m)) * (sport == 1 ? 2m : 1m);

    private static string SportName(long? sport) => sport switch
    {
        0 => "generic",
        1 => "running",
        2 => "cycling",
        4 => "fitness_equipment",
        5 => "swimming",
        10 => "training",
        11 => "walking",
        _ => sport is null ? "unknown" : $"sport_{sport.Value}"
    };

    private static string ActivityCategory(long? sport) => sport switch
    {
        1 => "running",
        2 => "cycling",
        4 => "cardio",
        10 => "strength",
        5 => "swimming",
        11 => "walking",
        _ => "other"
    };

    private static string ActivityType(long? sport, long? subSport) => (sport, subSport) switch
    {
        (1, 1) => "treadmill_running",
        (1, _) => "running",
        (2, 6) => "indoor_cycling",
        (2, _) => "cycling",
        (4, _) => "cardio",
        (5, _) => "swimming",
        (10, _) => "strength_training",
        (11, _) => "walking",
        _ => SportName(sport)
    };

    private static string? Modality(long? sport, long? subSport) => (sport, subSport) switch
    {
        (1, 1) => "treadmill",
        (1, _) => "outdoor",
        (2, 6) => "indoor",
        (2, _) => "outdoor",
        (4, _) => "indoor",
        _ => null
    };

    private static string? SubSportName(long? value) =>
        value is null ? null : $"sub_sport_{value.Value}";

    private static decimal? ToDegrees(decimal? semicircles) =>
        semicircles is null ? null : semicircles * (decimal)SemicircleToDegrees;

    private static string SerializeFields(IReadOnlyDictionary<string, object?> fields) =>
        JsonSerializer.Serialize(fields);

    private static string SerializeAdditional(
        IReadOnlyDictionary<string, object?> fields,
        params string[] excluded)
    {
        var exclusions = new HashSet<string>(excluded, StringComparer.Ordinal);
        return JsonSerializer.Serialize(fields
            .Where(pair => !exclusions.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static string? GetString(
        IReadOnlyDictionary<string, object?>? fields,
        string name) =>
        fields is not null && fields.TryGetValue(name, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

    private static string? GetStringOrNumber(
        IReadOnlyDictionary<string, object?> fields,
        string name) => GetString(fields, name);

    private static long? GetLong(
        IReadOnlyDictionary<string, object?> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static int? GetInt(
        IReadOnlyDictionary<string, object?> fields,
        string name)
    {
        var value = GetLong(fields, name);
        return value is null ? null : checked((int)value.Value);
    }

    private static decimal? GetDecimal(
        IReadOnlyDictionary<string, object?>? fields,
        string name)
    {
        if (fields is null || !fields.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }
        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<decimal?> GetDecimals(
        IReadOnlyDictionary<string, object?> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var value) || value is null)
        {
            return [];
        }
        if (value is IEnumerable enumerable && value is not string)
        {
            return enumerable.Cast<object?>()
                .Select(item => item is null
                    ? (decimal?)null
                    : Convert.ToDecimal(item, CultureInfo.InvariantCulture))
                .ToArray();
        }
        return [Convert.ToDecimal(value, CultureInfo.InvariantCulture)];
    }

    private static DateTime? GetUtcDateTime(
        IReadOnlyDictionary<string, object?>? fields,
        string name)
    {
        var value = GetString(fields, name);
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var result)
            ? DateTime.SpecifyKind(result, DateTimeKind.Utc)
            : null;
    }

    private static DateTime? GetLocalDateTime(
        IReadOnlyDictionary<string, object?>? fields,
        string name)
    {
        var value = GetString(fields, name);
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result)
            ? DateTime.SpecifyKind(result, DateTimeKind.Unspecified)
            : null;
    }
}

public sealed record FitActivityData(
    CanonicalFit Canonical,
    FitActivitySummary Summary,
    IReadOnlyList<FitSessionData> Sessions,
    IReadOnlyList<FitLapData> Laps,
    IReadOnlyList<FitEventData> Events,
    IReadOnlyList<FitZoneData> Zones,
    IReadOnlyList<FitSampleData> Samples);

public sealed record FitActivitySummary(
    DateTime? StartedAtLocal,
    DateTime? StartedAtUtc,
    string ActivityType,
    string ActivityCategory,
    string? Modality,
    string? Title,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? ElapsedSeconds,
    decimal? AverageSpeedMps,
    decimal? Calories,
    decimal? AverageHeartRateBpm,
    decimal? MaxHeartRateBpm,
    decimal? AverageCadenceSpm,
    decimal? AveragePowerW,
    decimal? ElevationGainM,
    int? LapCount)
{
    public decimal? AveragePaceSecondsPerKm =>
        DistanceM is > 0 && DurationSeconds is not null
            ? DurationSeconds.Value / (DistanceM.Value / 1000m)
            : null;
}

public sealed record FitSessionData(
    int Sequence,
    string Sport,
    string? SubSport,
    DateTime? StartedAtUtc,
    decimal? DurationSeconds,
    decimal? DistanceM,
    string SummaryJson);

public sealed record FitLapData(
    int Index,
    int? SessionIndex,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    decimal? DurationSeconds,
    decimal? DistanceM,
    string SummaryJson);

public sealed record FitEventData(
    int Index,
    DateTime? RecordedAtUtc,
    string? EventName,
    string? EventType,
    string? EventGroup,
    string? EventData,
    string AdditionalFieldsJson);

public sealed record FitZoneData(
    string ZoneType,
    int ZoneIndex,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal DurationSeconds,
    string SourceReference);

public sealed record FitSampleData(
    int Index,
    DateTime? RecordedAtUtc,
    decimal? DistanceM,
    decimal? LatitudeDegrees,
    decimal? LongitudeDegrees,
    decimal? AltitudeM,
    decimal? SpeedMps,
    decimal? HeartRateBpm,
    decimal? CadenceSpm,
    decimal? PowerW,
    decimal? TemperatureC,
    string AdditionalFieldsJson);
