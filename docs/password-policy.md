# Password policy and rejection codes

## Where the rules live

`PropertyApi.Application/Auth/Services/PasswordSecurityService.cs` is the only authority.
It runs three groups of checks, in order, and stops at the first group that fails:

1. **Complexity** — length ≥ 8, at least one uppercase, lowercase, digit, and symbol.
2. **Guessable patterns** — a common base word dressed up with digits or punctuation
   (`Password1!`, `Qwerty123!`), or a run of more than four sequential or repeated
   characters. These need no network and cannot fail open, which is why they run before
   the breach lookup rather than after it.
3. **Breach screening** — Have I Been Pwned, k-anonymity, over the SHA-1 prefix. This one
   **fails open**: an outage must not block registration. `PwnedPasswordsCircuitBreaker`
   stops the hammering and `ApplicationTelemetry.RecordPasswordBreachScreening` counts
   every skipped check, so "no breached passwords rejected lately" can be told apart from
   "screening has been silently off for a week".

## Rejection codes

Every failure carries a stable code alongside its English message. **The code is the
contract.** A localised client cannot translate a sentence, so renaming one of these
silently reverts a translated message to English for Arabic and German users.

| Code | Meaning | Checked in the browser? |
|---|---|---|
| `PASSWORD_REQUIRED` | Empty | yes |
| `PASSWORD_TOO_SHORT` | Fewer than 8 characters | yes |
| `PASSWORD_NO_UPPERCASE` | No `A-Z` | yes |
| `PASSWORD_NO_LOWERCASE` | No `a-z` | yes |
| `PASSWORD_NO_DIGIT` | No `0-9` | yes |
| `PASSWORD_NO_SPECIAL` | No symbol | yes |
| `PASSWORD_COMMON_WORD` | Common base word plus decoration | yes |
| `PASSWORD_LONG_RUN` | More than four sequential or repeated characters | yes |
| `PASSWORD_BREACHED` | Present in a known breach corpus | **no — server only** |

`PasswordErrorCodes` in `Auth/Interfaces/IPasswordSecurityService.cs` declares them.

## How a code reaches the client

`RegisterCommandValidator` reports each failure through the validation context with its
`ErrorCode` set. `ValidationBehavior` turns those into the application's own
`ValidationException`, which now carries an `ErrorCodes` dictionary parallel to `Errors`,
and `ExceptionHandlingMiddleware` emits both:

```json
{
  "title": "Validation Error",
  "status": 422,
  "errors":     { "Password": ["This password has been exposed in known data breaches. Please choose a different password."] },
  "errorCodes": { "Password": ["PASSWORD_BREACHED"] }
}
```

`errorCodes` is additive — a client that ignores it still gets the message it always got.
Rules that set no deliberate code contribute FluentValidation's own rule name
(`NotEmptyValidator` and friends); clients translate what they recognise and fall back to
the message for the rest. Dictionary keys stay in C# property casing (`Password`) because
`PropertyNamingPolicy` does not apply to dictionary keys.

## The browser mirror

`src/app/core/auth/password-policy.ts` in the Angular repo re-implements every rule above
except the breach lookup, so the form can state its requirements before the user submits
rather than after. The breach check cannot be mirrored: it needs the network, and a
password must not be sent to a third party from the browser.

**Changing a rule here means changing it there too.** A mirror that drifts reproduces the
original defect in a new form — a form that says "valid" over a server that says
"rejected". `password-policy.spec.ts` pins the rules; it cannot detect drift on its own.
