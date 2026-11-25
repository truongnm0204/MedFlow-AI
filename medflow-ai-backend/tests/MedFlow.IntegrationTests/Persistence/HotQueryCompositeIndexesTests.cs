using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace MedFlow.IntegrationTests.Persistence;

public sealed class HotQueryCompositeIndexesTests : AuthTestBase
{
    private const string MigrationFileName = "021_hot_query_composite_indexes.sql";

    private static readonly IReadOnlyDictionary<string, ExpectedIndexSpec> ExpectedIndexes =
        new Dictionary<string, ExpectedIndexSpec>(StringComparer.Ordinal)
        {
            ["ix_queue_entries_sp_status_created"] = new(
                TableName: "queue_entries",
                KeyColumnCount: 3,
                OrderedKeyColumns: "service_point_id:ASC,status:ASC,arrived_at:ASC"),
            ["ix_visits_hospital_created_status"] = new(
                TableName: "visits",
                KeyColumnCount: 2,
                OrderedKeyColumns: "checked_in_at:DESC,status:ASC"),
            ["ix_audit_logs_hospital_occurred_desc"] = new(
                TableName: "audit_logs",
                KeyColumnCount: 2,
                OrderedKeyColumns: "hospital_id:ASC,occurred_at:DESC"),
            ["ix_audit_logs_action_occurred_desc"] = new(
                TableName: "audit_logs",
                KeyColumnCount: 2,
                OrderedKeyColumns: "action:ASC,occurred_at:DESC"),
            ["ix_medicine_batches_fefo_lookup"] = new(
                TableName: "medicine_batches",
                KeyColumnCount: 3,
                OrderedKeyColumns: "medicine_id:ASC,is_blocked:ASC,expiry_date:ASC"),
            ["ix_service_prices_effective_lookup"] = new(
                TableName: "service_prices",
                KeyColumnCount: 2,
                OrderedKeyColumns: "service_id:ASC,effective_from:DESC"),
        };

    private static readonly string[] RetainedSingleColumnIndexes =
    [
        "ix_queue_entries_service_point_id",
        "ix_queue_entries_current_service_point_id",
        "ix_audit_logs_hospital_id",
        "ix_medicine_batches_medicine_id",
        "ix_service_prices_service_id",
    ];

    public HotQueryCompositeIndexesTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Migration021_AllHotQueryCompositeIndexesExist_WithExactCatalogDefinitions_AndExistingIndexesRetained()
    {
        var catalogRows = await QueryPublicHotIndexCatalogMetadataAsync();
        AssertAllExpectedIndexesMatchCatalog(catalogRows);

        var publicIndexNames = await Db.Database
            .SqlQueryRaw<string>(
                "SELECT indexname::text AS \"Value\" FROM pg_indexes WHERE schemaname = 'public'")
            .ToListAsync();

        foreach (var retainedIndex in RetainedSingleColumnIndexes)
        {
            Assert.Contains(retainedIndex, publicIndexNames);
        }
    }

