-- =============================================================================
-- Migration 014: Add user_category_rules table for persisting user recategorizations
-- =============================================================================
-- This table stores per-user description-to-category mappings. When a user
-- bulk-applies a category via "Apply to all", the rule is saved here and
-- applied to future statement imports.
-- =============================================================================

CREATE TABLE IF NOT EXISTS user_category_rules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    description TEXT NOT NULL,
    category_id UUID NOT NULL REFERENCES transaction_categories(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, description)
);

CREATE INDEX IF NOT EXISTS idx_category_rules_user_desc
    ON user_category_rules(user_id, LOWER(description));
