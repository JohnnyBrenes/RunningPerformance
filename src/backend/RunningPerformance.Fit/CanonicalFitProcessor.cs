using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dynastream.Fit;
using IoFile = System.IO.File;

namespace RunningPerformance.Fit;

public static class CanonicalFitProcessor
{
    public const string ProcessorVersion = "1.0.0";
    public const string SdkVersion = "21.205.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int RunCli(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: FitProcessor <garmin-activity-id> <input.fit> <output.json>");
            return 2;
        }

        string activityId = args[0];
        string inputPath = Path.GetFullPath(args[1]);
        string outputPath = Path.GetFullPath(args[2]);
        if (string.IsNullOrWhiteSpace(activityId) || !activityId.All(char.IsAsciiDigit))
        {
            Console.Error.WriteLine("Garmin activity ID must contain only ASCII digits.");
            return 2;
        }
        if (!IoFile.Exists(inputPath))
        {
            Console.Error.WriteLine($"FIT file not found: {inputPath}");
            return 2;
        }
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Input and output paths must be different.");
            return 2;
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            Console.Error.WriteLine($"Output directory not found: {outputDirectory}");
            return 2;
        }

        string temporaryPath = outputPath + ".tmp";
        try
        {
            CanonicalFit result = Process(activityId, inputPath);
            string json = Serialize(result);
            IoFile.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            IoFile.Move(temporaryPath, outputPath, overwrite: true);

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ActivityId = activityId,
                InputFile = result.Source.OriginalFileName,
                OutputPath = outputPath,
                SourceSha256 = result.Source.Sha256,
                OutputSha256 = ComputeSha256(outputPath),
                result.Counts.MessageTypeCount,
                result.Counts.TotalMessageCount,
                result.Counts.SessionCount,
                result.Counts.LapCount,
                result.Counts.EventCount,
                result.Counts.RecordCount,
                result.Counts.FilteredInvalidValueCount,
                WarningCount = result.Warnings.Count,
                IntegrityValid = result.Validation.IntegrityValid
            }, JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            if (IoFile.Exists(temporaryPath))
            {
                IoFile.Delete(temporaryPath);
            }
        }
    }

    public static CanonicalFit Process(string activityId, string inputPath)
    {
        if (string.IsNullOrWhiteSpace(activityId) || !activityId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Garmin activity ID must contain only ASCII digits.",
                nameof(activityId));
        }

        inputPath = Path.GetFullPath(inputPath);
        if (!IoFile.Exists(inputPath))
        {
            throw new FileNotFoundException("FIT file not found.", inputPath);
        }

        FitHeader header = FitHeaderReader.Read(inputPath);
        string sha256 = ComputeSha256(inputPath);
        var validationDecoder = new Decode();
        bool isFit;
        bool integrityValid;
        using (var validationStream = IoFile.OpenRead(inputPath))
        {
            isFit = validationDecoder.IsFIT(validationStream);
            validationStream.Position = 0;
            integrityValid = validationDecoder.CheckIntegrity(validationStream);
        }
        if (!isFit || !integrityValid)
        {
            throw new InvalidDataException(
                $"FIT validation failed. IsFIT={isFit}, CheckIntegrity={integrityValid}.");
        }

        var collector = new CanonicalCollector();
        var decoder = new Decode();
        decoder.MesgEvent += (_, eventArgs) => collector.Add(eventArgs.mesg);

        bool readSuccessful;
        using (var inputStream = IoFile.OpenRead(inputPath))
        {
            readSuccessful = decoder.Read(inputStream);
        }
        if (!readSuccessful)
        {
            throw new InvalidDataException("Garmin FIT SDK could not decode the complete file.");
        }

        var fileInfo = new FileInfo(inputPath);
        return collector.Build(
            activityId,
            fileInfo.Name,
            fileInfo.Length,
            sha256,
            header,
            isFit,
            integrityValid,
            readSuccessful,
            ProcessorVersion,
            SdkVersion);
    }

    public static string Serialize(CanonicalFit value) =>
        JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;

    private static string ComputeSha256(string path)
    {
        using FileStream stream = IoFile.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

internal static class FitHeaderReader
{
    public static FitHeader Read(string path)
    {
        byte[] bytes = new byte[14];
        using FileStream stream = IoFile.OpenRead(path);
        int bytesRead = stream.Read(bytes, 0, bytes.Length);
        if (bytesRead < 12)
        {
            throw new InvalidDataException("File is too small to contain a FIT header.");
        }

        int headerSize = bytes[0];
        if (headerSize is not (12 or 14))
        {
            throw new InvalidDataException($"Unsupported FIT header size: {headerSize}.");
        }
        if (bytesRead < headerSize)
        {
            throw new InvalidDataException("FIT header is truncated.");
        }

        string signature = Encoding.ASCII.GetString(bytes, 8, 4);
        if (!string.Equals(signature, ".FIT", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Invalid FIT signature: {signature}.");
        }

        byte protocolVersion = bytes[1];
        ushort profileVersion = BitConverter.ToUInt16(bytes, 2);
        uint declaredDataSize = BitConverter.ToUInt32(bytes, 4);
        long expectedWithoutFileCrc = headerSize + declaredDataSize;
        long trailingBytes = stream.Length - expectedWithoutFileCrc;
        if (trailingBytes is not (0 or 2))
        {
            throw new InvalidDataException(
                $"Unexpected bytes after FIT data. Expected 0 or 2, found {trailingBytes}.");
        }

        return new FitHeader(
            headerSize,
            protocolVersion,
            protocolVersion >> 4,
            protocolVersion & 0x0F,
            profileVersion,
            declaredDataSize,
            headerSize == 14,
            trailingBytes == 2,
            trailingBytes);
    }
}

internal sealed class CanonicalCollector
{
    private readonly SortedDictionary<string, List<CanonicalMessage>> messages =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, MessageSchemaBuilder> schemas = new();
    private readonly Dictionary<string, int> sequenceByMessageKey =
        new(StringComparer.Ordinal);
    private int messageIndex;

    public void Add(Mesg message)
    {
        string messageKey = CanonicalNames.MessageKey(message.Name, message.Num);
        if (!schemas.TryGetValue(message.Num, out MessageSchemaBuilder? schema))
        {
            schema = new MessageSchemaBuilder(messageKey, message.Name, message.Num);
            schemas.Add(message.Num, schema);
        }
        else if (!string.Equals(schema.MessageKey, messageKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Global message {message.Num} changed canonical key from " +
                $"{schema.MessageKey} to {messageKey}.");
        }

        schema.MessageOccurrences++;
        int sequence = sequenceByMessageKey.GetValueOrDefault(messageKey);
        sequenceByMessageKey[messageKey] = sequence + 1;

        var fieldValues = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (Field field in message.Fields.OrderBy(item => item.Num))
        {
            AddField(field, false, null, schema, fieldValues);
        }
        foreach (DeveloperField field in message.DeveloperFields
                     .OrderBy(item => item.DeveloperDataIndex)
                     .ThenBy(item => item.Num)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            AddField(field, true, field.DeveloperDataIndex, schema, fieldValues);
        }

        if (!messages.TryGetValue(messageKey, out List<CanonicalMessage>? instances))
        {
            instances = new List<CanonicalMessage>();
            messages.Add(messageKey, instances);
        }
        instances.Add(new CanonicalMessage(messageIndex++, sequence, message.Num, fieldValues));
    }

    public CanonicalFit Build(
        string activityId,
        string fileName,
        long fileSize,
        string sha256,
        FitHeader header,
        bool isFit,
        bool integrityValid,
        bool readSuccessful,
        string processorVersion,
        string sdkVersion)
    {
        List<MessageSchema> finalizedSchemas = schemas.Values
            .OrderBy(item => item.MessageKey, StringComparer.Ordinal)
            .ThenBy(item => item.GlobalMessageNumber)
            .Select(item => item.Build())
            .ToList();

        long invalidCount = finalizedSchemas
            .SelectMany(item => item.Fields)
            .Sum(item => item.InvalidValueCount);
        long developerValueCount = finalizedSchemas
            .SelectMany(item => item.Fields)
            .Where(item => item.IsDeveloperField)
            .Sum(item => item.ValidValueCount);

        var counts = new ExtractionCounts(
            finalizedSchemas.Count,
            messageIndex,
            Count("activity"),
            Count("session"),
            Count("lap"),
            Count("event"),
            Count("time_in_zone"),
            Count("record"),
            invalidCount,
            developerValueCount);

        List<CanonicalWarning> warnings = BuildWarnings(finalizedSchemas);
        return new CanonicalFit(
            1,
            new CanonicalizerInfo("FitProcessor", processorVersion, "Garmin.FIT.Sdk", sdkVersion),
            new SourceInfo(activityId, fileName, fileSize, sha256),
            new ValidationResult(header, isFit, integrityValid, readSuccessful),
            counts,
            finalizedSchemas,
            messages,
            warnings);
    }

    private int Count(string messageKey) =>
        messages.TryGetValue(messageKey, out List<CanonicalMessage>? instances)
            ? instances.Count
            : 0;

    private static List<CanonicalWarning> BuildWarnings(IEnumerable<MessageSchema> schemas)
    {
        var warnings = new List<CanonicalWarning>();
        foreach (MessageSchema schema in schemas)
        {
            if (schema.MessageKey.StartsWith("unknown_", StringComparison.Ordinal))
            {
                warnings.Add(new CanonicalWarning(
                    "unknown_message",
                    schema.GlobalMessageNumber,
                    null,
                    schema.MessageOccurrences,
                    $"Global message {schema.GlobalMessageNumber} has no public profile name; " +
                    "it was preserved without semantic interpretation."));
            }

            foreach (FieldSchema field in schema.Fields)
            {
                if (string.Equals(field.OriginalName, "unknown", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(field.OriginalName))
                {
                    warnings.Add(new CanonicalWarning(
                        "unknown_field",
                        schema.GlobalMessageNumber,
                        field.FieldNumber,
                        field.MessageOccurrences,
                        $"Field {field.FieldNumber} in global message " +
                        $"{schema.GlobalMessageNumber} was preserved without semantic interpretation."));
                }
                if (field.InvalidValueCount > 0)
                {
                    warnings.Add(new CanonicalWarning(
                        "invalid_values_filtered",
                        schema.GlobalMessageNumber,
                        field.FieldNumber,
                        field.InvalidValueCount,
                        $"Filtered {field.InvalidValueCount} FIT invalid/sentinel value(s) from " +
                        $"{schema.MessageKey}.{field.Key}."));
                }
                if (field.IsDeveloperField)
                {
                    warnings.Add(new CanonicalWarning(
                        "developer_field",
                        schema.GlobalMessageNumber,
                        field.FieldNumber,
                        field.ValidValueCount,
                        $"Developer field {field.Key} was preserved with its declared metadata."));
                }
            }
        }

        return warnings
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.GlobalMessageNumber)
            .ThenBy(item => item.FieldNumber)
            .ToList();
    }

    private static void AddField(
        FieldBase field,
        bool isDeveloper,
        byte? developerDataIndex,
        MessageSchemaBuilder schema,
        IDictionary<string, object?> destination)
    {
        byte fieldNumber = field switch
        {
            DeveloperField developerField => developerField.Num,
            Field standardField => standardField.Num,
            _ => throw new InvalidDataException(
                $"Unsupported FIT field type: {((object)field).GetType().FullName}")
        };

        string key = CanonicalNames.FieldKey(
            field.Name,
            fieldNumber,
            isDeveloper,
            developerDataIndex);
        if (destination.ContainsKey(key))
        {
            key += $"__field_{fieldNumber}";
        }

        string? profileType = field is Field standard
            ? standard.ProfileType.ToString()
            : null;
        FieldSchemaBuilder fieldSchema = schema.GetOrAddField(
            key,
            field.Name,
            fieldNumber,
            field.Units,
            field.Type,
            FitTypes.Name(field.Type),
            profileType,
            isDeveloper,
            developerDataIndex);
        fieldSchema.MessageOccurrences++;

        int numberOfValues = field.GetNumValues();
        if (field.Type == FitBaseType.String)
        {
            string? text = NormalizeStringField(field, numberOfValues);
            if (!string.IsNullOrEmpty(text))
            {
                fieldSchema.ValidValueCount++;
                destination[key] = text;
            }
            return;
        }

        var values = new List<object?>(numberOfValues);
        bool hasValidValue = false;
        for (int index = 0; index < numberOfValues; index++)
        {
            object? rawValue = field.GetRawValue(index);
            object? value = field.GetValue(index);
            object? normalized = FitValueNormalizer.Normalize(
                rawValue,
                value,
                field.Type,
                key,
                out bool invalid);
            if (invalid)
            {
                fieldSchema.InvalidValueCount++;
                values.Add(null);
                continue;
            }
            if (normalized is not null)
            {
                hasValidValue = true;
                fieldSchema.ValidValueCount++;
            }
            values.Add(normalized);
        }

        if (!hasValidValue)
        {
            return;
        }
        destination[key] = values.Count == 1 ? values[0] : values;
    }

    private static string? NormalizeStringField(FieldBase field, int numberOfValues)
    {
        var bytes = new List<byte>();
        for (int index = 0; index < numberOfValues; index++)
        {
            object? value = field.GetValue(index);
            if (value is string text)
            {
                int terminator = text.IndexOf('\0');
                return (terminator >= 0 ? text[..terminator] : text).TrimEnd();
            }
            if (value is byte[] buffer)
            {
                foreach (byte item in buffer)
                {
                    if (item == 0)
                    {
                        return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
                    }
                    bytes.Add(item);
                }
                continue;
            }
            if (value is byte itemByte)
            {
                if (itemByte == 0)
                {
                    return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
                }
                bytes.Add(itemByte);
            }
        }
        return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
    }
}

internal sealed class MessageSchemaBuilder(
    string messageKey,
    string originalName,
    ushort globalMessageNumber)
{
    private readonly SortedDictionary<string, FieldSchemaBuilder> fields =
        new(StringComparer.Ordinal);

    public string MessageKey { get; } = messageKey;
    public string OriginalName { get; } = originalName;
    public ushort GlobalMessageNumber { get; } = globalMessageNumber;
    public int MessageOccurrences { get; set; }

    public FieldSchemaBuilder GetOrAddField(
        string key,
        string originalName,
        byte fieldNumber,
        string units,
        byte baseTypeNumber,
        string baseTypeName,
        string? profileType,
        bool isDeveloperField,
        byte? developerDataIndex)
    {
        if (!fields.TryGetValue(key, out FieldSchemaBuilder? field))
        {
            field = new FieldSchemaBuilder(
                key,
                originalName,
                fieldNumber,
                units,
                baseTypeNumber,
                baseTypeName,
                profileType,
                isDeveloperField,
                developerDataIndex);
            fields.Add(key, field);
        }
        return field;
    }

    public MessageSchema Build() => new(
        MessageKey,
        OriginalName,
        GlobalMessageNumber,
        MessageOccurrences,
        fields.Values.Select(item => item.Build()).ToList());
}

internal sealed class FieldSchemaBuilder(
    string key,
    string originalName,
    byte fieldNumber,
    string units,
    byte baseTypeNumber,
    string baseTypeName,
    string? profileType,
    bool isDeveloperField,
    byte? developerDataIndex)
{
    public int MessageOccurrences { get; set; }
    public long ValidValueCount { get; set; }
    public long InvalidValueCount { get; set; }

    public FieldSchema Build() => new(
        key,
        originalName,
        fieldNumber,
        units,
        baseTypeNumber,
        baseTypeName,
        profileType,
        isDeveloperField,
        developerDataIndex,
        MessageOccurrences,
        ValidValueCount,
        InvalidValueCount);
}

internal static class CanonicalNames
{
    public static string MessageKey(string name, ushort globalMessageNumber)
    {
        string normalized = ToSnakeCase(name);
        return string.IsNullOrWhiteSpace(normalized) ||
               string.Equals(normalized, "unknown", StringComparison.Ordinal)
            ? $"unknown_{globalMessageNumber}"
            : normalized;
    }

    public static string FieldKey(
        string name,
        byte fieldNumber,
        bool isDeveloper,
        byte? developerDataIndex)
    {
        string normalized = ToSnakeCase(name);
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "unknown", StringComparison.Ordinal))
        {
            normalized = $"field_{fieldNumber}";
        }
        return isDeveloper
            ? $"developer_{developerDataIndex ?? byte.MaxValue}_{fieldNumber}_{normalized}"
            : normalized;
    }

    public static string ToSnakeCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsLetterOrDigit(current))
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }
                continue;
            }

            bool isUpper = char.IsUpper(current);
            if (isUpper && builder.Length > 0 && builder[^1] != '_')
            {
                char previous = value[index - 1];
                bool nextIsLower = index + 1 < value.Length && char.IsLower(value[index + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || nextIsLower)
                {
                    builder.Append('_');
                }
            }
            builder.Append(char.ToLowerInvariant(current));
        }
        return builder.ToString().Trim('_');
    }
}

