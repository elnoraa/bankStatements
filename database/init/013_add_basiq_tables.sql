-- =============================================================================
-- Migration 013: Add Basiq open banking integration tables
-- =============================================================================

-- Add source column to bank_statements to distinguish manual uploads from Basiq syncs
ALTER TABLE bank_statements
    ADD COLUMN IF NOT EXISTS source VARCHAR(20) NOT NULL DEFAULT 'upload';

-- Track Basiq connections per user.
-- A single Basiq connection (one set of banking credentials) can return multiple
-- accounts, so multiple rows may share the same basiq_connection_id.
CREATE TABLE IF NOT EXISTS basiq_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    bank_account_id UUID REFERENCES bank_accounts(id) ON DELETE SET NULL,
    basiq_user_id VARCHAR(100) NOT NULL,
    basiq_connection_id VARCHAR(100) NOT NULL DEFAULT '',
    institution_name VARCHAR(200) NOT NULL DEFAULT '',
    status VARCHAR(30) NOT NULL DEFAULT 'pending',
    connected_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ,
    sync_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    sync_frequency_minutes INT NOT NULL DEFAULT 1440,
    last_sync_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_basiq_connections_user_id ON basiq_connections(user_id);
CREATE INDEX IF NOT EXISTS idx_basiq_connections_basiq_uid ON basiq_connections(basiq_user_id);
CREATE INDEX IF NOT EXISTS idx_basiq_connections_status ON basiq_connections(status);

-- Partial unique index: only enforce uniqueness when bank_account_id is set
-- (pending connections have null bank_account_id and are exempt)
CREATE UNIQUE INDEX IF NOT EXISTS idx_basiq_conn_user_account
    ON basiq_connections(user_id, bank_account_id)
    WHERE bank_account_id IS NOT NULL;

-- Composite index for the background worker to efficiently find connections due for sync
CREATE INDEX IF NOT EXISTS idx_basiq_connections_sync_due
    ON basiq_connections(sync_enabled, last_sync_at, sync_frequency_minutes);

-- Track sync history for each connection
CREATE TABLE IF NOT EXISTS basiq_sync_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    basiq_connection_id UUID NOT NULL REFERENCES basiq_connections(id) ON DELETE CASCADE,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    transactions_fetched INT NOT NULL DEFAULT 0,
    transactions_inserted INT NOT NULL DEFAULT 0,
    bank_statement_id UUID REFERENCES bank_statements(id) ON DELETE SET NULL,
    error_message TEXT,
    synced_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_basiq_sync_log_conn_id ON basiq_sync_log(basiq_connection_id);
CREATE INDEX IF NOT EXISTS idx_basiq_sync_log_synced_at ON basiq_sync_log(synced_at);
