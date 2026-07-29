\set ON_ERROR_STOP on

WITH target_users AS (
  SELECT u."Id"
  FROM "UserAccounts" ua
  JOIN "Users" u ON u."Id" = ua."Id"
  WHERE ua."LastName" = :'marker'
), target_otp AS (
  SELECT otp.*
  FROM "PhoneOtpChallenges" otp
  WHERE otp."NormalizedPhoneNumber" = :'phone'
)
SELECT jsonb_pretty(to_jsonb(result))
FROM (
  SELECT
    :'race_kind'::text AS race_kind,
    :'concurrency'::integer AS concurrency,
    (SELECT count(*) FROM target_users) AS account_count,
    (SELECT count(*) FROM "UserAccounts" WHERE "LastName" = :'marker') AS profile_count,
    (SELECT count(*) FROM "RefreshTokens" WHERE "UserId" IN (SELECT "Id" FROM target_users)) AS refresh_token_count,
    (SELECT count(*) FROM "RefreshTokens" WHERE "UserId" IN (SELECT "Id" FROM target_users)
      AND NOT "IsRevoked" AND "ExpiresAt" > now()) AS active_refresh_token_count,
    (SELECT count(*) FROM "RefreshTokens" WHERE "UserId" IN (SELECT "Id" FROM target_users)
      AND "IsRevoked") AS revoked_refresh_token_count,
    (SELECT count(*) FROM target_otp) AS otp_challenge_count,
    (SELECT count(*) FROM target_otp WHERE "ConsumedAtUtc" IS NOT NULL) AS consumed_otp_count,
    COALESCE((SELECT max("AttemptCount") FROM target_otp), 0) AS maximum_otp_attempt_count,
    CASE
      WHEN :'race_kind' = 'refresh' THEN
        (SELECT count(*) FROM target_users) = 1
        AND (SELECT count(*) FROM "RefreshTokens" WHERE "UserId" IN (SELECT "Id" FROM target_users)
          AND NOT "IsRevoked" AND "ExpiresAt" > now()) <= 1
      WHEN :'race_kind' = 'otp-invalid' THEN
        (SELECT count(*) FROM target_users) = 0
        AND (SELECT count(*) FROM target_otp WHERE "ConsumedAtUtc" IS NOT NULL) = 0
        AND COALESCE((SELECT max("AttemptCount") FROM target_otp), 0) <= 3
      ELSE
        (SELECT count(*) FROM target_users) <= 1
        AND (SELECT count(*) FROM "UserAccounts" WHERE "LastName" = :'marker') <= 1
        AND (SELECT count(*) FROM target_otp WHERE "ConsumedAtUtc" IS NOT NULL) <= 1
    END AS invariant_passed
) AS result;