    [Fact]
    public async Task Migration021_SqlScript_HasLockTimeoutAndIsIdempotentWhenReapplied()
    {
        var migrationPath = ResolveMigrationFilePath();
        Assert.True(File.Exists(migrationPath), $"Expected migration file at '{migrationPath}'.");

        var sql = await File.ReadAllTextAsync(migrationPath);
        Assert.DoesNotContain("\r\n", sql);
        Assert.Contains("BEGIN;", sql, StringComparison.Ordinal);
        Assert.Contains("SET LOCAL lock_timeout", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX IF NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", sql, StringComparison.Ordinal);

        await Db.Database.ExecuteSqlRawAsync(sql);

        var catalogRows = await QueryPublicHotIndexCatalogMetadataAsync();
        AssertAllExpectedIndexesMatchCatalog(catalogRows);
    }

    [Fact]
    public async Task CatalogVerifier_DetectsStaleWrongDefinitionIndex_WhenIfNotExistsSkipsRebuild()
    {
        await using var tx = await Db.Database.BeginTransactionAsync();

        await Db.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE tmp_idx_drift_829 (
                service_id bigint,
                effective_from timestamp without time zone
            ) ON COMMIT DROP;

            CREATE INDEX ix_tmp_drift_829_lookup
                ON tmp_idx_drift_829 (service_id);

            CREATE INDEX IF NOT EXISTS ix_tmp_drift_829_lookup
                ON tmp_idx_drift_829 (service_id, effective_from DESC);
            """);

        var driftedRow = await Db.Database
            .SqlQueryRaw<HotIndexCatalogRow>("""
                SELECT
                    ns.nspname::text AS "SchemaName",
                    tbl.relname::text AS "TableName",
                    idx.relname::text AS "IndexName",
                    pg_get_indexdef(idx.oid)::text AS "IndexDef",
                    am.amname::text AS "AccessMethod",
                    i.indisvalid AS "IsValid",
                    i.indisready AS "IsReady",
                    i.indisunique AS "IsUnique",
                    (i.indpred IS NOT NULL) AS "IsPartial",
                    i.indnkeyatts::int AS "KeyColumnCount",
                    string_agg(
                        att.attname::text || ':' || CASE WHEN (i.indoption[ord.n] & 1) = 1 THEN 'DESC' ELSE 'ASC' END,
                        ',' ORDER BY ord.n
                    )::text AS "OrderedKeyColumns"
                FROM pg_index i
                JOIN pg_class idx ON idx.oid = i.indexrelid
                JOIN pg_namespace ns ON ns.oid = idx.relnamespace
                JOIN pg_class tbl ON tbl.oid = i.indrelid
                JOIN pg_am am ON am.oid = idx.relam
                JOIN LATERAL generate_subscripts(i.indkey, 1) AS ord(n) ON ord.n < i.indnkeyatts
                JOIN pg_attribute att ON att.attrelid = tbl.oid AND att.attnum = i.indkey[ord.n]
                WHERE ns.oid = pg_my_temp_schema()
                  AND idx.relname = 'ix_tmp_drift_829_lookup'
                GROUP BY
                    ns.nspname,
                    tbl.relname,
                    idx.relname,
                    idx.oid,
                    am.amname,
                    i.indisvalid,
                    i.indisready,
                    i.indisunique,
                    i.indpred,
                    i.indnkeyatts
                """)
            .SingleAsync();

        // PostgreSQL IF NOT EXISTS keeps the stale 1-column definition rather than rebuilding it.
        Assert.Equal(1, driftedRow.KeyColumnCount);
        Assert.Equal("service_id:ASC", driftedRow.OrderedKeyColumns);

        var expectedTwoColumnSpec = new ExpectedIndexSpec(
            TableName: "tmp_idx_drift_829",
            KeyColumnCount: 2,
            OrderedKeyColumns: "service_id:ASC,effective_from:DESC");

        Assert.ThrowsAny<XunitException>(() =>
            AssertSingleIndexMatchesExpectedCatalog(
                "ix_tmp_drift_829_lookup",
                expectedShadowSchema: driftedRow.SchemaName,
                expectedTwoColumnSpec,
                driftedRow));

        await tx.RollbackAsync();
    }

    private async Task<List<HotIndexCatalogRow>> QueryPublicHotIndexCatalogMetadataAsync()
    {
        return await Db.Database
            .SqlQueryRaw<HotIndexCatalogRow>("""
                SELECT
                    pgx.schemaname::text AS "SchemaName",
                    pgx.tablename::text AS "TableName",
                    pgx.indexname::text AS "IndexName",
                    pgx.indexdef::text AS "IndexDef",
                    am.amname::text AS "AccessMethod",
                    i.indisvalid AS "IsValid",
                    i.indisready AS "IsReady",
                    i.indisunique AS "IsUnique",
                    (i.indpred IS NOT NULL) AS "IsPartial",
                    i.indnkeyatts::int AS "KeyColumnCount",
                    string_agg(
                        att.attname::text || ':' || CASE WHEN (i.indoption[ord.n] & 1) = 1 THEN 'DESC' ELSE 'ASC' END,
                        ',' ORDER BY ord.n
                    )::text AS "OrderedKeyColumns"
                FROM pg_indexes pgx
                JOIN pg_namespace ns ON ns.nspname = pgx.schemaname
                JOIN pg_class tbl ON tbl.relnamespace = ns.oid AND tbl.relname = pgx.tablename
                JOIN pg_class idx ON idx.relnamespace = ns.oid AND idx.relname = pgx.indexname
                JOIN pg_index i ON i.indexrelid = idx.oid AND i.indrelid = tbl.oid
                JOIN pg_am am ON am.oid = idx.relam
                JOIN LATERAL generate_subscripts(i.indkey, 1) AS ord(n) ON ord.n < i.indnkeyatts
                JOIN pg_attribute att ON att.attrelid = tbl.oid AND att.attnum = i.indkey[ord.n]
                WHERE pgx.schemaname = 'public'
                  AND pgx.indexname IN (
                      'ix_queue_entries_sp_status_created',
                      'ix_visits_hospital_created_status',
                      'ix_audit_logs_hospital_occurred_desc',
                      'ix_audit_logs_action_occurred_desc',
                      'ix_medicine_batches_fefo_lookup',
                      'ix_service_prices_effective_lookup'
                  )
                GROUP BY
                    pgx.schemaname,
                    pgx.tablename,
                    pgx.indexname,
                    pgx.indexdef,
                    am.amname,
                    i.indisvalid,
                    i.indisready,
                    i.indisunique,
                    i.indpred,
                    i.indnkeyatts
                ORDER BY pgx.indexname
                """)
            .ToListAsync();
    }

    private static void AssertAllExpectedIndexesMatchCatalog(IReadOnlyCollection<HotIndexCatalogRow> actualRows)
    {
        var byName = actualRows.ToDictionary(r => r.IndexName, StringComparer.Ordinal);

        Assert.Equal(ExpectedIndexes.Count, byName.Count);

        foreach (var (expectedIndexName, expected) in ExpectedIndexes)
        {
            Assert.True(
                byName.TryGetValue(expectedIndexName, out var actual),
                $"Missing composite index '{expectedIndexName}' in public schema pg_indexes.");

            AssertSingleIndexMatchesExpectedCatalog(expectedIndexName, "public", expected, actual);
        }
    }

    private static void AssertSingleIndexMatchesExpectedCatalog(
        string expectedIndexName,
        string expectedShadowSchema,
        ExpectedIndexSpec expected,
        HotIndexCatalogRow actual)
    {
        Assert.Equal(expectedShadowSchema, actual.SchemaName);
        Assert.Equal(expected.TableName, actual.TableName);
        Assert.Equal("btree", actual.AccessMethod);
        Assert.True(actual.IsValid, $"Index '{expectedIndexName}' has indisvalid = false.");
        Assert.True(actual.IsReady, $"Index '{expectedIndexName}' has indisready = false.");
        Assert.False(actual.IsUnique, $"Index '{expectedIndexName}' must be non-unique.");
        Assert.False(actual.IsPartial, $"Index '{expectedIndexName}' must not have a partial predicate.");
        Assert.Equal(expected.KeyColumnCount, actual.KeyColumnCount);
        Assert.Equal(expected.OrderedKeyColumns, actual.OrderedKeyColumns);
        Assert.Contains($"USING btree", actual.IndexDef, StringComparison.Ordinal);
    }

    private static string ResolveMigrationFilePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "database", "migrations", MigrationFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate database/migrations/{MigrationFileName} starting from '{AppContext.BaseDirectory}'.");
    }

    private sealed record ExpectedIndexSpec(
        string TableName,
        int KeyColumnCount,
        string OrderedKeyColumns);

    public sealed class HotIndexCatalogRow
    {
        public string SchemaName { get; set; } = string.Empty;

        public string TableName { get; set; } = string.Empty;

        public string IndexName { get; set; } = string.Empty;

        public string IndexDef { get; set; } = string.Empty;

        public string AccessMethod { get; set; } = string.Empty;

        public bool IsValid { get; set; }

        public bool IsReady { get; set; }

        public bool IsUnique { get; set; }

        public bool IsPartial { get; set; }

        public int KeyColumnCount { get; set; }

        public string OrderedKeyColumns { get; set; } = string.Empty;
    }
}
