using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RunningPerformance.Application.Ingestion;

public sealed record NormalizedActivityRow(
    int Ordinal,
    int SourceRowNumber,
    string ProvisionalActivityKey,
    long? GarminActivityId,
    string ActivityType,
    string ActivityCategory,
    string Modality,
    DateTime StartedAtLocal,
    string? Title,
    decimal? DistanceM,
    decimal? DurationSeconds,
    decimal? MovingSeconds,
    decimal? ElapsedSeconds,
    decimal? AveragePaceSecondsPerKm,
    decimal? AverageSpeedMps,
    decimal? Calories,
    decimal? AverageHeartRateBpm,
    decimal? MaxHeartRateBpm,
    decimal? AverageCadenceSpm,
    decimal? AveragePowerW,
    decimal? ElevationGainM,
    int? LapCount,
    IReadOnlyDictionary<string, string?> SourceValues);

public sealed record CsvRowError(
    int Ordinal,
    int? SourceRowNumber,
    string? ObservedKey,
    string Code,
    string Message);

public sealed record NormalizedCsvValidationResult(
    IReadOnlyList<NormalizedActivityRow> Rows,
    IReadOnlyList<CsvRowError> Errors,
    int ObservedRowCount)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed partial class NormalizedActivityCsvValidator(HistoricalImportOptions options)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<NormalizedCsvValidationResult> ValidateAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string text;
        try
        {
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            return InvalidFile("csv_invalid_utf8", "The CSV must be valid UTF-8.");
        }

        IReadOnlyList<string[]> records;
        try
        {
            records = ParseCsv(text, cancellationToken);
        }
        catch (CsvFormatException exception)
        {
            return InvalidFile("csv_invalid_format", exception.Message);
        }

        if (records.Count == 0)
        {
            return InvalidFile("csv_empty", "The CSV is empty.");
        }

        if (!records[0].SequenceEqual(NormalizedActivityCsvContract.Headers, StringComparer.Ordinal))
        {
            return InvalidFile(
                "csv_header_mismatch",
                $"Expected the exact {NormalizedActivityCsvContract.Headers.Length}-column normalized CSV header.");
        }

        var parsedRows = new List<NormalizedActivityRow>(Math.Max(0, records.Count - 1));
        var errors = new List<CsvRowError>();
        var observedKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var observedGarminIds = new Dictionary<long, int>();

        for (var index = 1; index < records.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = index;
            var record = records[index];
            if (record.Length == 1 && record[0].Length == 0 && index == records.Count - 1)
            {
                continue;
            }

            if (record.Length != NormalizedActivityCsvContract.Headers.Length)
            {
                errors.Add(new(
                    ordinal,
                    null,
                    null,
                    "csv_column_count",
                    $"Row {ordinal + 1} has {record.Length} columns; expected {NormalizedActivityCsvContract.Headers.Length}."));
                continue;
            }

            var values = NormalizedActivityCsvContract.Headers
                .Select((header, column) => new KeyValuePair<string, string?>(
                    header,
                    NullIfMissing(record[column])))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var rowErrors = new List<string>();
            var sourceRow = RequiredInt(values, "source_row_number", rowErrors, minimum: 2);
            var key = Required(values, "provisional_activity_key", rowErrors);
            if (key is not null && !Sha256Regex().IsMatch(key))
            {
                rowErrors.Add("provisional_activity_key must be a lowercase SHA-256 value");
            }

            var garminId = OptionalLong(values, "garmin_activity_id", rowErrors, minimum: 1);
            var activityType = Required(values, "activity_type", rowErrors);
            var activityCategory = Required(values, "activity_category", rowErrors);
            var startedAt = RequiredLocalTimestamp(values, "started_at_local", rowErrors);
            var duration = OptionalDecimal(values, "duration_seconds", rowErrors, minimum: 0);
            if (duration is null)
            {
                rowErrors.Add("duration_seconds is required");
            }

            var distanceValue = OptionalDecimal(values, "distance_value", rowErrors, minimum: 0);
            var distanceUnit = values["distance_unit"];
            var distanceM = ConvertDistance(distanceValue, distanceUnit, rowErrors);
            var pace = OptionalDecimal(values, "average_pace_seconds", rowErrors, minimum: 0);
            var pacePerKm = ConvertPace(pace, values["pace_basis"], rowErrors);
            var speedKph = OptionalDecimal(values, "average_speed_kph", rowErrors, minimum: 0);
            var lapCount = OptionalInt(values, "lap_count", rowErrors, minimum: 0);
            var movingSeconds = OptionalDecimal(values, "moving_time_seconds", rowErrors, minimum: 0);
            var elapsedSeconds = OptionalDecimal(values, "elapsed_time_seconds", rowErrors, minimum: 0);
            var calories = OptionalDecimal(values, "calories_kcal", rowErrors, minimum: 0);
            var averageHeartRate = OptionalDecimal(values, "average_heart_rate_bpm", rowErrors, minimum: 0);
            var maximumHeartRate = OptionalDecimal(values, "maximum_heart_rate_bpm", rowErrors, minimum: 0);
            var averageCadence = OptionalDecimal(values, "average_cadence", rowErrors, minimum: 0);
            var averagePower = OptionalDecimal(values, "average_power_w", rowErrors, minimum: 0);
            var elevationGain = OptionalDecimal(values, "total_ascent_m", rowErrors, minimum: 0);
            ValidateRemainingFields(values, rowErrors);

            var candidateSourceRow = sourceRow ?? ordinal + 1;
            if (sourceRow.HasValue && sourceRow.Value != ordinal + 1)
            {
                rowErrors.Add($"source_row_number must be {ordinal + 1}");
            }

            if (key is not null && observedKeys.TryGetValue(key, out var firstKeyOrdinal))
            {
                rowErrors.Add($"provisional_activity_key duplicates data row {firstKeyOrdinal}");
            }

            if (garminId.HasValue && observedGarminIds.TryGetValue(garminId.Value, out var firstGarminOrdinal))
            {
                rowErrors.Add($"garmin_activity_id duplicates data row {firstGarminOrdinal}");
            }

            if (rowErrors.Count > 0)
            {
                errors.Add(new(
                    ordinal,
                    sourceRow,
                    key,
                    "csv_row_invalid",
                    string.Join("; ", rowErrors)));
                continue;
            }

            observedKeys.Add(key!, ordinal);
            if (garminId.HasValue)
            {
                observedGarminIds.Add(garminId.Value, ordinal);
            }

            parsedRows.Add(new(
                ordinal,
                candidateSourceRow,
                key!,
                garminId,
                activityType!,
                activityCategory!,
                ResolveModality(activityType!),
                startedAt!.Value,
                values["title"],
                distanceM,
                duration,
                movingSeconds,
                elapsedSeconds,
                pacePerKm,
                speedKph / 3.6m,
                calories,
                averageHeartRate,
                maximumHeartRate,
                averageCadence,
                averagePower,
                elevationGain,
                lapCount,
                values));
        }

        var observedRowCount = records.Count - 1;
        if (observedRowCount != options.ExpectedRowCount)
        {
            errors.Add(new(
                Math.Max(1, observedRowCount),
                null,
                null,
                "csv_row_count",
                $"The historical import requires {options.ExpectedRowCount} rows; observed {observedRowCount}."));
        }

        return new(parsedRows, errors, observedRowCount);
    }

    private static NormalizedCsvValidationResult InvalidFile(string code, string message) =>
        new([], [new(1, null, null, code, message)], 0);

    private static string? Required(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors)
    {
        var value = values[field];
        if (value is null)
        {
            errors.Add($"{field} is required");
        }

        return value;
    }

    private static int? RequiredInt(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors,
        int minimum)
    {
        var value = OptionalInt(values, field, errors, minimum);
        if (!value.HasValue && values[field] is null)
        {
            errors.Add($"{field} is required");
        }

        return value;
    }

    private static int? OptionalInt(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors,
        int minimum)
    {
        var value = values[field];
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, Invariant, out var parsed) || parsed < minimum)
        {
            errors.Add($"{field} must be an integer greater than or equal to {minimum}");
            return null;
        }

        return parsed;
    }

    private static long? OptionalLong(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors,
        long minimum)
    {
        var value = values[field];
        if (value is null)
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.Integer, Invariant, out var parsed) || parsed < minimum)
        {
            errors.Add($"{field} must be an integer greater than or equal to {minimum}");
            return null;
        }

        return parsed;
    }

    private static decimal? OptionalDecimal(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors,
        decimal minimum)
    {
        var value = values[field];
        if (value is null)
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.Float, Invariant, out var parsed) || parsed < minimum)
        {
            errors.Add($"{field} must be a number greater than or equal to {minimum.ToString(Invariant)}");
            return null;
        }

        return parsed;
    }

    private static void ValidateRemainingFields(
        IReadOnlyDictionary<string, string?> values,
        ICollection<string> errors)
    {
        string[] nonNegativeDecimalFields =
        [
            "aerobic_training_effect",
            "maximum_cadence",
            "maximum_pace_seconds",
            "maximum_speed_kph",
            "total_descent_m",
            "average_stride_length_m",
            "average_vertical_ratio_percent",
            "average_vertical_oscillation_cm",
            "average_ground_contact_time_ms",
            "ground_contact_balance_left_percent",
            "ground_contact_balance_right_percent",
            "grade_adjusted_pace_seconds_per_km",
            "normalized_power_w",
            "training_stress_score",
            "maximum_power_w",
            "total_cycles_or_strokes",
            "average_swolf",
            "average_stroke_rate_per_minute",
            "steps",
            "total_repetitions",
            "total_sets",
            "best_lap_seconds",
            "average_respiration_brpm",
            "minimum_respiration_brpm",
            "maximum_respiration_brpm"
        ];
        foreach (var field in nonNegativeDecimalFields)
        {
            _ = OptionalDecimal(values, field, errors, minimum: 0);
        }

        string[] signedDecimalFields =
        [
            "body_battery_drain",
            "minimum_temperature_c",
            "maximum_temperature_c",
            "minimum_altitude_m",
            "maximum_altitude_m"
        ];
        foreach (var field in signedDecimalFields)
        {
            _ = OptionalDecimal(values, field, errors, decimal.MinValue);
        }

        ValidateOptionalBoolean(values, "favorite", errors);
        ValidateOptionalBoolean(values, "decompression_required", errors);
    }

    private static void ValidateOptionalBoolean(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors)
    {
        var value = values[field];
        if (value is not null && !bool.TryParse(value, out _))
        {
            errors.Add($"{field} must be true or false");
        }
    }

    private static DateTime? RequiredLocalTimestamp(
        IReadOnlyDictionary<string, string?> values,
        string field,
        ICollection<string> errors)
    {
        var value = Required(values, field, errors);
        if (value is null)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss",
                Invariant,
                DateTimeStyles.None,
                out var parsed))
        {
            errors.Add($"{field} must use yyyy-MM-ddTHH:mm:ss without an invented offset");
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
    }

    private static decimal? ConvertDistance(decimal? value, string? unit, ICollection<string> errors)
    {
        if (!value.HasValue)
        {
            if (unit is not null)
            {
                errors.Add("distance_unit must be empty when distance_value is empty");
            }

            return null;
        }

        if (value == 0 && unit is null)
        {
            // Garmin exports non-distance activities with a zero sentinel and no unit.
            return null;
        }

        return unit switch
        {
            "km" => value * 1000m,
            "m" => value,
            _ => AddConversionError(errors, "distance_unit must be km or m when distance_value is present")
        };
    }

    private static decimal? ConvertPace(decimal? value, string? basis, ICollection<string> errors)
    {
        if (!value.HasValue)
        {
            if (basis is not null)
            {
                errors.Add("pace_basis must be empty when average_pace_seconds is empty");
            }

            return null;
        }

        return basis switch
        {
            "per_km" => value,
            "per_100m" => value * 10m,
            _ => AddConversionError(errors, "pace_basis must be per_km or per_100m when pace is present")
        };
    }

    private static decimal? AddConversionError(ICollection<string> errors, string message)
    {
        errors.Add(message);
        return null;
    }

    private static string ResolveModality(string activityType) => activityType switch
    {
        "treadmill_running" => "treadmill",
        "running" or "cycling" or "hiking" => "outdoor",
        "indoor_cycling" or "strength_training" or "cardio" => "indoor",
        "pool_swimming" => "pool",
        _ => "unspecified"
    };

    private static string? NullIfMissing(string value) =>
        string.IsNullOrWhiteSpace(value) || value == "--" ? null : value.Trim();

    private static IReadOnlyList<string[]> ParseCsv(string text, CancellationToken cancellationToken)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        var atFieldStart = true;

        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (closedQuote && character is not (',' or '\r' or '\n'))
            {
                throw new CsvFormatException("Unexpected text after a closing quote.");
            }

            if (character == '"')
            {
                if (!atFieldStart)
                {
                    throw new CsvFormatException("A quote may only begin an empty CSV field.");
                }

                quoted = true;
                atFieldStart = false;
            }
            else if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                closedQuote = false;
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                fields.Add(field.ToString());
                field.Clear();
                records.Add([.. fields]);
                fields.Clear();
                atFieldStart = true;
                closedQuote = false;
            }
            else
            {
                field.Append(character);
                atFieldStart = false;
            }
        }

        if (quoted)
        {
            throw new CsvFormatException("The CSV ends inside a quoted field.");
        }

        if (field.Length > 0 || fields.Count > 0 || (text.Length > 0 && text[^1] == ','))
        {
            fields.Add(field.ToString());
            records.Add([.. fields]);
        }

        return records;
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    private sealed class CsvFormatException(string message) : Exception(message);
}
