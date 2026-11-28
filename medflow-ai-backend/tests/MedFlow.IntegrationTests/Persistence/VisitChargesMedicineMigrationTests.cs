using Npgsql;

namespace MedFlow.IntegrationTests.Persistence;

public sealed class VisitChargesMedicineMigrationTests
{
    private static readonly string AdminConnectionString = ResolveAdminConnectionString();
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public async Task Migration035_FreshDatabase_IsIdempotent_AndEnforcesMedicineChargeShape()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyMigrationsThrough034Async(tempDb.ConnectionString, includeSeed: true);

        var migration035 = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "database", "migrations", "035_visit_charges_medicine.sql"));

        await ExecuteSqlAsync(tempDb.ConnectionString, migration035);
        await ExecuteSqlAsync(tempDb.ConnectionString, migration035); // idempotent re-run

        // 5 medicine link columns exactly once.
        Assert.Equal(5L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_name = 'visit_charges'
              AND column_name IN (
                'prescription_id',
                'prescription_item_id',
                'medicine_id',
                'quantity',
                'medicine_unit_price')
            """));

        // Indexes + the unique per-prescription-item guard.
        Assert.Equal(1L, await ScalarLongAsync(tempDb.ConnectionString,
            "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'visit_charges' AND indexname = 'uq_visit_charges_prescription_item'"));
        Assert.Equal(1L, await ScalarLongAsync(tempDb.ConnectionString,
            "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'visit_charges' AND indexname = 'ix_visit_charges_visit_type'"));

        // FK + check constraints.
        Assert.Equal(3L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM pg_constraint
            WHERE conrelid = '"visit_charges"'::regclass
              AND conname IN ('fk_visit_charges_prescription', 'fk_visit_charges_prescription_item', 'fk_visit_charges_medicine')
            """));
        Assert.Equal(2L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM pg_constraint
            WHERE conrelid = '"visit_charges"'::regclass
              AND conname IN ('chk_visit_charges_medicine_quantity', 'chk_visit_charges_medicine_unit_price')
            """));

        // A prescription line can back at most one charge.
        var visitId = await ScalarLongAsync(tempDb.ConnectionString,
            """
            INSERT INTO visits (visit_code, patient_id, status, checked_in_at)
            VALUES ('VC-MIG035-1', 1, 'CHECKED_IN', timestamp '2026-11-29 09:00:00')
            RETURNING id;
            """);
        var prescriptionId = await ScalarLongAsync(tempDb.ConnectionString,
            $"""
            INSERT INTO prescriptions (visit_id, status, created_at)
            VALUES ({visitId}, 'ISSUED', timestamp '2026-11-29 09:00:00')
            RETURNING id;
            """);
        var itemId = await ScalarLongAsync(tempDb.ConnectionString,
            $"""
            INSERT INTO prescription_items (prescription_id, medicine_id, prescribed_quantity)
            VALUES ({prescriptionId}, 1, 2)
            RETURNING id;
            """);

        var insertCharge = $"""
            INSERT INTO visit_charges
              (visit_id, prescription_id, prescription_item_id, medicine_id, charge_type, quantity, medicine_unit_price, unit_price, payment_requirement, status, created_at)
            VALUES
              ({visitId}, {prescriptionId}, {itemId}, 1, 'MEDICINE', 2, 12500, 25000, 'REQUIRED', 'ACTIVE', timestamp '2026-11-29 09:00:00');
            """;

        await ExecuteSqlAsync(tempDb.ConnectionString, insertCharge);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString, insertCharge));
        Assert.Equal("23505", duplicate.SqlState);

        var invalidQuantity = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(tempDb.ConnectionString,
                $"""
                INSERT INTO visit_charges (visit_id, charge_type, quantity, payment_requirement, status, created_at)
                VALUES ({visitId}, 'MEDICINE', 0, 'REQUIRED', 'ACTIVE', timestamp '2026-11-29 09:00:00');
                """));
        Assert.Equal("23514", invalidQuantity.SqlState);
        Assert.Equal("chk_visit_charges_medicine_quantity", invalidQuantity.ConstraintName);

        // Dev/test seed: pharmacy staff can collect medicine money at the counter.
        Assert.Equal(1L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM role_permissions rp
            JOIN roles r ON r.id = rp.role_id
            JOIN permissions p ON p.id = rp.permission_id
            WHERE r.code = 'PHARMACY_STAFF' AND p.code = 'PAYMENT_COLLECT'
            """));
    }

    private static async Task ApplyMigrationsThrough034Async(string connectionString, bool includeSeed)
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
                return string.CompareOrdinal(name, "035_") < 0;
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
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
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
            var dbName = $"medflow_s3b866_{Guid.NewGuid():N}"[..28];
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
