# BankStatements — Backend API

The backend is an ASP.NET Core 10 Web API that handles user authentication, PDF statement uploads, transaction parsing, and spending analysis against a PostgreSQL database.

---

## Project Structure

```
backend/
├── Statements.WebAPI/               # Main API project
│   ├── Auth/                        # Authentication primitives
│   │   ├── BCryptPasswordHasher.cs  # BCrypt hash/verify
│   │   ├── IJwtTokenService.cs      # JWT generation interface
│   │   ├── IPasswordHasher.cs       # Password hashing interface
│   │   ├── JwtAccessToken.cs        # Access token model
│   │   ├── JwtOptions.cs            # JWT configuration options
│   │   └── JwtTokenService.cs       # JWT + refresh token implementation
│   ├── Contracts/                   # Request/response DTOs
│   │   ├── Auth/                    # Login, Register, ExternalLogin, Refresh DTOs
│   │   ├── Analysis/                # Spending summary response
│   │   └── Statements/              # Statement upload response
│   ├── Controllers/                 # API endpoint controllers
│   │   ├── AuthController.cs        # /api/auth/*
│   │   ├── StatementsController.cs  # /api/statements/*
│   │   ├── AnalysisController.cs    # /api/analysis/*
│   │   └── HealthController.cs      # /api/health
│   ├── Data/                        # Data access layer
│   │   ├── IDbConnectionFactory.cs  # Connection factory interface
│   │   ├── NpgsqlConnectionFactory.cs # PostgreSQL implementation
│   │   ├── IDbExecutor.cs           # Query executor interface
│   │   └── DapperDbExecutor.cs      # Dapper implementation
│   ├── Infrastructure/              # Cross-cutting concerns
│   │   ├── DateOnlyHandler.cs       # Dapper type handler for DateOnly
│   │   └── NullableDateOnlyHandler.cs
│   ├── Models/                      # Domain models
│   │   └── WeatherForecast.cs       # Scaffold (can be removed)
│   ├── Services/                    # Business logic
│   │   ├── Auth/
│   │   │   ├── IAuthService.cs      # Auth service interface
│   │   │   ├── AuthService.cs       # Register, login, token refresh, OAuth
│   │   │   ├── IExternalAuthValidator.cs
│   │   │   └── ExternalAuthValidator.cs # Google/Auth0 token validation
│   │   ├── Analysis/
│   │   │   ├── IAnalysisService.cs
│   │   │   └── AnalysisService.cs   # Spending aggregation & summary
│   │   └── Statements/
│   │       ├── IStatementService.cs
│   │       ├── StatementService.cs  # Upload, scan, parse, store
│   │       ├── IStatementParser.cs
│   │       ├── PdfStatementParser.cs # PDF text extraction via PdfPig
│   │       ├── IVirusScanService.cs
│   │       └── ClamAvVirusScanService.cs # ClamAV client
│   ├── Program.cs                   # Entry point, DI, middleware
│   ├── appsettings.json             # Base config + Serilog + ClamAV
│   └── appsettings.Development.json # Dev overrides
├── Statements.WebAPI.Tests/         # Test project
│   ├── Contracts/                   # Request validation tests
│   ├── Controllers/                 # Controller unit tests
│   ├── Infrastructure/              # DateOnly handler tests
│   ├── IntegrationTests/            # E2E tests with Testcontainers
│   │   ├── AuthServiceIntegrationTests.cs
│   │   ├── DatabaseFixture.cs
│   │   └── TestSqlScripts.cs
│   └── Services/                    # Service unit tests
├── Uploads/                         # Uploaded PDF storage
└── Dockerfile                       # Multi-stage build
```

---

## How to Add a New Endpoint

1. **Create a contract** — Add request/response DTOs in the appropriate `Contracts/` subfolder
2. **Add a service interface + implementation** — Business logic goes in `Services/` — register in DI in `Program.cs`
3. **Add a controller action** — Create or extend a controller in `Controllers/`
4. **Register in DI** — Add the service registration to `Program.cs` (e.g., `builder.Services.AddScoped<IMyService, MyService>()`)
5. **Add validation** — Add request validation tests in the test project
6. **Add unit tests** — Test the controller and service in `Statements.WebAPI.Tests/`

