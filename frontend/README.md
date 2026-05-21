# BankStatements — Frontend

The frontend is a React 19 single-page application (SPA) built with TypeScript and Vite. It provides the UI for uploading bank statement PDFs, viewing and editing parsed transactions, tracking budgets, and analysing spending. Real-time updates use SignalR with polling fallback.

---

## Tech Stack

| Technology | Version |
|------------|---------|
| React | 19.2 |
| TypeScript | 6.0 |
| Vite | 8.0 |
| Axios | 1.16 |
| SignalR | 8.0 |
| ESLint | 10.3 |

---

## Setup

### Via Docker (recommended)

```bash
# From repo root — starts all services
docker compose up --build

# Frontend-only
docker compose up --build frontend
```

The frontend is served at **http://localhost:3000**.

### Standalone (for quick UI work)

```bash
cd frontend
npm install
npm run dev
```

---

## Available Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start Vite dev server with HMR |
| `npm run build` | TypeScript check + production build |
| `npm run lint` | Run ESLint across all source files |

---

## Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `VITE_API_BASE_URL` | `http://localhost:5213` | Backend API base URL |
| `VITE_GOOGLE_CLIENT_ID` | — | Google OAuth client ID |
| `VITE_AUTH0_DOMAIN` | — | Auth0 tenant domain |
| `VITE_AUTH0_CLIENT_ID` | — | Auth0 application client ID |

Proxy config in `vite.config.ts` forwards `/api` and `/hubs` (SignalR WebSocket) to the backend.

---

## Project Structure

```
src/
├── types.ts                         # Shared TypeScript types and constants
├── App.tsx                          # Main component — auth, routing, state management
├── App.css                          # Application styles
├── components/
│   ├── AccountToolbar.tsx           # Bank account selector, rename, delete
│   ├── AuthPanel.tsx                # Login/register form
│   ├── BudgetBar.tsx                # Budget progress bar (ok/warning/over)
│   ├── BudgetManager.tsx            # Monthly budget management UI
│   ├── ExternalLoginButtons.tsx     # Google/Auth0 OAuth buttons
│   ├── MetricStrip.tsx              # Total credit/debit/cashflow metrics
│   ├── RecentActivity.tsx           # Recent transactions with inline editing
│   ├── SpendingBreakdown.tsx        # Category breakdown with budget bars
│   ├── StatementManager.tsx         # Statement history with status badges and retry
│   ├── TransactionRow.tsx           # Editable transaction row (double-click to edit)
│   └── UploadPanel.tsx              # PDF upload form with live status
├── hooks/
│   └── useStatementHub.ts           # SignalR connection hook with reconnect
└── services/
    └── externalAuth.ts              # API client (Axios)
```

---

## Key Features

### Statement Upload
- Upload PDFs via the upload panel — status updates in real-time via SignalR
- Falls back to 2-second polling if SignalR connection fails

### Statement History
- See all uploaded statements with status badges (uploaded/processing/processed/failed)
- Retry failed statements with one click

### Transaction Editing
- Double-click any transaction to edit its description and category inline
- Changes are saved immediately via the API

### CSV Export
- Download the current transaction view as a CSV file
- Filters (bank account, date range) are respected in the export

### Budget Tracking
- Set monthly budgets per category using the budget manager
- Progress bars in the spending breakdown show ok/warning/over status
- Budgets update in real-time as new statements are processed

### Spending Analysis
- Category breakdown with transaction counts
- Total credit, total debit, and net cashflow metrics
- Recent transactions list

### Auth
- Local email/password authentication
- Google and Auth0 OAuth via PKCE popup flow
- Access tokens stored in memory only (never localStorage)
- Automatic 401 retry with token refresh

---

## API Client

The app uses two fetch strategies:

1. **`authedFetch`** — Native `fetch` wrapper with Bearer token, used for all authenticated data endpoints (accounts, statements, analysis, budgets, transactions, export).
2. **Axios** — Used for auth endpoints (login, register, OAuth, refresh, logout) that rely on httpOnly cookies.

Both automatically retry on 401 by refreshing the access token.
