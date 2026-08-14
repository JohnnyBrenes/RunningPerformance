using RunningPerformance.Infrastructure.Database;
using NpgsqlTypes;

namespace RunningPerformance.Api.Features;

internal static class AuditWriter
{
    public static async Task WriteAsync(
        OwnerDbSession session,
        Guid ownerId,
        string action,
        string entityType,
        Guid? entityId,
        Guid correlationId,
        string[] changedFields,
        CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = """
            insert into app.audit_events (
              owner_id,
              actor_id,
              actor_type,
              action,
              entity_type,
              entity_id,
              correlation_id,
              changed_fields)
            values (
              @owner_id,
              @owner_id,
              'athlete',
              @action,
              @entity_type,
              @entity_id,
              @correlation_id,
              @changed_fields);
            """;
        command.Parameters.AddWithValue("owner_id", ownerId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.Add("entity_id", NpgsqlDbType.Uuid).Value =
            entityId.HasValue ? entityId.Value : DBNull.Value;
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("changed_fields", changedFields);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