---

## Database Migrations

Migration files live in `database/init/` and follow this naming convention:

```
NNN_description.sql
```

Where `NNN` is a zero-padded sequence number. Files are executed alphabetically on database initialisation via the PostgreSQL Docker entrypoint (`/docker-entrypoint-initdb.d`).

**Current migrations:**

| File | Description |
|------|-------------|
| `001_create_tables.sql` | Core schema — users, refresh tokens, bank accounts, statements, transactions, categories, analysis runs |
| `002_seed_data.sql` | Demo user, bank account, and sample transactions |
| `003_add_scan_columns.sql` | Adds `scan_status` and `scanned_at` to `bank_statements` |
| `004_add_user_lock_columns.sql` | Adds `failed_login_attempts` and `locked_until` to `app_users` |

To add a new migration:
1. Create `005_your_description.sql` in `database/init/`
2. Run `docker compose down` and `docker compose up --build` — PostgreSQL will execute the new file on a fresh volume, or you can apply it manually via pgAdmin.

---

## Testing

### Test stack

| Library | Purpose |
|---------|---------|
| xUnit | Test framework |
| Moq | Mocking |
| FluentAssertions | Readable assertions |
| Testcontainers.PostgreSql | Ephemeral PostgreSQL for integration tests |

### Conventions

- **Unit tests** — No special trait. Test controllers, services, and contracts in isolation.
- **Integration tests** — Marked with `[Trait("Category", "Integration")]`. These spin up a temporary PostgreSQL container and test real database interactions.

### Running tests

```bash
# Unit tests only (build gate)
docker build --target test -t backend-tests ./backend
docker run --rm backend-tests

# Integration tests
docker compose run --rm test-integration

# Both (via Dockerfile multi-stage)
docker build --target test-integration -t backend-tests ./backend
docker run --rm backend-tests
```

---

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `Dapper` | Micro-ORM for PostgreSQL queries |
| `Npgsql` | PostgreSQL ADO.NET provider |
| `BCrypt.Net-Next` | Password hashing and verification |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT token validation |
| `UglyToad.PdfPig` | PDF text extraction |
| `nClam` | ClamAV virus scan client |
| `Serilog.AspNetCore` | Structured logging (console + rolling file, 14-day retention) |
| `Scalar.AspNetCore` | OpenAPI reference UI (dev only) |

---

## Configuration

The API is configured via environment variables (passed through Docker Compose from `.env`):

```env
# Database
ConnectionStrings__DefaultConnection=Host=db;Database=bankdb;Username=user;Password=password

# JWT
Jwt__Issuer=Statements.WebAPI
Jwt__Audience=Statements.Client
Jwt__Secret=<min-32-char-key>
Jwt__AccessTokenMinutes=15
Jwt__RefreshTokenDays=30

# File storage
FileStorage__UploadsDirectory=/uploads

# ClamAV
ClamAv__Host=clamav
ClamAv__Port=3310

# External auth (optional)
ExternalProviders__Auth0__Authority=https://dev-bankstatements.au.auth0.com
ExternalProviders__Auth0__ClientId=...
ExternalProviders__Auth0__Audience=...
ExternalProviders__Google__ClientId=...
ExternalProviders__Google__ClientSecret=...
```

### appsettings.json

Non-sensitive defaults live in `appsettings.json` — notably the Serilog logging configuration and ClamAV timeout settings.

---

## Authentication Flow

### Local auth
```
Register → BCrypt hash password → Store user
Login → Verify password → Check lockout → Generate JWT (15min) + Refresh token (30d)
Refresh → Validate refresh token → Rotate (revoke old, issue new pair)
```

### OAuth (Google / Auth0)
```
ID Token flow:  Frontend sends provider JWT → Backend validates signature → Create/link account
Code (PKCE):    Frontend exchanges code with provider → Sends code to backend → Backend validates with provider → Create/link account
```

- **Email auto-linking**: If an OAuth provider email matches an existing local account, the accounts are linked
- **Fallback email**: Users without a provider email get `{provider}:{provider_key}@noemail.local`
