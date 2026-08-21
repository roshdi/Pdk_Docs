# Authentication, Authorization, and Sessions

PDK separates credential validation, request authentication, authorization, and
login-session persistence. This allows the same service code to run alone during
development, inside the platform monolith, or as an independently hosted service.

## Runtime contracts

| Contract | Responsibility | Typical implementation |
| --- | --- | --- |
| `ICredentialAuthenticationService` | Validates credentials and creates the claims used at login | In-memory identity or the Identity host |
| `IAuthenticationService` | Reads the already validated `ClaimsPrincipal` for the current request | `HttpContextAuthenticationService` |
| `IAuthorizationService` | Returns one unrestricted, filtered, or denied decision | Claims, the Identity database, or an Identity client |
| `IUserSessionService` | Creates, validates, lists, and revokes login sessions | No-op in memory or database-backed in Identity |

Business services never receive user passwords. ASP.NET Core first validates the
cookie or bearer token; PDK then reads that trusted principal and, when present,
validates its `pdk:session_id` before an endpoint runs.

## Built-in Identity service

Identity is a built-in PDK service. Derive the platform definition from
`Pdk.Definitions.BuiltInServices.IdentityService` and customize it through
`IdentityServiceBuilder`:

```csharp
public sealed class IdentityService
    : Pdk.Definitions.BuiltInServices.IdentityService
{
    protected override void Configure()
    {
        DefaultSchema("sec");

        Configure(identity =>
        {
            identity.User.TableName = "Users";

            identity.Session.Enabled = true;
            identity.Session.EntityName = "Session";
            identity.Session.TableName = "Sessions";
        });
    }
}
```

Sessions are enabled by default. Generation creates a session entity derived from
`IdentitySession<AuthId>` and an Identity DbContext derived from
`PdkIdentityDbContext<...>`. The PDK base context owns the session key, indexes,
column lengths, user foreign key, and cascade-delete mapping. Applications can add
properties in the generated entity's companion partial class without duplicating
the base mapping.

Set `identity.Session.Enabled = false` only when persistent login sessions are not
required. In that mode PDK registers a no-op session service and does not issue a
session claim.

## Host modes

### Single-service development

Use in-memory identity while developing or testing one service without running the
Identity database:

```csharp
builder.Services.AddPdkInMemoryIdentity();
builder.Services.AddIdentitySeedsFromAssembly(
    typeof(CatalogDeveloperIdentitySeed).Assembly);
```

In-memory identity uses `ClaimsAuthorizationService`. It intentionally does not
persist sessions, so session creation returns `null`, active-session listing is
empty, and session validation is skipped.

### Identity host and platform monolith

The generated Identity API module registers ASP.NET Core Identity and the database
adapters using the generated types:

```csharp
builder.Services
    .AddIdentityCore<UserEntity>()
    .AddRoles<RoleEntity>()
    .AddEntityFrameworkStores<IdentityDbContext>();

builder.Services.AddPdkIdentityAuthentication<
    IdentityDbContext,
    UserEntity,
    RoleEntity,
    SessionEntity,
    AuthId>();
```

This registration replaces development identity services with the database-backed
credential, authorization, seed-store, and session implementations. Therefore, when
all modules run in the platform web application, Identity remains authoritative and
the in-memory implementation is not used.

If sessions are disabled, the generator uses the four-entity registration overload,
which installs `NoOpUserSessionService`.

### Independently hosted client service

A service that receives Identity-issued JWTs can authorize from claims:

```csharp
builder.Services.AddPdkClaimsAuthorization();
```

This avoids a network call for each permission check, but permission changes take
effect only after token renewal. If permission claims are disabled, the service must
instead register its database-backed or remote Identity implementation of
`IAuthorizationService`; claims authorization cannot infer permissions that are not
in the principal.

## Identity seeds

Keep provider-neutral identity seeds in each service's Core project:

```csharp
public sealed class CatalogDeveloperIdentitySeed : IIdentitySeedContributor
{
    public string Name => "Catalog.Developer";
    public string Profile => "Developer";

    public void Configure(IdentitySeedBuilder identity)
    {
        identity.Role("CatalogDeveloper", role => role
            .Permission("catalog.products.read", SecurityScopes.Unit)
            .Permission("catalog.products.create", SecurityScopes.All));

        identity.User("catalog-developer", user => user
            .UserName("catalog.dev")
            .DisplayName("Catalog Developer")
            .PasswordFrom("IdentitySeeds:CatalogDeveloper:Password")
            .Role("CatalogDeveloper")
            .Permission("catalog.products.update", SecurityScopes.Owner)
            .Deny("catalog.products.delete"));
    }
}
```

Generated API modules discover contributors from their own Core assembly. At host
startup, PDK applies all contributors whose profile is active:

```csharp
await app.Services.ApplyIdentitySeedsAsync(
    SeedProfiles.For(app.Environment.EnvironmentName));
```

The same contributors work with the in-memory store and the ASP.NET Core Identity
store. Password configuration values are development secrets and must not be
committed to source control.

## Authentication configuration

Configuration is bound from the `Authentication` section:

