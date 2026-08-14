using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RunningPerformance.Application.Ingestion;

namespace RunningPerformance.UnitTests;

internal static class SyntheticNormalizedCsvFixture
{
    public static MemoryStream Create(int rowCount = 460, bool duplicateLastKey = false)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', NormalizedActivityCsvContract.Headers.Select(Escape)));
        string? firstKey = null;
        for (var index = 1; index <= rowCount; index++)
        {
            var activityType = (index % 7) switch
            {
                0 => "running",
                1 => "treadmill_running",
                2 => "strength_training",
                3 => "walking",
                4 => "indoor_cycling",
                5 => "pool_swimming",
                _ => "cardio"
            };
            var category = activityType switch
            {
                "running" or "treadmill_running" => "running",
                "strength_training" => "strength",
                "walking" => "walking",
                "indoor_cycling" => "cycling",
                "pool_swimming" => "swimming",
                _ => "cardio"
            };
            var key = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-activity-{index}")))
                .ToLowerInvariant();
            firstKey ??= key;
            if (duplicateLastKey && index == rowCount)
            {
                key = firstKey;
            }

            var hasDistance = activityType is not ("strength_training" or "cardio");
            var hasPace = activityType is "running" or "treadmill_running";
            var values = NormalizedActivityCsvContract.Headers
                .ToDictionary(header => header, _ => string.Empty, StringComparer.Ordinal);
            values["source_row_number"] = (index + 1).ToString(CultureInfo.InvariantCulture);
            values["provisional_activity_key"] = key;
            values["garmin_activity_id"] = index <= 2 ? (90000000000L + index).ToString(CultureInfo.InvariantCulture) : string.Empty;
            values["activity_type"] = activityType;
            values["activity_type_source"] = $"Synthetic {activityType}";
            values["activity_category"] = category;
            values["started_at_local"] = new DateTime(2025, 1, 1).AddDays(index - 1).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            values["favorite"] = "false";
            values["title"] = index == 10 ? "Synthetic, quoted \"session\"" : $"Synthetic session {index}";
            values["distance_value"] = hasDistance ? (3m + index % 15).ToString(CultureInfo.InvariantCulture) : "0";
            values["distance_unit"] = hasDistance ? "km" : string.Empty;
            values["calories_kcal"] = index % 10 == 0 ? string.Empty : (100 + index).ToString(CultureInfo.InvariantCulture);
            values["duration_seconds"] = (900 + index).ToString(CultureInfo.InvariantCulture);
            values["average_heart_rate_bpm"] = index % 11 == 0 ? string.Empty : "145";
            values["maximum_heart_rate_bpm"] = index % 11 == 0 ? string.Empty : "172";
            values["average_cadence"] = hasPace ? "165" : string.Empty;
            values["cadence_unit"] = hasPace ? "steps_per_minute" : string.Empty;
            values["average_pace_seconds"] = hasPace ? "360" : string.Empty;
            values["pace_basis"] = hasPace ? "per_km" : string.Empty;
            values["average_speed_kph"] = hasDistance ? "10" : string.Empty;
            values["training_stress_score"] = "0";
            values["body_battery_drain"] = index % 3 == 0 ? "-2" : string.Empty;
            values["decompression_required"] = "false";
            values["lap_count"] = hasDistance ? "1" : string.Empty;
            values["moving_time_seconds"] = (890 + index).ToString(CultureInfo.InvariantCulture);
            values["elapsed_time_seconds"] = (900 + index).ToString(CultureInfo.InvariantCulture);

            builder.AppendLine(string.Join(
                ',',
                NormalizedActivityCsvContract.Headers.Select(header => Escape(values[header]))));
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()), writable: false);
    }

    private static string Escape(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}
