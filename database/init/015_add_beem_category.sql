-- Add "Beem" category for Beem It transfer transactions (typically debits).
INSERT INTO transaction_categories (name, transaction_type) VALUES
    ('Beem', 'debit')
ON CONFLICT (name) DO NOTHING;