```json
{
  "Authentication": {
    "Session": {
      "Lifetime": "30.00:00:00"
    },
    "Claims": {
      "IncludePermissions": false
    }
  }
}
```

### Session lifetime

`Authentication:Session:Lifetime` controls newly created database sessions. It is
not supplied by `LoginRequest` and cannot be chosen by a client.

- Missing or `null`: the session has no expiration (`ExpiresAt = null`).
- Positive `TimeSpan`: `ExpiresAt = CreatedAt + Lifetime`.
- Zero or negative: application startup fails configuration validation.

Session lifetime is distinct from JWT and cookie lifetimes. JWTs should remain
short-lived even when a database session has no expiration.

### Permission claims

`Authentication:Claims:IncludePermissions` controls whether the resolved,
versioned `pdk:authorization` snapshot is emitted into both JWT and cookie
identities. The default is `true`. Setting it to `false` also removes legacy
`permissions`, `pdk:grant`, and `pdk:deny` claims if a custom credential provider
returns them.

Set it to `false` when authorization must read current permissions from the Identity
database or a remote Identity service. User identity, role, display-name, and session
claims are unaffected.

## Login and token consistency

`POST /auth/login` accepts only the user name and password. After credentials are
validated, PDK:

1. Builds one common claims collection.
2. Creates a database session when sessions are enabled.
3. Adds the same `pdk:session_id` to the common claims.
4. Issues the cookie and JWT from that same collection.

The cookie authentication ticket and JWT are different serialized formats, but they
represent the same identity and session. PDK does not store the literal JWT string in
the cookie.

## Session endpoints

All session endpoints except login require an authenticated user.

### List active sessions

```http
GET /auth/sessions
```

Returns the current user's non-revoked, non-expired sessions ordered newest first.
Each item contains the session ID, IP address, detected operating system and browser,
user agent, creation and expiration times, and `isCurrent`. The current session is
identified using the `pdk:session_id` claim.

### Revoke the current session and log out

```http
POST /auth/sessions/revoke
```

When the principal contains a session ID, PDK revokes that session. For cookie
authentication it also removes the authentication cookie. In-memory identity has no
persisted session, so this route performs cookie logout only.

### Revoke a selected session

```http
POST /auth/sessions/revoke/{sessionId}
```

Revokes the selected session only when it belongs to the current user. A missing
session returns `404 Not Found`; it is never possible to revoke another user's
session through this endpoint.

Revocation takes effect on the next protected request because the authentication
preprocessor validates the token's session against `IUserSessionService`.

## Claims and authorization behavior

PDK uses standard name identifier, name, role, and email claims plus:

- `pdk:display_name`
- `pdk:session_id`
- `pdk:authorization` (one versioned effective-authorization JSON snapshot)

The current snapshot schema version is `1`. It contains only allowed effective
permissions. Each permission is either unrestricted or contains all contributing
filtered scopes, including root permission, source, optional source ID, constraint,
schema ID, and schema version. Explicit denies and invalidated roots are evaluated
before emission and are not serialized as effective grants.

When a snapshot claim exists, it is authoritative. Duplicate claims, malformed JSON,
unsupported versions, duplicate permission IDs, or structurally inconsistent
decisions fail closed with `InvalidAuthorizationData`; legacy claims are not used as
a downgrade fallback. `permissions`, `pdk:grant`, and `pdk:deny` remain readable only
for identities issued before the versioned format was introduced.

Claims authorization returns every scope in a filtered snapshot. It does not choose
one scope or merge grants merely because their scope IDs match: Owner and Unit are
both retained, and two Unit grants with different roots or constraints retain their
individual provenance. Downstream security filters OR-compose these grants. `All`
has already been normalized to an unrestricted decision, while `None` is absent from
the snapshot and contributes no access in the legacy reader.

Endpoints declare a required permission through `RequirePermission(...)`.
Authentication failure or an invalid/revoked session returns `401 Unauthorized`;
an authenticated user who lacks the required permission receives `403 Forbidden`.

`IAuthorizationService.AuthorizeAsync(permission)` returns a
`PermissionAuthorizationDecision`; callers must not separately test a Boolean and
then load scope data. `IsAllowed` controls permission access, `IsUnrestricted`
identifies `All`, and filtered decisions carry every contributing scope with its
constraint envelope and root provenance. Denied decisions carry a stable
`AuthorizationDenialReason` and never carry scopes.

The structured Identity implementation reads the five authorization entities,
applies implication and user denies, removes `None`, applies `All` dominance, and
selects permission-specific scope data before overall data. Hosts without those
entities retain the legacy Identity-claim fallback. Claims-only services interpret a
plain permission claim as unrestricted and a `pdk:grant` claim as filtered.

Authorization snapshot schema changes require a new version and explicit reader
support. Readers never assume that an unknown version is compatible.

## Operational guidance

- Use HTTPS and secure, HTTP-only cookies in deployed environments.
- Keep JWT signing keys outside source control and configure issuer and audience
  consistently across Identity and client services.
- Do not treat token expiration as session revocation; validate persisted sessions on
  protected requests when immediate revocation is required.
- Consider retention cleanup for expired and revoked session rows.
- If permission claims are enabled, keep JWT lifetimes short enough for acceptable
  permission-change propagation.
