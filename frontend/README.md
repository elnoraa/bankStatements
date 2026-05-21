# BankStatements — Frontend

The frontend is a React 19 single-page application (SPA) built with TypeScript and Vite. It provides the UI for uploading bank statement PDFs, viewing parsed transactions, and analysing spending. It communicates with the backend API via Axios.

---

## Tech Stack

| Technology | Version |
|------------|---------|
| React | 19.2 |
| TypeScript | 6.0 |
| Vite | 8.0 |
| Axios | 1.16 |
| ESLint | 10.3 |

---

## Setup

### Via Docker (recommended)

The frontend runs inside a Docker container as part of the full stack:

```bash
# From repo root — starts all services
docker compose up --build

# Frontend-only (if backend is already running)
docker compose up --build frontend
```

The frontend is served at **http://localhost:3000**.

### Standalone (for quick UI work)

```bash
cd frontend
npm install
npm run dev
```

> ⚠️ The standalone dev server expects the backend API at the URL specified by `VITE_API_BASE_URL`. See [Configuration](#configuration) below.

---

## Available Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start Vite dev server with HMR |
| `npm run build` | TypeScript check + production build |
| `npm run lint` | Run ESLint across all source files |
| `npm run preview` | Preview the production build |

---

## Configuration

The frontend is configured via environment variables at build time. In Docker, these are passed through from `.env`.

| Variable | Default | Description |
|----------|---------|-------------|
| `VITE_API_BASE_URL` | `http://localhost:5213` | Backend API base URL |
| `VITE_GOOGLE_CLIENT_ID` | — | Google OAuth client ID for social login |
| `VITE_AUTH0_DOMAIN` | — | Auth0 tenant domain |
| `VITE_AUTH0_CLIENT_ID` | — | Auth0 application client ID |

### OAuth Configuration

To enable social login with Google or Auth0:

1. Set the relevant `VITE_*` variables in `.env` (at the repo root)
2. Ensure the backend has matching `ExternalProviders__*` variables configured
3. For Google: add `http://localhost:3000` to your Google Cloud Console authorised JavaScript origins
4. For Auth0: add `http://localhost:3000` to your Auth0 application's allowed callback URLs

---

## Key Files

| File | Purpose |
|------|---------|
| `src/App.tsx` | Main application component — manages auth state, file upload, and spending summary display |
| `src/components/ExternalLoginButtons.tsx` | Google and Auth0 sign-in buttons with modal OAuth flow |
| `src/services/externalAuth.ts` | Axios-based API client — login, register, OAuth, token refresh, statement upload, analysis fetch |
| `src/App.css` | Application styles |
| `src/index.css` | Global styles and resets |

### Auth State Management

The app manages authentication entirely in-memory:

- **Access token** — Stored in a React `useRef` (never persisted to storage or cookies)
- **Refresh token** — Handled via an httpOnly `SameSite=Strict` cookie (inaccessible to JavaScript)
- **Auto-refresh** — On page load, the app calls `refreshAuthToken()` using the httpOnly cookie to obtain a new access token
- **401 handling** — If an API call returns 401, the `authedFetch` helper attempts a token refresh and retries the request

---

## API Client

All API calls go through Axios with `withCredentials: true` for cookie-based refresh token support. The client in `src/services/externalAuth.ts` provides:

- `postLogin()` / `postRegister()` — Local auth
- `postExternalLogin()` / `postExternalCode()` — OAuth flows
- `refreshAuthToken()` — Refresh access token via cookie
- `logout()` — Revoke refresh token and clear session
- `uploadStatement()` — Upload PDF (multipart/form-data with auth header)
- `getAnalysisSummary()` — Fetch spending analysis with optional filters
