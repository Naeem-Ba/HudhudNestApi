using System.Data;
using System.Data.Common;
using HudhudNestApi.Application.Listings.Translations;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Translations;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace HudhudNestApi.Infrastructure.Translation;

/// <summary>
/// PostgreSQL implementation. Claim/complete/fail are single statements so they are atomic across API
/// instances: the unique index on (listing, field, language) serialises claimants, and every write re-checks
/// that the listing still exists and its text still hashes to the value that was claimed. The hash is
/// computed in the database the same way the application computes it (SHA-256 of the UTF-8 bytes).
/// </summary>
public sealed class ListingTranslationStore : IListingTranslationStore
{
    private readonly AppDbContext _db;

    public ListingTranslationStore(AppDbContext db) => _db = db;

    public async Task<ListingTextSource?> GetPubliclyViewableTextAsync(ListingRef listing, DateTime nowUtc, CancellationToken ct)
    {
        // Same visibility rule as each kind's public detail page (soft-deleted rows are removed by the global filters).
        return listing.Kind switch
        {
            ListingTranslationSubject.Property => await _db.Properties
                .AsNoTracking()
                .Where(p => p.Id == listing.Id)
                .Where(Property.PubliclyViewable(nowUtc))
                .Select(p => new ListingTextSource(p.Title, p.Description))
                .FirstOrDefaultAsync(ct),
            ListingTranslationSubject.ShortStay => await _db.ShortStayListings
                .AsNoTracking()
                .Where(l => l.Id == listing.Id && l.IsPublished)
                .Select(l => new ListingTextSource(l.Title, l.Description))
                .FirstOrDefaultAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(listing))
        };
    }

    public async Task<IReadOnlyList<StoredListingTranslation>> GetAsync(ListingRef listing, string targetLanguage, CancellationToken ct)
    {
        var query = _db.ListingTranslations.AsNoTracking().Where(t => t.TargetLanguage == targetLanguage);
        query = listing.Kind == ListingTranslationSubject.Property
            ? query.Where(t => t.PropertyId == listing.Id)
            : query.Where(t => t.ShortStayListingId == listing.Id);

        return await query
            .Select(t => new StoredListingTranslation(
                t.Id, t.Field, t.Status, t.SourceHash, t.PolicyVersion, t.SourceLanguage, t.TranslatedText,
                t.AttemptCount, t.ClaimedUntil, t.RetryAfter, t.LastError))
            .ToListAsync(ct);
    }

    // Column names come from this closed switch, never from input.
    private static string Column(ListingTranslationField field) => field switch
    {
        ListingTranslationField.Title => "Title",
        ListingTranslationField.Description => "Description",
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    // Table and foreign-key column of each kind: closed switches, never taken from input.
    private static string Table(ListingRef listing) => listing.Kind switch
    {
        ListingTranslationSubject.Property => "Properties",
        ListingTranslationSubject.ShortStay => "ShortStayListings",
        _ => throw new ArgumentOutOfRangeException(nameof(listing))
    };

    private static string ForeignKey(ListingRef listing) => listing.Kind switch
    {
        ListingTranslationSubject.Property => "PropertyId",
        ListingTranslationSubject.ShortStay => "ShortStayListingId",
        _ => throw new ArgumentOutOfRangeException(nameof(listing))
    };

    public async Task<IReadOnlyList<TranslationClaim?>> TryClaimAsync(
        ListingRef listing, string targetLanguage, IReadOnlyList<TranslationClaimRequest> requests,
        int policyVersion, string provider, TimeSpan lease, DateTime nowUtc, CancellationToken ct)
    {
        var claims = new TranslationClaim?[requests.Count];

        // One short transaction under a per-(listing, language) advisory lock: the fields of a request are
        // claimed together or not at all relative to any other request for the same listing and language.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", ct,
            P("key", $"listing-translation:{listing.Kind}:{listing.Id:N}:{targetLanguage}"));

        for (var i = 0; i < requests.Count; i++)
            claims[i] = await TryClaimOneAsync(listing, targetLanguage, requests[i], policyVersion, provider, lease, nowUtc, ct);

        await transaction.CommitAsync(ct);
        return claims;
    }

    private async Task<TranslationClaim?> TryClaimOneAsync(
        ListingRef listing, string targetLanguage, TranslationClaimRequest request,
        int policyVersion, string provider, TimeSpan lease, DateTime nowUtc, CancellationToken ct)
    {
        var field = request.Field;
        var sourceLanguage = request.SourceLanguage;
        var sourceHash = request.SourceHash;
        var sourceCharacters = request.SourceCharacters;
        var column = Column(field);
        var table = Table(listing);
        var fk = ForeignKey(listing);
        var rowId = Guid.NewGuid();
        var token = Guid.NewGuid();

        // Insert when absent; when present, take over only if the row is stale (other text/policy), an abandoned
        // claim, or a failure whose cooldown has passed. A valid or actively-owned row yields no row => null.
        var sql = $"""
            INSERT INTO "ListingTranslations"
                ("Id","{fk}","Field","TargetLanguage","SourceLanguage","SourceHash","PolicyVersion","Provider","Status",
                 "TranslatedText","SourceCharacters","AttemptCount","ClaimToken","ClaimedUntil","RetryAfter","LastError","CreatedAt","UpdatedAt")
            SELECT @id, p."Id", @field, @target, @srclang, @hash, @policy, @provider, 1,
                   NULL, @chars, 0, @token, @until, NULL, NULL, @now, @now
            FROM "{table}" p
            WHERE p."Id" = @pid AND NOT p."IsDeleted"
              AND encode(sha256(convert_to(p."{column}", 'UTF8')), 'hex') = @hash
            ON CONFLICT ("{fk}","Field","TargetLanguage") WHERE "{fk}" IS NOT NULL DO UPDATE SET
                "SourceLanguage" = EXCLUDED."SourceLanguage",
                "SourceHash" = EXCLUDED."SourceHash",
                "PolicyVersion" = EXCLUDED."PolicyVersion",
                "Provider" = EXCLUDED."Provider",
                "Status" = 1,
                "TranslatedText" = NULL,
                "SourceCharacters" = EXCLUDED."SourceCharacters",
                "AttemptCount" = CASE WHEN "ListingTranslations"."SourceHash" = EXCLUDED."SourceHash"
                                       AND "ListingTranslations"."PolicyVersion" = EXCLUDED."PolicyVersion"
                                      THEN "ListingTranslations"."AttemptCount" ELSE 0 END,
                "ClaimToken" = EXCLUDED."ClaimToken",
                "ClaimedUntil" = EXCLUDED."ClaimedUntil",
                "RetryAfter" = NULL,
                "LastError" = NULL,
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            WHERE "ListingTranslations"."SourceHash" <> EXCLUDED."SourceHash"
               OR "ListingTranslations"."PolicyVersion" <> EXCLUDED."PolicyVersion"
               OR ("ListingTranslations"."Status" = 1 AND "ListingTranslations"."ClaimedUntil" < @now)
               OR ("ListingTranslations"."Status" = 4 AND "ListingTranslations"."RetryAfter" <= @now)
            RETURNING "AttemptCount"
            """;

        var attempts = await ScalarAsync(sql, ct,
            P("id", rowId), P("pid", listing.Id), P("field", (int)field), P("target", targetLanguage),
            P("srclang", sourceLanguage), P("hash", sourceHash), P("policy", policyVersion), P("provider", provider),
            P("chars", sourceCharacters), P("token", token), P("until", nowUtc.Add(lease)), P("now", nowUtc));

        if (attempts is null) return null;

        // On conflict the surviving row keeps its original Id, so look it up by the unique key.
        var id = await ScalarAsync(
            $"""SELECT "Id" FROM "ListingTranslations" WHERE "{fk}" = @pid AND "Field" = @field AND "TargetLanguage" = @target AND "ClaimToken" = @token""",
            ct, P("pid", listing.Id), P("field", (int)field), P("target", targetLanguage), P("token", token));

        return id is Guid guid ? new TranslationClaim(guid, token, Convert.ToInt32(attempts)) : null;
    }

    public async Task<bool> CompleteAsync(
        ListingRef listing, TranslationClaim claim, ListingTranslationField field, ListingTranslationStatus status,
        string? translatedText, string? sourceLanguage, DateTime nowUtc, CancellationToken ct)
    {
        if (status == ListingTranslationStatus.Completed && string.IsNullOrWhiteSpace(translatedText))
            return false; // an empty result is never stored as a success

        var column = Column(field);
        var sql = $"""
            UPDATE "ListingTranslations" t SET
                "Status" = @status, "TranslatedText" = @text, "SourceLanguage" = COALESCE(@srclang, t."SourceLanguage"),
                "ClaimToken" = NULL, "ClaimedUntil" = NULL, "RetryAfter" = NULL, "LastError" = NULL, "UpdatedAt" = @now
            WHERE t."Id" = @id AND t."ClaimToken" = @token AND t."Status" = 1
              AND EXISTS (SELECT 1 FROM "{Table(listing)}" p
                          WHERE p."Id" = t."{ForeignKey(listing)}" AND NOT p."IsDeleted"
                            AND encode(sha256(convert_to(p."{column}", 'UTF8')), 'hex') = t."SourceHash")
            """;

        var affected = await ExecuteAsync(sql, ct,
            P("status", (int)status), P("text", status == ListingTranslationStatus.Completed ? translatedText : null),
            P("srclang", sourceLanguage), P("now", nowUtc), P("id", claim.RowId), P("token", claim.Token));
        return affected == 1;
    }

    public async Task FailAsync(
        TranslationClaim claim, string errorCode, bool countAttempt, DateTime retryAfterUtc, DateTime nowUtc, CancellationToken ct)
    {
        const string sql = """
            UPDATE "ListingTranslations" SET
                "Status" = 4, "TranslatedText" = NULL, "ClaimToken" = NULL, "ClaimedUntil" = NULL,
                "RetryAfter" = @retry, "LastError" = @err, "AttemptCount" = "AttemptCount" + @inc, "UpdatedAt" = @now
            WHERE "Id" = @id AND "ClaimToken" = @token AND "Status" = 1
            """;
        await ExecuteAsync(sql, ct,
            P("retry", retryAfterUtc), P("err", errorCode), P("inc", countAttempt ? 1 : 0),
            P("now", nowUtc), P("id", claim.RowId), P("token", claim.Token));
    }

    public async Task<bool> TryReserveBudgetAsync(string month, long characters, long limit, DateTime nowUtc, CancellationToken ct)
    {
        if (characters > limit) return false;

        const string sql = """
            INSERT INTO "TranslationMonthlyUsages" ("Month","Characters","UpdatedAt") VALUES (@month, @chars, @now)
            ON CONFLICT ("Month") DO UPDATE SET
                "Characters" = "TranslationMonthlyUsages"."Characters" + EXCLUDED."Characters", "UpdatedAt" = EXCLUDED."UpdatedAt"
            WHERE "TranslationMonthlyUsages"."Characters" + EXCLUDED."Characters" <= @limit
            """;
        return await ExecuteAsync(sql, ct, P("month", month), P("chars", characters), P("now", nowUtc), P("limit", limit)) == 1;
    }

    private static NpgsqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);

    private async Task<int> ExecuteAsync(string sql, CancellationToken ct, params NpgsqlParameter[] parameters)
        => await _db.Database.ExecuteSqlRawAsync(sql, parameters.Cast<object>().ToArray(), ct);

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        var connection = _db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            var current = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (current is not null) command.Transaction = current;
            foreach (var parameter in parameters) command.Parameters.Add(parameter);
            var result = await command.ExecuteScalarAsync(ct);
            return result is DBNull ? null : result;
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
