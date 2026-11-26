using MedFlow.Application.Common.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedFlow.IntegrationTests.Persistence;

public sealed class Migration023AndRowLockIntegrationTests
{
    private static readonly string AdminConnectionString = ResolveAdminConnectionString();
    private static readonly string RepoRoot = ResolveRepoRoot();

    [Fact]
    public async Task Migration023_FromScratchWithSeed_IsIdempotent_PassesPreflight_AndPreservesLegacyPayments()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyAllMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive023: true);

        // 1. Read-only preflight audit must report 0 violations across all checks
        var preflightSql = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "database", "scripts", "preflight_023_audit.sql"));
        await using (var conn = new NpgsqlConnection(tempDb.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(preflightSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var checkCount = 0;
            while (await reader.ReadAsync())
            {
                checkCount++;
                var checkName = reader.GetString(0);
                var violations = reader.GetInt64(1);
                Assert.True(violations == 0, $"Preflight check '{checkName}' reported {violations} violations.");
            }

            Assert.True(checkCount >= 12);
        }

        // 2. Apply Migration 023 twice to prove idempotency
        var migration023 = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "database", "migrations", "023_business_integrity_constraints_and_indexes.sql"));
        await ExecuteSqlScriptAsync(tempDb.ConnectionString, migration023);
        await ExecuteSqlScriptAsync(tempDb.ConnectionString, migration023);

        // 3. Verify historical payments were NOT guessed/backfilled to visit_charges
        await using (var conn = new NpgsqlConnection(tempDb.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM payments WHERE payment_scope = 'LEGACY_VISIT' AND visit_charge_id IS NULL",
                conn);
            var legacyCount = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(10L, legacyCount);

            await using var diagCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM visit_charges WHERE diagnostic_order_id IS NOT NULL AND charge_type = 'DIAGNOSTIC'",
                conn);
            var diagChargeCount = (long)(await diagCmd.ExecuteScalarAsync())!;
            Assert.True(diagChargeCount > 0);
        }
    }

    [Fact]
    public async Task Migration023_DirtyNegativeStock_AbortsWithException_AndNeverClampsStock()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyAllMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive023: true);

        await ExecuteSqlScriptAsync(
            tempDb.ConnectionString,
            "UPDATE medicine_batches SET quantity_on_hand = -20 WHERE id = 1;");

        var migration023 = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "database", "migrations", "023_business_integrity_constraints_and_indexes.sql"));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlScriptAsync(tempDb.ConnectionString, migration023));
        Assert.Contains("medicine_batches has null or negative quantity_on_hand", ex.MessageText);

        // Prove rollback preserved -20 without silent clamping
        await using var conn = new NpgsqlConnection(tempDb.ConnectionString);
        await conn.OpenAsync();
        await using var verifyCmd = new NpgsqlCommand(
            "SELECT quantity_on_hand FROM medicine_batches WHERE id = 1",
            conn);
        var actualStock = (decimal)(await verifyCmd.ExecuteScalarAsync())!;
        Assert.Equal(-20m, actualStock);
    }

    [Fact]
    public async Task Migration023_DuplicateDraftPrescriptions_AbortsAndPreservesDuplicateRows()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyAllMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive023: true);

        await ExecuteSqlScriptAsync(
            tempDb.ConnectionString,
            """
            INSERT INTO prescriptions (prescription_code, visit_id, prescribing_doctor_id, fulfillment_choice, status, created_at)
            VALUES ('RX-DUP-1', 1, 1, 'HOSPITAL', 'DRAFT', '2026-11-25 09:00:00'),
                   ('RX-DUP-2', 1, 1, 'HOSPITAL', 'DRAFT', '2026-11-25 09:01:00');
            """);

        var migration023 = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot, "database", "migrations", "023_business_integrity_constraints_and_indexes.sql"));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlScriptAsync(tempDb.ConnectionString, migration023));
        Assert.Contains("duplicate DRAFT rows per visit", ex.MessageText);

        await using var conn = new NpgsqlConnection(tempDb.ConnectionString);
        await conn.OpenAsync();
        await using var countCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM prescriptions WHERE visit_id = 1 AND status = 'DRAFT'",
            conn);
        Assert.Equal(2L, (long)(await countCmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Migration023_CompositeForeignKeys_PaymentScope_AndPerChargeIndexes_AreEnforced()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyAllMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive023: false);

        await using var conn = new NpgsqlConnection(tempDb.ConnectionString);
        await conn.OpenAsync();

        // 1. Raw SQL INSERT into payments without visit_charge_id uses default 'CHARGE' and MUST be rejected (23514)
        var unlinkedChargeEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO payments (visit_id, amount, status) VALUES (1, 100000, 'PENDING')",
                conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", unlinkedChargeEx.SqlState);
        Assert.Equal("chk_payments_scope_charge_consistency", unlinkedChargeEx.ConstraintName);

        // 2. Cross-visit Payment (Visit 1 referencing VisitCharge 3 which belongs to Visit 2) -> rejected by fk_payments_charge_visit (23503)
        var crossVisitPaymentEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO payments (visit_id, visit_charge_id, payment_scope, amount, status) VALUES (1, 3, 'CHARGE', 220000, 'PENDING')",
                conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23503", crossVisitPaymentEx.SqlState);
        Assert.Equal("fk_payments_charge_visit", crossVisitPaymentEx.ConstraintName);

        // 3. Cross-visit VisitCharge (Visit 1 referencing a fresh DiagnosticOrder belonging to Visit 2) -> rejected by fk_visit_charges_diagnostic_order_visit (23503)
        long visit2FreshOrderId;
        await using (var seedOrderCmd = new NpgsqlCommand(
            "INSERT INTO diagnostic_orders (order_code, visit_id, ordering_doctor_id, service_id, status, ordered_at) VALUES ('DIA-P01-V2', 2, 2, 6, 'ORDERED', '2026-11-25 10:00:00') RETURNING id",
            conn))
        {
            visit2FreshOrderId = (long)(await seedOrderCmd.ExecuteScalarAsync())!;
        }

        var crossVisitChargeEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                $"INSERT INTO visit_charges (visit_id, service_id, diagnostic_order_id, charge_type, unit_price, payment_requirement) VALUES (1, 6, {visit2FreshOrderId}, 'DIAGNOSTIC', 180000, 'REQUIRED')",
                conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23503", crossVisitChargeEx.SqlState);
        Assert.Equal("fk_visit_charges_diagnostic_order_visit", crossVisitChargeEx.ConstraintName);

        // 4. chk_visit_charges_type_consistency rejects EXAMINATION with diagnostic_order_id != null
        var badTypeEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO visit_charges (visit_id, service_id, diagnostic_order_id, charge_type, unit_price, payment_requirement) VALUES (1, 5, 1, 'EXAMINATION', 90000, 'REQUIRED')",
                conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", badTypeEx.SqlState);
        Assert.Equal("chk_visit_charges_type_consistency", badTypeEx.ConstraintName);

        // 5. Two concurrent PENDING QRs on TWO DIFFERENT charges of Visit 1 succeed,
        //    while duplicate PENDING or duplicate PAID on the SAME charge fails (23505)
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO payments (payment_code, visit_id, visit_charge_id, payment_scope, amount, status, created_at)
            VALUES ('MF9001', 1, 1, 'CHARGE', 200000, 'PENDING', '2026-11-25 10:00:00'),
                   ('MF9002', 1, 2, 'CHARGE', 90000, 'PENDING', '2026-11-25 10:00:00');
            """,
            conn))
        {
            Assert.Equal(2, await cmd.ExecuteNonQueryAsync());
        }

        var dupPendingEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO payments (payment_code, visit_id, visit_charge_id, payment_scope, amount, status) VALUES ('MF9003', 1, 1, 'CHARGE', 200000, 'PENDING')",
                conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23505", dupPendingEx.SqlState);
        Assert.Equal("ux_payments_one_pending_per_charge", dupPendingEx.ConstraintName);

        // Expire MF9001 -> new PENDING QR MF9004 on charge 1 succeeds, then PAID locks out second PAID
        await using (var expireCmd = new NpgsqlCommand(
            """
            UPDATE payments SET status = 'EXPIRED' WHERE payment_code = 'MF9001';
            INSERT INTO payments (payment_code, visit_id, visit_charge_id, payment_scope, amount, status)
            VALUES ('MF9004', 1, 1, 'CHARGE', 200000, 'PAID');
            """,
            conn))
        {
            await expireCmd.ExecuteNonQueryAsync();
        }

        var dupPaidEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO payments (payment_code, visit_id, visit_charge_id, payment_scope, amount, status) VALUES ('MF9005', 1, 1, 'CHARGE', 200000, 'PAID')",
                conn);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23505", dupPaidEx.SqlState);
        Assert.Equal("ux_payments_one_paid_per_charge", dupPaidEx.ConstraintName);
    }

    [Fact]
    public async Task RowLocks_HospitalRooms_AdvisoryLock_AndAppointmentXmin_ContendDeterministicallyAcrossConnections()
    {
        await using var tempDb = await TempPostgresDatabase.CreateAsync(AdminConnectionString);
        await ApplyAllMigrationsAsync(tempDb.ConnectionString, includeSeed: true, upToExclusive023: false);

        await using var dbA = CreateDbContext(tempDb.ConnectionString);
        await using var dbB = CreateDbContext(tempDb.ConnectionString);

        // 1. Calling FOR UPDATE outside an active transaction MUST fail fast
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbA.TryLockRoomForUpdateAsync(1));

        // 2. Deterministic SHA-256 advisory lock key is stable and non-zero
        var key1 = MedFlowDbContext.ComputeDeterministicAdvisoryLockKey("queue-unrouted", "1:2026-11-25");
        var key2 = MedFlowDbContext.ComputeDeterministicAdvisoryLockKey("queue-unrouted", "1:2026-11-25");
        var keyOther = MedFlowDbContext.ComputeDeterministicAdvisoryLockKey("queue-unrouted", "1:2026-11-26");
        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, keyOther);

        // 3. Real two-connection barrier test on hospital_rooms (physical table) + missing-row false return
        await using (var txA = await dbA.BeginTransactionAsync())
        {
            Assert.False(await dbA.TryLockRoomForUpdateAsync(999_999_999));
            Assert.True(await dbA.TryLockRoomsForUpdateAsync([2, 1]));

            await using (var txB = await dbB.BeginTransactionAsync())
            {
                await dbB.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '150ms'");
                var lockTimeoutEx = await Assert.ThrowsAnyAsync<Exception>(() =>
                    dbB.TryLockRoomForUpdateAsync(1));
                Assert.Equal("55P03", PostgresConcurrencyException.TryGetSqlState(lockTimeoutEx));
                await txB.RollbackAsync();
            }

            // Advisory lock contention across independent connections
            Assert.True(await dbA.TryLockUnroutedQueueAdvisoryAsync(1, new DateOnly(2026, 11, 25)));
            await using (var txBAdv = await dbB.BeginTransactionAsync())
            {
                await dbB.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '150ms'");
                var advTimeoutEx = await Assert.ThrowsAnyAsync<Exception>(() =>
                    dbB.TryLockUnroutedQueueAdvisoryAsync(1, new DateOnly(2026, 11, 25)));
                Assert.Equal("55P03", PostgresConcurrencyException.TryGetSqlState(advTimeoutEx));
                await txBAdv.RollbackAsync();
            }

            await txA.CommitAsync();
        }

        // After txA commits, dbB immediately acquires both locks
        await using (var txBAfter = await dbB.BeginTransactionAsync())
        {
            Assert.True(await dbB.TryLockRoomForUpdateAsync(1));
            Assert.True(await dbB.TryLockUnroutedQueueAdvisoryAsync(1, new DateOnly(2026, 11, 25)));
            await txBAfter.CommitAsync();
        }

        // 4. Appointment PostgreSQL xmin optimistic concurrency token detects lost update
        await using var xminDb1 = CreateDbContext(tempDb.ConnectionString);
        await using var xminDb2 = CreateDbContext(tempDb.ConnectionString);

        var appt1 = await xminDb1.Appointments.FirstAsync(a => a.Id == 1);
        var appt2 = await xminDb2.Appointments.FirstAsync(a => a.Id == 1);

        appt1.ReceptionNote = "Checked-in by Reception";
        await xminDb1.SaveChangesAsync();

        appt2.Status = "CANCELLED";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => xminDb2.SaveChangesAsync());
    }

    private static MedFlowDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MedFlowDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new MedFlowDbContext(options);
    }

    private static async Task ApplyAllMigrationsAsync(
        string connectionString,
        bool includeSeed,
        bool upToExclusive023)
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
                if (upToExclusive023 && string.CompareOrdinal(name, "023_") >= 0)
                    return false;
                return true;
            })
            .ToList();

        foreach (var file in files)
        {
            var sql = await File.ReadAllTextAsync(file);
            await ExecuteSqlScriptAsync(connectionString, sql);
        }
    }

    private static async Task ExecuteSqlScriptAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 60 };
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
            var dbName = $"medflow_p01_{Guid.NewGuid():N}"[..28];
            await using (var admin = new NpgsqlConnection(adminConnectionString))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
                await cmd.ExecuteNonQueryAsync();
            }

            var targetBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = dbName,
                Pooling = true,
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
