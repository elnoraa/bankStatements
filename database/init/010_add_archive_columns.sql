ALTER TABLE bank_statements
    ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;

ALTER TABLE bank_statements
    DROP CONSTRAINT IF EXISTS bank_statements_status_check;

ALTER TABLE bank_statements
    ADD CONSTRAINT bank_statements_status_check
        CHECK (status IN ('uploaded', 'processing', 'processed', 'failed', 'archived'));
