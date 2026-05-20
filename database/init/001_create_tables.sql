-- =============================================================================
-- Migration 001: Create core database tables
-- =============================================================================
-- This is the initial schema migration that creates all core tables for the
-- bank statement management system.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Registered application users (both local auth and externally-linked accounts).
CREATE TABLE app_users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(320) NOT NULL UNIQUE,
    display_name VARCHAR(120) NOT NULL,
    password_hash TEXT,
    email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    failed_login_attempts INT NOT NULL DEFAULT 0,
    locked_until TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Refresh tokens for JWT token rotation. Each user can have multiple active tokens.
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    token_hash TEXT NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Bank accounts associated with users for categorising statements.
CREATE TABLE bank_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    bank_name VARCHAR(120) NOT NULL,
    account_name VARCHAR(120) NOT NULL,
    account_mask VARCHAR(20),
    currency CHAR(3) NOT NULL DEFAULT 'AUD',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Uploaded bank statement files with metadata and processing status.
CREATE TABLE bank_statements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    bank_account_id UUID REFERENCES bank_accounts(id) ON DELETE SET NULL,
    original_file_name VARCHAR(255) NOT NULL,
    stored_file_name VARCHAR(255) NOT NULL UNIQUE,
    file_hash VARCHAR(64) NOT NULL,
    content_type VARCHAR(120),
    size_in_bytes BIGINT NOT NULL CHECK (size_in_bytes >= 0),
    statement_start_date DATE,
    statement_end_date DATE,
    status VARCHAR(30) NOT NULL DEFAULT 'uploaded',
    scan_status VARCHAR(20) NOT NULL DEFAULT 'pending',
    scanned_at TIMESTAMPTZ,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ,
    CHECK (statement_start_date IS NULL OR statement_end_date IS NULL OR statement_start_date <= statement_end_date),
    CHECK (scan_status IN ('pending', 'clean', 'infected', 'error'))
);

-- Predefined transaction categories for classifying spending and income.
CREATE TABLE transaction_categories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(80) NOT NULL UNIQUE,
    transaction_type VARCHAR(10) NOT NULL CHECK (transaction_type IN ('credit', 'debit', 'both')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Individual transactions parsed from uploaded bank statements.
CREATE TABLE statement_transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    bank_statement_id UUID NOT NULL REFERENCES bank_statements(id) ON DELETE CASCADE,
    bank_account_id UUID REFERENCES bank_accounts(id) ON DELETE SET NULL,
    category_id UUID REFERENCES transaction_categories(id) ON DELETE SET NULL,
    transaction_date DATE NOT NULL,
    description TEXT NOT NULL,
    merchant_name VARCHAR(160),
    amount NUMERIC(14, 2) NOT NULL,
    transaction_type VARCHAR(10) NOT NULL CHECK (transaction_type IN ('credit', 'debit')),
    balance_after NUMERIC(14, 2),
    external_reference VARCHAR(160),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Cached spending analysis results for quick retrieval.
CREATE TABLE analysis_runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    bank_account_id UUID REFERENCES bank_accounts(id) ON DELETE SET NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    total_credit NUMERIC(14, 2) NOT NULL DEFAULT 0,
    total_debit NUMERIC(14, 2) NOT NULL DEFAULT 0,
    net_cashflow NUMERIC(14, 2) NOT NULL DEFAULT 0,
    is_cashflow_positive BOOLEAN NOT NULL DEFAULT FALSE,
    summary JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (period_start <= period_end)
);

CREATE INDEX idx_bank_accounts_user_id ON bank_accounts(user_id);
CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens(user_id);
CREATE INDEX idx_refresh_tokens_expires_at ON refresh_tokens(expires_at);
CREATE INDEX idx_bank_statements_user_id ON bank_statements(user_id);
CREATE INDEX idx_bank_statements_account_id ON bank_statements(bank_account_id);
CREATE UNIQUE INDEX idx_bank_statements_user_file_hash ON bank_statements(user_id, file_hash);
CREATE INDEX idx_statement_transactions_statement_id ON statement_transactions(bank_statement_id);
CREATE INDEX idx_statement_transactions_account_date ON statement_transactions(bank_account_id, transaction_date);
CREATE INDEX idx_statement_transactions_category_id ON statement_transactions(category_id);
CREATE INDEX idx_analysis_runs_user_period ON analysis_runs(user_id, period_start, period_end);

CREATE TABLE external_logins (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    provider VARCHAR(100) NOT NULL,
    provider_key VARCHAR(200) NOT NULL,
    display_name VARCHAR(200),
    email VARCHAR(320),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(provider, provider_key)
);

CREATE INDEX idx_external_logins_user_id ON external_logins(user_id);
