namespace MedFlow.Application.Services.Payments;

internal static class PostgresUniqueViolation
{
    public static bool Is(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var state = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            if (state == "23505")
                return true;
        }

        return false;
    }
}