internal static class FitValueNormalizer
{
    private static readonly HashSet<string> UtcTimestampFields = new(StringComparer.Ordinal)
    {
        "timestamp", "start_time", "time_created"
    };
    private static readonly System.DateTime GarminEpoch =
        new(1989, 12, 31, 0, 0, 0, System.DateTimeKind.Utc);

    public static object? Normalize(
        object? rawValue,
        object? value,
        byte baseType,
        string fieldKey,
        out bool invalid)
    {
        invalid = IsInvalid(rawValue, value, baseType);
        if (invalid || value is null)
        {
            return null;
        }

        if (value is Dynastream.Fit.DateTime fitDateTime)
        {
            return FormatTimestamp(fitDateTime.GetTimeStamp(), fieldKey);
        }
        if (value is System.DateTime systemDateTime)
        {
            return systemDateTime.Kind == System.DateTimeKind.Unspecified
                ? systemDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                : systemDateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }
        if (value is byte[] bytes)
        {
            return Convert.ToHexString(bytes);
        }
        if ((UtcTimestampFields.Contains(fieldKey) ||
             string.Equals(fieldKey, "local_timestamp", StringComparison.Ordinal)) &&
            TryGetUnsignedInteger(value, out ulong timestamp))
        {
            return FormatTimestamp(timestamp, fieldKey);
        }
        if (value.GetType().IsEnum)
        {
            return value.ToString();
        }
        if (value is float single)
        {
            return float.IsFinite(single) ? single : null;
        }
        if (value is double number)
        {
            return double.IsFinite(number) ? number : null;
        }
        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or
            decimal)
        {
            return value;
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString();
    }

