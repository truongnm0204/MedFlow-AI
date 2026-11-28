using Npgsql;

namespace MedFlow.IntegrationTests.Persistence;

public sealed class MedicinePricesMigrationTests
{
    private static readonly string AdminConnectionString = ResolveAdminConnectionString();
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public async Task Migration034_FreshDatabase_IsIdempotent_AndEnforcesConstraints()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyMigrationsThrough033Async(tempDb.ConnectionString, includeSeed: true);

        var migration034 = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "database", "migrations", "034_medicine_prices.sql"));

        await ExecuteSqlAsync(tempDb.ConnectionString, migration034);
        await ExecuteSqlAsync(tempDb.ConnectionString, migration034); // idempotent re-run

        // Table + expected columns + effective-lookup index exist exactly once.
        Assert.Equal(1L, await ScalarLongAsync(tempDb.ConnectionString,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'medicine_prices'"));

        Assert.Equal(11L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_name = 'medicine_prices'
            """));
        Assert.Equal(1L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE tablename = 'medicine_prices' AND indexname = 'ix_medicine_prices_effective_lookup'
            """));

        // Valid row accepted (FKs to seeded hospitals/medicines).
        await ExecuteSqlAsync(tempDb.ConnectionString,
            """
            INSERT INTO medicine_prices (hospital_id, medicine_id, price_amount, effective_from)
            SELECT m.hospital_id, m.id, 1500, timestamp '2026-01-01 00:00:00'
            FROM medicines m
            WHERE m.hospital_id IS NOT NULL
            ORDER BY m.id
            LIMIT 1;
            """);

        var negative = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString,
                """
                INSERT INTO medicine_prices (hospital_id, medicine_id, price_amount, effective_from)
                SELECT m.hospital_id, m.id, -1, timestamp '2026-02-01 00:00:00'
                FROM medicines m
                WHERE m.hospital_id IS NOT NULL
                ORDER BY m.id
                LIMIT 1;
                """));
        Assert.Equal("23514", negative.SqlState);
        Assert.Equal("chk_medicine_prices_price_amount_nonnegative", negative.ConstraintName);

        var invalidRange = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString,
                """
                INSERT INTO medicine_prices (hospital_id, medicine_id, price_amount, effective_from, effective_to)
                SELECT m.hospital_id, m.id, 100, timestamp '2026-03-10 00:00:00', timestamp '2026-03-01 00:00:00'
                FROM medicines m
                WHERE m.hospital_id IS NOT NULL
                ORDER BY m.id
                LIMIT 1;
                """));
        Assert.Equal("23514", invalidRange.SqlState);
        Assert.Equal("chk_medicine_prices_effective_range", invalidRange.ConstraintName);

        var badMedicine = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString,
                """
                INSERT INTO medicine_prices (hospital_id, medicine_id, price_amount, effective_from)
                VALUES (1, 999999999, 100, timestamp '2026-03-01 00:00:00');
                """));
        Assert.Equal("23503", badMedicine.SqlState);

        // Preflight audit reports zero violations.
        Assert.Equal(0L, await ScalarLongAsync(tempDb.ConnectionString,
            "SELECT COUNT(*) FROM medicine_prices WHERE price_amount < 0"));
        Assert.Equal(0L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM medicine_prices
            WHERE effective_to IS NOT NULL AND effective_to <= effective_from
            """));
        Assert.Equal(6L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE table_name = 'medicine_prices'
              AND constraint_name IN (
                'medicine_prices_pkey',
                'medicine_prices_hospital_id_fkey',
                'medicine_prices_medicine_id_fkey',
                'medicine_prices_created_by_user_id_fkey',
                'chk_medicine_prices_price_amount_nonnegative',
                'chk_medicine_prices_effective_range')
            """));
    }

    private static async Task ApplyMigrationsThrough033Async(string connectionString, bool includeSeed)
    {
        var migrationsDir = Path.Combine(RepoRoot, "database", "migrations");
        var files = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("001_", StringComparison.Ordinal))
                    return false;
                if (!includeSeed && name.StartsWith("003_", StringComparison.Ordinal))
                    return false;
                return string.CompareOrdinal(name, "034_") < 0;
            })
            .ToList();

        foreach (var file in files)
        {
            await ExecuteSqlAsync(connectionString, await File.ReadAllTextAsync(file));
        }
    }

    private static async Task<long> ScalarLongAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    private static string ResolveAdminConnectionString()
    {
        var baseConn = Environment.GetEnvironmentVariable("MEDFLOW_TEST_DB")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=medflow_test;Username=postgres;Password=medflow_dev";
        var builder = new NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = "postgres",
            Pooling = false,
        };
        return builder.ConnectionString;
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing database/migrations.");
    }

    private sealed class TempPostgresDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        public string DatabaseName { get; }
        public string ConnectionString { get; }

        private TempPostgresDatabase(string adminConnectionString, string databaseName, string connectionString)
        {
            _adminConnectionString = adminConnectionString;
            DatabaseName = databaseName;
            ConnectionString = connectionString;
        }

        public static async Task<TempPostgresDatabase> CreateAsync(string adminConnectionString)
        {
            var dbName = $"medflow_s3a865_{Guid.NewGuid():N}"[..28];
            await using (var admin = new NpgsqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = dbName,
                Pooling = false,
            };
            return new TempPostgresDatabase(adminConnectionString, dbName, builder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
