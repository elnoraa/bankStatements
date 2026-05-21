-- =============================================================================
-- Migration 006: Add columns for background processing support
-- =============================================================================
-- Adds 'processing' status tracking, failure details, and a CHECK constraint
-- to bank_statements for the async message-queue processing flow.
-- =============================================================================

ALTER TABLE bank_statements
    ADD COLUMN IF NOT EXISTS failed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS error_message TEXT;

-- Drop any existing constraint before recreating to handle idempotent re-runs
ALTER TABLE bank_statements
    DROP CONSTRAINT IF EXISTS bank_statements_status_check;

ALTER TABLE bank_statements
    ADD CONSTRAINT bank_statements_status_check
        CHECK (status IN ('uploaded', 'processing', 'processed', 'failed'));