    private static string FormatTimestamp(ulong timestamp, string fieldKey)
    {
        if (timestamp > int.MaxValue)
        {
            throw new InvalidDataException(
                $"FIT timestamp {timestamp} in {fieldKey} exceeds supported range.");
        }
        System.DateTime result = GarminEpoch.AddSeconds((long)timestamp);
        return string.Equals(fieldKey, "local_timestamp", StringComparison.Ordinal)
            ? result.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            : result.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static bool IsInvalid(object? rawValue, object? value, byte baseType)
    {
        if (value is null)
        {
            return true;
        }
        if (rawValue is float rawSingle)
        {
            return !float.IsFinite(rawSingle);
        }
        if (rawValue is double rawDouble)
        {
            return !double.IsFinite(rawDouble);
        }
        if (!TryGetSignedBits(rawValue, out long rawBits))
        {
            return false;
        }

        try
        {
            return FitBaseType.IsNumericInvalid(rawBits, baseType);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetSignedBits(object? value, out long bits)
    {
        switch (value)
        {
            case byte item: bits = item; return true;
            case sbyte item: bits = item; return true;
            case short item: bits = item; return true;
            case ushort item: bits = item; return true;
            case int item: bits = item; return true;
            case uint item: bits = item; return true;
            case long item: bits = item; return true;
            case ulong item: bits = unchecked((long)item); return true;
            case Enum item: bits = Convert.ToInt64(item, CultureInfo.InvariantCulture); return true;
            default: bits = default; return false;
        }
    }

    private static bool TryGetUnsignedInteger(object value, out ulong number)
    {
        try
        {
            number = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception) when (value is not string)
        {
            number = default;
            return false;
        }
        catch (FormatException)
        {
            number = default;
            return false;
        }
    }
}

internal static class FitTypes
{
    public static string Name(byte type) => type switch
    {
        FitBaseType.Enum => "enum",
        FitBaseType.Sint8 => "sint8",
        FitBaseType.Uint8 => "uint8",
        FitBaseType.Sint16 => "sint16",
        FitBaseType.Uint16 => "uint16",
        FitBaseType.Sint32 => "sint32",
        FitBaseType.Uint32 => "uint32",
        FitBaseType.String => "string",
        FitBaseType.Float32 => "float32",
        FitBaseType.Float64 => "float64",
        FitBaseType.Uint8z => "uint8z",
        FitBaseType.Uint16z => "uint16z",
        FitBaseType.Uint32z => "uint32z",
        FitBaseType.Byte => "byte",
        FitBaseType.Sint64 => "sint64",
        FitBaseType.Uint64 => "uint64",
        FitBaseType.Uint64z => "uint64z",
        _ => $"unknown_{type}"
    };
}

public sealed record CanonicalFit(
    int SchemaVersion,
    CanonicalizerInfo Canonicalizer,
    SourceInfo Source,
    ValidationResult Validation,
    ExtractionCounts Counts,
    IReadOnlyList<MessageSchema> MessageSchemas,
    IReadOnlyDictionary<string, List<CanonicalMessage>> Messages,
    IReadOnlyList<CanonicalWarning> Warnings);

public sealed record CanonicalizerInfo(
    string Name,
    string Version,
    string Decoder,
    string DecoderVersion);

public sealed record SourceInfo(
    string GarminActivityId,
    string OriginalFileName,
    long SizeBytes,
    string Sha256);

public sealed record ValidationResult(
    FitHeader Header,
    bool IsFit,
    bool IntegrityValid,
    bool ReadSuccessful);

public sealed record FitHeader(
    int HeaderSizeBytes,
    byte ProtocolVersionRaw,
    int ProtocolMajor,
    int ProtocolMinor,
    ushort ProfileVersionRaw,
    uint DeclaredDataSizeBytes,
    bool HeaderCrcPresent,
    bool FileCrcPresent,
    long TrailingBytes);

public sealed record ExtractionCounts(
    int MessageTypeCount,
    int TotalMessageCount,
    int ActivityCount,
    int SessionCount,
    int LapCount,
    int EventCount,
    int TimeInZoneCount,
    int RecordCount,
    long FilteredInvalidValueCount,
    long DeveloperFieldValueCount);

public sealed record MessageSchema(
    string MessageKey,
    string OriginalName,
    ushort GlobalMessageNumber,
    int MessageOccurrences,
    IReadOnlyList<FieldSchema> Fields);

public sealed record FieldSchema(
    string Key,
    string OriginalName,
    byte FieldNumber,
    string Units,
    byte BaseTypeNumber,
    string BaseTypeName,
    string? ProfileType,
    bool IsDeveloperField,
    byte? DeveloperDataIndex,
    int MessageOccurrences,
    long ValidValueCount,
    long InvalidValueCount);

public sealed record CanonicalMessage(
    int MessageIndex,
    int Sequence,
    ushort GlobalMessageNumber,
    IReadOnlyDictionary<string, object?> Fields);

public sealed record CanonicalWarning(
    string Code,
    ushort GlobalMessageNumber,
    byte? FieldNumber,
    long Count,
    string Message);
