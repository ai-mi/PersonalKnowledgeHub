using System.Data.Common;
using KnowledgeHub.Api.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

/// <summary>
/// При каждом открытии соединения передаёт в PostgreSQL пользователя и системный режим (<c>app.user_id</c>, <c>app.is_system</c>)
/// для политик Row-Level Security. Значения ставятся всегда и оба сразу: соединение берётся из пула, и без этого
/// значение предыдущего запроса могло бы «просочиться» в следующий.
/// </summary>
public sealed class UserContextConnectionInterceptor(ICurrentUser currentUser) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('app.user_id', @uid, false), set_config('app.is_system', @sys, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        AddParameter(command, "uid", currentUser.UserId?.ToString() ?? "");
        AddParameter(command, "sys", currentUser.IsSystem ? "on" : "off");
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
