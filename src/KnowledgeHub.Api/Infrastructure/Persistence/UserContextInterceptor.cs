using System.Data.Common;
using KnowledgeHub.Api.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KnowledgeHub.Api.Infrastructure.Persistence;

/// <summary>
/// Передаёт в PostgreSQL пользователя и системный режим (<c>app.user_id</c>, <c>app.is_system</c>) для политик Row-Level Security.
/// <list type="bullet">
/// <item>При каждом открытии соединения значения ставятся всегда и оба сразу: соединение берётся из пула, и без этого значение
/// предыдущего запроса могло бы «просочиться» в следующий.</item>
/// <item>Перед каждой командой выставленное сверяется с текущим <see cref="ICurrentUser"/>: <see cref="SystemContext"/> может
/// переключиться, пока EF держит соединение (транзакция, явно открытое соединение). Тогда значения ставятся заново на том же
/// соединении — иначе сырой SQL, который фильтр EF не защищает, выполнился бы от имени прежнего пользователя.</item>
/// <item><c>set_config</c> внутри транзакции откатывается вместе с ней (и с <c>ROLLBACK TO SAVEPOINT</c>), поэтому после отката
/// выставленное считается неизвестным и следующая команда ставит значения заново.</item>
/// </list>
/// </summary>
public sealed class UserContextInterceptor(ICurrentUser currentUser)
    : DbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor
{
    private const string Sql = "SELECT set_config('app.user_id', @uid, false), set_config('app.is_system', @sys, false)";

    private sealed record Applied(DbConnection Connection, string UserId, string IsSystem);

    /// <summary>Что выставлено на открытом соединении; <c>null</c> — неизвестно (не открыто, закрыто или откатилось).</summary>
    private Applied? _applied;

    // ---- Соединение ----

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection, null);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, null, cancellationToken);

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) => Forget();

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        Forget();
        return Task.CompletedTask;
    }

    // ---- Команды: контекст мог смениться, пока соединение открыто ----

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        EnsureCurrent(command);
        return result;
    }

    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAsync(command, cancellationToken);
        return result;
    }

    public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        EnsureCurrent(command);
        return result;
    }

    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAsync(command, cancellationToken);
        return result;
    }

    public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        EnsureCurrent(command);
        return result;
    }

    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAsync(command, cancellationToken);
        return result;
    }

    // ---- Транзакции: откат отменяет и set_config ----

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Forget();

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget();
        return Task.CompletedTask;
    }

    public void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) => Forget();

    public Task RolledBackToSavepointAsync(
        DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget();
        return Task.CompletedTask;
    }

    // Неудачные commit/rollback оставляют состояние сервера неизвестным.
    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Forget();

    public Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget();
        return Task.CompletedTask;
    }

    // ---- Выставление ----

    private void EnsureCurrent(DbCommand command)
    {
        if (NeedsApply(command.Connection) is { } connection)
        {
            Apply(connection, command.Transaction);
        }
    }

    private async Task EnsureCurrentAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (NeedsApply(command.Connection) is { } connection)
        {
            await ApplyAsync(connection, command.Transaction, cancellationToken);
        }
    }

    private DbConnection? NeedsApply(DbConnection? connection)
    {
        if (connection is null)
        {
            return null;
        }

        var (userId, isSystem) = Current();
        return _applied is { } applied && applied.Connection == connection && applied.UserId == userId && applied.IsSystem == isSystem
            ? null
            : connection;
    }

    private void Apply(DbConnection connection, DbTransaction? transaction)
    {
        using var command = CreateCommand(connection, transaction, out var applied);
        command.ExecuteNonQuery();
        _applied = applied;
    }

    private async Task ApplyAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, out var applied);
        await command.ExecuteNonQueryAsync(cancellationToken);
        _applied = applied;
    }

    private void Forget() => _applied = null;

    private (string UserId, string IsSystem) Current() =>
        (currentUser.UserId?.ToString() ?? "", currentUser.IsSystem ? "on" : "off");

    private DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, out Applied applied)
    {
        var (userId, isSystem) = Current();
        applied = new Applied(connection, userId, isSystem);
        var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.Transaction = transaction;
        AddParameter(command, "uid", userId);
        AddParameter(command, "sys", isSystem);
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
