# BankStatements — Backend API

The backend is an ASP.NET Core 10 Web API that handles user authentication, PDF statement uploads, transaction parsing, and spending analysis against a PostgreSQL database. Background processing uses RabbitMQ, and real-time status updates use SignalR.

---

## Project Structure

```
backend/
├── Statements.WebAPI/               # Main API project
│   ├── Auth/                        # Authentication primitives
│   ├── Contracts/                   # Request/response DTOs
│   │   ├── Auth/                    # Login, Register, ExternalLogin, Refresh DTOs
│   │   ├── Analysis/                # Spending summary, categories, budgets, transactions
│   │   ├── Messages/                # RabbitMQ message contracts
│   │   └── Statements/              # Statement upload/list/retry responses
│   ├── Controllers/                 # API endpoint controllers
│   │   ├── v1/AuthController.cs        # /api/v1/auth/*
│   │   ├── v1/StatementsController.cs  # /api/v1/statements/* (upload, list, retry, status)
│   │   ├── v1/AnalysisController.cs    # /api/v1/analysis/* (summary, categories, export)
│   │   ├── v1/TransactionsController.cs # /api/v1/transactions/* (edit)
│   │   ├── v1/BudgetsController.cs     # /api/v1/budgets/* (CRUD + progress)
│   │   ├── v1/BankAccountsController.cs # /api/v1/bank-accounts/*
│   │   └── HealthController.cs      # /api/health
│   ├── Data/                        # Data access layer (Dapper + Npgsql)
│   ├── Hubs/                        # SignalR hubs
│   │   └── StatementProcessingHub.cs  # Real-time status updates at /hubs/statement-processing
│   ├── Infrastructure/              # Dapper type handlers
│   ├── Services/                    # Business logic
│   │   ├── Auth/                    # Register, login, token refresh, OAuth
│   │   ├── Analysis/                # Spending aggregation, budgets, transaction editing
│   │   ├── BankAccounts/            # Bank account CRUD
│   │   ├── Export/                  # CSV export
│   │   ├── Messaging/               # RabbitMQ publisher + background consumer
│   │   └── Statements/              # Upload, virus scan, PDF parsing, background processing
│   ├── Program.cs                   # Entry point, DI, middleware
│   └── appsettings*.json
├── Statements.WebAPI.Tests/         # xUnit test project (210+ tests)
│   ├── Contracts/                   # Request validation tests
│   ├── Controllers/                 # Controller unit tests
│   ├── Infrastructure/              # DateOnly handler tests
│   ├── IntegrationTests/            # E2E tests with Testcontainers PostgreSQL
│   └── Services/                    # Service unit tests
├── Uploads/                         # Uploaded PDF storage
└── Dockerfile                       # Multi-stage build (test → test-integration → runtime)
```

---

## API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/v1/auth/register` | Register a new user |
| POST | `/api/v1/auth/login` | Login with email/password |
| POST | `/api/v1/auth/refresh` | Refresh JWT access token |
| POST | `/api/v1/auth/external` | OAuth ID token login |
| POST | `/api/v1/auth/external/code` | OAuth PKCE code exchange |
| POST | `/api/v1/auth/logout` | Revoke refresh token |
| GET | `/api/v1/bank-accounts` | List user's bank accounts |
| POST | `/api/v1/bank-accounts` | Create a bank account |
| PUT | `/api/v1/bank-accounts/{id}` | Rename a bank account |
| DELETE | `/api/v1/bank-accounts/{id}` | Delete a bank account (cascades to statements) |
| POST | `/api/v1/statements/upload` | Upload a PDF statement |
| GET | `/api/v1/statements` | List all statements (paginated) |
| GET | `/api/v1/statements/{id}` | Get statement status (for polling) |
| POST | `/api/v1/statements/{id}/retry` | Retry a failed statement |
| GET | `/api/v1/analysis/summary` | Spending summary with category breakdown |
| GET | `/api/v1/analysis/categories` | List available transaction categories |
| GET | `/api/v1/analysis/export` | Download transactions as CSV |
| PUT | `/api/v1/transactions/{id}` | Edit a transaction (description, category) |
| GET | `/api/v1/budgets` | List monthly budgets |
| POST | `/api/v1/budgets` | Create or update a budget |
| DELETE | `/api/v1/budgets/{id}` | Delete a budget |
| GET | `/api/v1/budgets/progress` | Budget vs actual spending |
| GET | `/api/health` | Health check |

### WebSocket (SignalR)

| Hub | Endpoint | Description |
|-----|----------|-------------|
| `StatementProcessingHub` | `/hubs/statement-processing` | Push status updates (uploaded → processing → processed/failed) |

---

## Background Processing

Statement processing uses RabbitMQ for durability and retry:

1. Upload → virus scan → insert DB row (status=`uploaded`) → publish to `process-statement` queue
2. `StatementProcessingBackgroundService` consumes the queue → parse PDF → insert transactions → update status to `processed`
3. On failure: status set to `failed`, error stored, retry available via the retry endpoint
4. Status changes are pushed to connected clients via SignalR

---

## Database Migrations

Migration files live in `database/init/` and are executed alphabetically on container startup.

| File | Description |
|------|-------------|
| `001_create_tables.sql` | Core schema — users, refresh tokens, bank accounts, statements, transactions, categories, analysis runs, external logins |
| `002_seed_data.sql` | Demo user, bank account, sample transactions, 16 categories |
| `003_add_scan_columns.sql` | Adds `scan_status` and `scanned_at` to `bank_statements` |
| `004_add_user_lock_columns.sql` | Adds `failed_login_attempts` and `locked_until` to `app_users` |
| `005_make_bank_account_id_required.sql` | Makes `bank_account_id` NOT NULL with CASCADE delete |
| `006_add_background_processing.sql` | Adds `failed_at`, `error_message`, and `processing` status to `bank_statements` |
| `007_add_budgets_table.sql` | Creates `budgets` table for monthly budget tracking |

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
- **Integration tests** — Marked with `[Trait("Category", "Integration")]`. Uses Testcontainers PostgreSQL.

### Running tests

```bash
# Unit tests only (build gate)
docker compose build test-unit
docker compose run --rm test-unit

# Integration tests
docker compose run --rm test-integration
```

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

# RabbitMQ
RabbitMq__Host=rabbitmq
RabbitMq__Username=guest
RabbitMq__Password=guest

# File storage
FileStorage__UploadsDirectory=/uploads

# ClamAV
ClamAv__Host=clamav
ClamAv__Port=3310
```

---

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `Dapper` | Micro-ORM for PostgreSQL queries |
| `Npgsql` | PostgreSQL ADO.NET provider |
| `RabbitMQ.Client` | Message queue client for async processing |
| `BCrypt.Net-Next` | Password hashing |
| `UglyToad.PdfPig` | PDF text extraction |
| `nClam` | ClamAV virus scan client |
| `Serilog.AspNetCore` | Structured logging |
| `Scalar.AspNetCore` | OpenAPI reference UI (dev only) |

---

## Authentication Flow

### Local auth
```
Register → BCrypt hash password → Store user
Login → Verify password → Check lockout → Generate JWT (15min) + Refresh token (30d)
```

### OAuth (Google / Auth0)
```
Frontend sends provider JWT → Backend validates signature → Create/link account
```
- Email auto-linking links OAuth accounts to existing local accounts by email
- Rate limited: 2 req/15min per IP for login/register
