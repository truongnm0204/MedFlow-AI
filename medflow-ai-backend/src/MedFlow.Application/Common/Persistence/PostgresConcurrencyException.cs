using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Common.Persistence;

/// <summary>
/// Classifies PostgreSQL SQLSTATE codes and EF Core optimistic concurrency exceptions
/// without taking a direct compile-time dependency on Npgsql in MedFlow.Application (ADR-0059).
/// </summary>
public static class PostgresConcurrencyException
{
    public const string UniqueViolationSqlState = "23505";
    public const string ForeignKeyViolationSqlState = "23503";
    public const string CheckViolationSqlState = "23514";
    public const string SerializationFailureSqlState = "40001";
    public const string DeadlockDetectedSqlState = "40P01";
    public const string LockNotAvailableSqlState = "55P03";

    public static string? TryGetSqlState(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().GetProperty("SqlState")?.GetValue(current) is string state &&
                !string.IsNullOrWhiteSpace(state))
            {
                return state;
            }
        }

        return null;
    }

    public static string? TryGetConstraintName(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().GetProperty("ConstraintName")?.GetValue(current) is string constraint &&
                !string.IsNullOrWhiteSpace(constraint))
            {
                return constraint;
            }
        }

        return null;
    }

    public static bool IsUniqueViolation(Exception exception)
        => TryGetSqlState(exception) == UniqueViolationSqlState;

    public static bool IsDeadlockOrSerializationFailure(Exception exception)
        => TryGetSqlState(exception) is DeadlockDetectedSqlState or SerializationFailureSqlState;

    public static bool IsTransientConcurrencyConflict(Exception exception)
        => exception is DbUpdateConcurrencyException || IsDeadlockOrSerializationFailure(exception);
}
