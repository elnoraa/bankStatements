-- =============================================================================
-- Migration 007: Create budgets table for monthly budget tracking per category
-- =============================================================================

CREATE TABLE budgets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    category_id UUID NOT NULL REFERENCES transaction_categories(id) ON DELETE CASCADE,
    month_year DATE NOT NULL,  -- stored as first day of month, e.g. '2026-05-01'
    amount NUMERIC(14, 2) NOT NULL CHECK (amount > 0),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, category_id, month_year)
);

CREATE INDEX idx_budgets_user_month ON budgets(user_id, month_year);
CREATE INDEX idx_budgets_category ON budgets(category_id);
