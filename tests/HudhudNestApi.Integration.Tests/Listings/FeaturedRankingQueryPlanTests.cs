using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// The query plan of the REAL default-list statements, the ones EF Core sends for GET /api/Properties,
/// over a table large enough for the planner to prefer an index (30,000 listings, vacuumed and analysed).
/// The hand-written statements of the performance plan gate cannot notice when the repository's query
/// stops being index-friendly; this does. QA Phase 18 D16: the old list read and sorted every row.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class FeaturedRankingQueryPlanTests : IAsyncLifetime
{
    private const int Rows = 30_000;

    private sealed record Captured(string Sql, NpgsqlParameter[] Parameters);

    private sealed class CaptureInterceptor : DbCommandInterceptor
    {
        public List<Captured> Commands { get; } = [];
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Enabled)
            {
                lock (Commands)
                    Commands.Add(new Captured(command.CommandText, command.Parameters.OfType<NpgsqlParameter>().Select(p => p.Clone()).ToArray()));
            }
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }

    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"pl{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        var owner = await _factory.SeedUserAsync($"{_marker}@test.local");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await GovernoratesSeed.SeedAsync(db);

        var template = Property.Create($"{_marker} template", "وصف كافٍ للإعلان", owner.UserAccountId, ListingType.ForSale, currencyCode: "USD");
        db.Properties.Add(template);
        await db.SaveChangesAsync();

        // Copy the template 30,000 times, column by column (generated columns excluded), spreading
        // creation dates, governorates and a 1% share of live featured ads.
        var columns = new List<string>();
        await using (var conn = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using (var cmd = new NpgsqlCommand(
                "SELECT column_name FROM information_schema.columns WHERE table_name = 'Properties' AND table_schema = current_schema() AND is_generated = 'NEVER' AND generation_expression IS NULL ORDER BY ordinal_position", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync()) columns.Add(reader.GetString(0));

            string Value(string c) => c switch
            {
                "Id" => "gen_random_uuid()",
                "Title" => $"'{_marker} row ' || i",
                "CreatedAt" => "(now() at time zone 'utc') - (i || ' minutes')::interval",
                "GovernorateId" => "1 + (i % 14)",
                "IsFeatured" => "(i % 100 = 0)",
                "FeaturedUntil" => "CASE WHEN i % 100 = 0 THEN (now() at time zone 'utc') + interval '10 days' END",
                _ => $"t.\"{c}\""
            };

            var sql = $"INSERT INTO \"Properties\" ({string.Join(",", columns.Select(c => $"\"{c}\""))}) " +
                      $"SELECT {string.Join(",", columns.Select(Value))} FROM \"Properties\" t, generate_series(1, {Rows}) i WHERE t.\"Id\" = @id";
            await using (var insert = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 })
            {
                insert.Parameters.AddWithValue("id", template.Id);
                Assert.Equal(Rows, await insert.ExecuteNonQueryAsync());
            }

            await using var vacuum = new NpgsqlCommand("VACUUM (ANALYZE) \"Properties\"", conn) { CommandTimeout = 120 };
            await vacuum.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Properties\" WHERE \"Title\" LIKE {_marker + "%"}");
        }
        _factory.Dispose();
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement plan)
    {
        yield return plan;
        if (plan.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                foreach (var node in Nodes(child))
                    yield return node;
    }

    private async Task<List<(string Sql, JsonElement Plan)>> ExplainRequestAsync(string url)
    {
        var capture = new CaptureInterceptor();
        var client = _factory
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(capture))))
            .CreateClient();

        await client.GetAsync(url);          // warm-up: output cache, JIT, connection
        capture.Commands.Clear();
        capture.Enabled = true;
        var response = await client.GetAsync(url + "&searchTerm=%20"); // blank term: same filter, a new cache key
        capture.Enabled = false;
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var plans = new List<(string, JsonElement)>();
        await using var scope = _factory.Services.CreateAsyncScope();
        await using var conn = new NpgsqlConnection(scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString());
        await conn.OpenAsync();
        foreach (var command in capture.Commands.Where(c => c.Sql.Contains("\"Properties\"")))
        {
            await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, FORMAT JSON) " + command.Sql, conn);
            foreach (var p in command.Parameters) explain.Parameters.Add(p);
            var json = (string)(await explain.ExecuteScalarAsync())!;
            using var doc = JsonDocument.Parse(json);
            plans.Add((command.Sql, doc.RootElement[0].GetProperty("Plan").Clone()));
        }
        return plans;
    }

    [Theory(DisplayName = "The default list never scans or sorts the whole Properties table (home section and a deep page)")]
    [InlineData("/api/Properties?page=1&pageSize=6&interestGovernorateIds=3,7")]
    [InlineData("/api/Properties?page=400&pageSize=20&interestGovernorateIds=3,7")]
    [InlineData("/api/Properties?page=1&pageSize=20")]
    public async Task RealStatements_UseIndexes(string url)
    {
        var plans = await ExplainRequestAsync(url);

        // The aggregate count, the page ids and the entity load: three statements over Properties.
        Assert.InRange(plans.Count, 2, 3);
        foreach (var (sql, plan) in plans)
        {
            var scans = Nodes(plan)
                .Where(n => n.GetProperty("Node Type").GetString() is "Seq Scan" or "Parallel Seq Scan"
                            && n.GetProperty("Relation Name").GetString() == "Properties")
                .ToList();
            Assert.True(scans.Count == 0, $"Seq Scan over Properties in:\n{sql}\n\nPlan:\n{plan}");

            // The old statement sorted every row by the featured-now expression; nothing may sort that many.
            foreach (var sort in Nodes(plan).Where(n => n.GetProperty("Node Type").GetString() == "Sort"))
                Assert.True(sort.GetProperty("Actual Rows").GetDouble() * sort.GetProperty("Actual Loops").GetDouble() < 1000,
                    $"A sort of {sort.GetProperty("Actual Rows")} rows in:\n{sql}");
        }

        // The structure the plans above are about: two legs over one UNION ALL, no whole-table featured-first ORDER BY.
        var pageStatement = plans.Select(p => p.Sql).Single(s => s.Contains("UNION ALL"));
        Assert.DoesNotContain("ORDER BY p.\"IsFeatured\"", pageStatement.Replace("\r", ""));
        Assert.Contains("LIMIT", pageStatement);

        // The count is the index-only scan the covering index was added for.
        var countPlan = plans.Single(p => p.Sql.Contains("\"Featured\"")).Plan;
        Assert.Contains(Nodes(countPlan), n => n.TryGetProperty("Index Name", out var name) && name.GetString() == "IX_Properties_PublicList_Count");
    }
}
