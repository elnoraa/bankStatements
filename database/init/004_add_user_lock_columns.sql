-- =============================================================================
-- Migration 004: Add account lock tracking columns to app_users
-- =============================================================================
-- This migration adds columns for tracking failed login attempts and temporary
-- account lockout.
--
-- New columns:
--   failed_login_attempts  INT          NOT NULL DEFAULT 0
--   locked_until           TIMESTAMPTZ  when the account lock expires
--
-- Run against existing databases that were created before this feature.
-- =============================================================================

ALTER TABLE app_users
    ADD COLUMN IF NOT EXISTS failed_login_attempts INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ;
