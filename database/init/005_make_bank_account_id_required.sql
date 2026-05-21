-- =============================================================================
-- Migration 005: Make bank_account_id required with CASCADE delete
-- =============================================================================
-- Since all statements must belong to an account, this migration makes
-- bank_account_id NOT NULL and cascades deletes so removing an account
-- removes its statements and their transactions.
-- =============================================================================

-- Remove any orphan records that may have been created before this constraint
DELETE FROM statement_transactions WHERE bank_account_id IS NULL;
DELETE FROM bank_statements WHERE bank_account_id IS NULL;

-- Drop existing foreign key constraints
ALTER TABLE bank_statements
    DROP CONSTRAINT IF EXISTS bank_statements_bank_account_id_fkey;

ALTER TABLE statement_transactions
    DROP CONSTRAINT IF EXISTS statement_transactions_bank_account_id_fkey;

-- Re-create with NOT NULL and CASCADE on bank_statements
ALTER TABLE bank_statements
    ALTER COLUMN bank_account_id SET NOT NULL,
    ADD CONSTRAINT bank_statements_bank_account_id_fkey
        FOREIGN KEY (bank_account_id)
        REFERENCES bank_accounts(id)
        ON DELETE CASCADE;

-- Re-create on statement_transactions with CASCADE (rows cascade through bank_statements,
-- but this extra FK ensures referential integrity if transactions reference accounts directly)
ALTER TABLE statement_transactions
    ADD CONSTRAINT statement_transactions_bank_account_id_fkey
        FOREIGN KEY (bank_account_id)
        REFERENCES bank_accounts(id)
        ON DELETE CASCADE;
