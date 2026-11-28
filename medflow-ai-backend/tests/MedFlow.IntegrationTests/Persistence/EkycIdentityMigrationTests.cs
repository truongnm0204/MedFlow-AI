using Npgsql;

namespace MedFlow.IntegrationTests.Persistence;

/// <summary>
/// Migration 029 (edited in place — owner scope) + 032 (patients identity status) against real
/// PostgreSQL: fresh apply, upgrade of a populated pre-eKYC database, idempotent re-apply, and
/// the owner-scope constraints (mode/purpose/performed_by, VERIFIED-without-reason).
/// </summary>
public sealed class EkycIdentityMigrationTests
{
    private static readonly string AdminConnectionString = ResolveAdminConnectionString();
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public async Task FreshDatabase_AppliesEkycMigrations_AndPreflightsClean()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive: "029");
        await ApplyEkycMigrationsAsync(tempDb.ConnectionString);

        // Columns/constraints from the owner-scope 029.
        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "ekyc_verification_sessions", "verification_mode"));
        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "ekyc_verification_sessions", "purpose"));
        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "ekyc_verification_sessions", "performed_by"));
        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "ekyc_verification_sessions", "declared_identification_hmac"));
        Assert.False(await ColumnExistsAsync(tempDb.ConnectionString, "ekyc_verification_sessions", "verification_method"));
        Assert.False(await ColumnExistsAsync(tempDb.ConnectionString, "ekyc_verification_sessions", "verified_by"));

        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "patients", "identity_status"));
        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "patients", "unverified_reason"));
        Assert.True(await ColumnExistsAsync(tempDb.ConnectionString, "patients", "identity_note"));

        // Preflights report zero violations.
        await AssertPreflightZeroAsync(tempDb.ConnectionString, "preflight_029_audit.sql");
        await AssertPreflightZeroAsync(tempDb.ConnectionString, "preflight_032_audit.sql");
        await AssertPreflightZeroAsync(tempDb.ConnectionString, "preflight_033_audit.sql");

        // Migration 033: seeded RECEPTION role holds PATIENT_IDENTITY_VERIFY exactly once.
        Assert.Equal(1L, await ReceptionGrantCountAsync(tempDb.ConnectionString));

        // All seeded pre-eKYC patients are UNVERIFIED by definition.
        await using var conn = new NpgsqlConnection(tempDb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM patients WHERE identity_status <> 'UNVERIFIED'", conn);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PopulatedDatabase_UpgradeKeepsData_AndEnforcesOwnerScopeConstraints()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive: "029");

        // Apply the edited 029/030/031/032 over populated tables.
        await ApplyEkycMigrationsAsync(tempDb.ConnectionString);

        await using var conn = new NpgsqlConnection(tempDb.ConnectionString);
        await conn.OpenAsync();

        // SELF_SERVICE session: performed_by must be NULL.
        await using (var ok = new NpgsqlCommand(
            """
            INSERT INTO ekyc_verification_sessions
              (status, verification_mode, purpose, expires_at, created_at, updated_at)
            VALUES ('INITIATED', 'SELF_SERVICE', 'ACCOUNT_ACTIVATION', now(), now(), now())
            """, conn))
        {
            Assert.Equal(1, await ok.ExecuteNonQueryAsync());
        }

        // RECEPTION_ASSISTED without performed_by → rejected.
        var missingVerifier = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(conn,
            """
            INSERT INTO ekyc_verification_sessions
              (status, verification_mode, purpose, expires_at, created_at, updated_at)
            VALUES ('INITIATED', 'RECEPTION_ASSISTED', 'PATIENT_PROFILE', now(), now(), now())
            """));
        Assert.Equal("23514", missingVerifier.SqlState);
        Assert.Equal("chk_ekyc_performed_by_consistency", missingVerifier.ConstraintName);

        // performed_by == subject user_id → rejected (no self-verification).
        long userId;
        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (email, password_hash, auth_provider, status, email_verified_at)
            VALUES (concat('ekyc-mig-', gen_random_uuid(), '@test.local'), 'x', 'LOCAL', 'ACTIVE', now())
            RETURNING id
            """, conn))
        {
            userId = (long)(await userCmd.ExecuteScalarAsync())!;
        }

        var selfVerify = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(conn,
            $"""
            INSERT INTO ekyc_verification_sessions
              (user_id, status, verification_mode, purpose, performed_by, expires_at, created_at, updated_at)
            VALUES ({userId}, 'INITIATED', 'RECEPTION_ASSISTED', 'PATIENT_PROFILE', {userId}, now(), now(), now())
            """));
        Assert.Equal("23514", selfVerify.SqlState);
        Assert.Equal("chk_ekyc_performed_by_consistency", selfVerify.ConstraintName);

        // patients: VERIFIED cannot carry an unverified reason.
        var verifiedWithReason = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(conn,
            """
            INSERT INTO patients (patient_code, full_name, identity_status, unverified_reason)
            VALUES ('BN-MIG-1', 'Migrated Patient', 'VERIFIED', 'CHILD')
            """));
        Assert.Equal("23514", verifiedWithReason.SqlState);
        Assert.Equal("chk_patients_unverified_reason", verifiedWithReason.ConstraintName);

        // UNVERIFIED with an owner-approved reason is allowed.
        await ExecuteAsync(conn,
            """
            INSERT INTO patients (patient_code, full_name, identity_status, unverified_reason)
            VALUES ('BN-MIG-2', 'Child Patient', 'UNVERIFIED', 'CHILD')
            """);
    }

    [Fact]
    public async Task EkycMigrations_ReapplyTwice_AreIdempotent()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive: "029");

        for (var i = 0; i < 2; i++)
        {
            await ApplyEkycMigrationsAsync(tempDb.ConnectionString);
        }

        await using var conn = new NpgsqlConnection(tempDb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_constraint
            WHERE conrelid = 'ekyc_verification_sessions'::regclass
              AND conname IN ('chk_ekyc_verification_mode','chk_ekyc_purpose','chk_ekyc_performed_by_consistency')
            """, conn);
        Assert.Equal(3L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Migration033_GrantsReceptionPermission_OnPopulatedDatabase_AndIsIdempotent()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive: "033");

        // Pre-033 baseline: the permission exists (018) and is granted to admins only.
        Assert.Equal(1L, await ScalarLongAsync(tempDb.ConnectionString,
            "SELECT COUNT(*) FROM roles WHERE code = 'RECEPTION'"));
        Assert.Equal(0L, await ScalarLongAsync(tempDb.ConnectionString,
            "SELECT COUNT(*) FROM roles WHERE code = 'RECEPTIONIST'"));
        Assert.Equal(0L, await ReceptionGrantCountAsync(tempDb.ConnectionString));
        Assert.Equal(2L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM role_permissions rp
            JOIN roles r ON r.id = rp.role_id
            JOIN permissions p ON p.id = rp.permission_id
            WHERE p.code = 'PATIENT_IDENTITY_VERIFY' AND r.code IN ('HOSPITAL_ADMIN','SYSTEM_ADMIN')
            """));

        for (var i = 0; i < 2; i++)
        {
            await ExecuteScriptAsync(tempDb.ConnectionString,
                await File.ReadAllTextAsync(Path.Combine(RepoRoot, "database", "migrations",
                    "033_reception_patient_identity_verify_grant.sql")));
        }

        Assert.Equal(1L, await ReceptionGrantCountAsync(tempDb.ConnectionString));

        // Admin grants untouched; no duplicate rows.
        Assert.Equal(2L, await ScalarLongAsync(tempDb.ConnectionString,
            """
            SELECT COUNT(*)
            FROM role_permissions rp
            JOIN roles r ON r.id = rp.role_id
            JOIN permissions p ON p.id = rp.permission_id
            WHERE p.code = 'PATIENT_IDENTITY_VERIFY' AND r.code IN ('HOSPITAL_ADMIN','SYSTEM_ADMIN')
            """));
        await AssertPreflightZeroAsync(tempDb.ConnectionString, "preflight_033_audit.sql");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static async Task<long> ReceptionGrantCountAsync(string connectionString)
        => await ScalarLongAsync(connectionString,
            """
            SELECT COUNT(*)
            FROM role_permissions rp
            JOIN roles r ON r.id = rp.role_id
            JOIN permissions p ON p.id = rp.permission_id
            WHERE r.code = 'RECEPTION' AND p.code = 'PATIENT_IDENTITY_VERIFY'
            """);

    private static async Task<long> ScalarLongAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task ApplyEkycMigrationsAsync(string connectionString)
    {
        foreach (var file in new[]
                 {
                     "029_ekyc_verification_sessions.sql",
                     "030_ekyc_proxy_call_logs.sql",
                     "031_ekyc_signed_evidence_capture.sql",
                     "032_patients_identity_status.sql",
                     "033_reception_patient_identity_verify_grant.sql",
                 })
        {
            await ExecuteScriptAsync(connectionString,
                await File.ReadAllTextAsync(Path.Combine(RepoRoot, "database", "migrations", file)));
        }
    }

    private static async Task AssertPreflightZeroAsync(string connectionString, string fileName)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "database", "scripts", fileName));
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return; // NOTICE-only preflight
        }

        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetFieldType(i) == typeof(long)
                && !reader.GetName(i).EndsWith("_present", StringComparison.Ordinal)
                && !reader.GetName(i).StartsWith("verified_", StringComparison.Ordinal)
                && !reader.GetName(i).StartsWith("unverified_", StringComparison.Ordinal)
                && !reader.GetName(i).StartsWith("legacy_", StringComparison.Ordinal))
            {
                Assert.Equal(0L, reader.GetInt64(i));
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(string connectionString, string table, string column)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @t AND column_name = @c", conn);
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    private static async Task ApplyMigrationsAsync(string connectionString, bool includeSeed, string upToExclusive)
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
                return string.CompareOrdinal(name, upToExclusive) < 0;
            })
            .ToList();

        foreach (var file in files)
        {
            await ExecuteScriptAsync(connectionString, await File.ReadAllTextAsync(file));
        }
    }

    private static async Task ExecuteScriptAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task ExecuteAsync(string connectionString, string sql)
        => ExecuteScriptAsync(connectionString, sql);

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
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
            var dbName = $"medflow_ekyc5_{Guid.NewGuid():N}"[..28];
            await using (var admin = new NpgsqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
                await cmd.ExecuteNonQueryAsync();
            }

            var targetBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = dbName,
                Pooling = false,
            };
            return new TempPostgresDatabase(adminConnectionString, dbName, targetBuilder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using (var terminate = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}' AND pid <> pg_backend_pid();",
                admin))
            {
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
