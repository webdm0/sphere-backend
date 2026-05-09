using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace SphereBackend.Data
{
    public static class BoardOrderGuardExtensions
    {
        private const int BoardOrderLockNamespace = 4101;
        private const int UserBoardOrderLockNamespace = 4102;
        private const int MaxOrderMutationAttempts = 3;

        private static readonly HashSet<string> OrderConstraintNames = new(StringComparer.Ordinal)
        {
            "IX_Columns_BoardId_Order_Active",
            "IX_Cards_ColumnId_Order_Active",
            "IX_BoardMembers_UserId_Order"
        };

        public static async Task<TResult> ExecuteWithBoardOrderGuardAsync<TResult>(
            this AppDbContext context,
            int boardId,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            if (!UsesPostgresOrderGuards(context))
            {
                return await operation(cancellationToken);
            }

            for (var attempt = 1; ; attempt++)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await context.AcquireBoardOrderLockAsync(boardId, cancellationToken);

                    var result = await operation(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (DbUpdateException ex) when (IsOrderUniquenessViolation(ex) && attempt < MaxOrderMutationAttempts)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        public static bool IsOrderUniquenessViolation(this DbUpdateException exception)
        {
            return exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: var constraintName
            }
            && constraintName is not null
            && OrderConstraintNames.Contains(constraintName);
        }

        public static Task<TResult> ExecuteWithUserBoardOrderGuardAsync<TResult>(
            this AppDbContext context,
            int userId,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            return context.ExecuteWithUserBoardOrderGuardAsync(new[] { userId }, operation, cancellationToken);
        }

        public static async Task<TResult> ExecuteWithUserBoardOrderGuardAsync<TResult>(
            this AppDbContext context,
            IEnumerable<int> userIds,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            var normalizedUserIds = userIds
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            if (normalizedUserIds.Length == 0)
                throw new ArgumentException("At least one user id is required.", nameof(userIds));

            if (!UsesPostgresOrderGuards(context))
            {
                return await operation(cancellationToken);
            }

            for (var attempt = 1; ; attempt++)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    foreach (var userId in normalizedUserIds)
                    {
                        await context.AcquireScopedOrderLockAsync(UserBoardOrderLockNamespace, userId, cancellationToken);
                    }

                    var result = await operation(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (DbUpdateException ex) when (IsOrderUniquenessViolation(ex) && attempt < MaxOrderMutationAttempts)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        private static async Task AcquireBoardOrderLockAsync(
            this AppDbContext context,
            int boardId,
            CancellationToken cancellationToken)
        {
            await context.AcquireScopedOrderLockAsync(BoardOrderLockNamespace, boardId, cancellationToken);
        }

        private static bool UsesPostgresOrderGuards(AppDbContext context)
        {
            return string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal);
        }

        private static async Task AcquireScopedOrderLockAsync(
            this AppDbContext context,
            int scope,
            int key,
            CancellationToken cancellationToken)
        {
            var currentTransaction = context.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Board order guard requires an active transaction.");

            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = currentTransaction.GetDbTransaction();
            command.CommandText = "SELECT pg_advisory_xact_lock(@scope, @key)";

            var scopeParameter = command.CreateParameter();
            scopeParameter.ParameterName = "@scope";
            scopeParameter.Value = scope;
            command.Parameters.Add(scopeParameter);

            var keyParameter = command.CreateParameter();
            keyParameter.ParameterName = "@key";
            keyParameter.Value = key;
            command.Parameters.Add(keyParameter);

            await command.ExecuteScalarAsync(cancellationToken);
        }
    }
}
