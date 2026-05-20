-- =============================================================================
-- Migration 003: Add virus scan tracking columns to bank_statements
-- =============================================================================
-- This migration adds columns for tracking ClamAV virus scan results on
-- uploaded PDF bank statements.
--
-- New columns:
--   scan_status  VARCHAR(20)  NOT NULL DEFAULT 'pending'
--       Values: pending — scan not yet performed
--               clean   — scanned and no threats found
--               infected — malware detected
--               error   — scan failed or timed out
--   scanned_at   TIMESTAMPTZ  when the scan was performed
--
-- Run against existing databases that were created before this feature.
-- =============================================================================

ALTER TABLE bank_statements
    ADD COLUMN IF NOT EXISTS scan_status VARCHAR(20) NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS scanned_at TIMESTAMPTZ;

ALTER TABLE bank_statements
    DROP CONSTRAINT IF EXISTS bank_statements_scan_status_check;

ALTER TABLE bank_statements
    ADD CONSTRAINT bank_statements_scan_status_check
        CHECK (scan_status IN ('pending', 'clean', 'infected', 'error'));
