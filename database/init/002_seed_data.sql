INSERT INTO transaction_categories (name, transaction_type) VALUES
    ('Salary', 'credit'),
    ('Refunds', 'credit'),
    ('Interest', 'credit'),
    ('Transfers In', 'credit'),
    ('Groceries', 'debit'),
    ('Dining', 'debit'),
    ('Transport', 'debit'),
    ('Rent', 'debit'),
    ('Utilities', 'debit'),
    ('Entertainment', 'debit'),
    ('Shopping', 'debit'),
    ('Health', 'debit'),
    ('Insurance', 'debit'),
    ('Fees', 'debit'),
    ('Transfers Out', 'debit'),
    ('Uncategorised', 'both')
ON CONFLICT (name) DO NOTHING;

INSERT INTO app_users (id, email, display_name, password_hash, email_verified) VALUES
    (
        '11111111-1111-1111-1111-111111111111',
        'demo@example.com',
        'Demo User',
        '$2a$11$7EqJtq98hPqEX7fNZaFWoOhiHLnT7g4YPuM7iU4US3VnFQCV1Lq6S',
        TRUE
    )
ON CONFLICT (email) DO NOTHING;

INSERT INTO bank_accounts (id, user_id, bank_name, account_name, account_mask, currency) VALUES
    ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'Demo Bank', 'Everyday Account', '1234', 'AUD')
ON CONFLICT (id) DO NOTHING;

INSERT INTO bank_statements (
    id,
    user_id,
    bank_account_id,
    original_file_name,
    stored_file_name,
    content_type,
    size_in_bytes,
    statement_start_date,
    statement_end_date,
    status,
    processed_at
) VALUES (
    '33333333-3333-3333-3333-333333333333',
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222',
    'demo-statement.csv',
    'demo-statement-333333333333.csv',
    'text/csv',
    1024,
    '2026-04-01',
    '2026-04-30',
    'processed',
    NOW()
) ON CONFLICT (stored_file_name) DO NOTHING;

INSERT INTO statement_transactions (
    bank_statement_id,
    bank_account_id,
    category_id,
    transaction_date,
    description,
    merchant_name,
    amount,
    transaction_type,
    balance_after,
    external_reference
)
SELECT
    '33333333-3333-3333-3333-333333333333',
    '22222222-2222-2222-2222-222222222222',
    c.id,
    v.transaction_date,
    v.description,
    v.merchant_name,
    v.amount,
    v.transaction_type,
    v.balance_after,
    v.external_reference
FROM (
    VALUES
        ('2026-04-01'::date, 'Salary payment', 'Employer Pty Ltd', 4200.00::numeric, 'credit', 5200.00::numeric, 'demo-001', 'Salary'),
        ('2026-04-03'::date, 'Weekly groceries', 'Fresh Market', 156.45::numeric, 'debit', 5043.55::numeric, 'demo-002', 'Groceries'),
        ('2026-04-05'::date, 'Train top up', 'Transport NSW', 50.00::numeric, 'debit', 4993.55::numeric, 'demo-003', 'Transport'),
        ('2026-04-07'::date, 'Rent payment', 'Property Manager', 2100.00::numeric, 'debit', 2893.55::numeric, 'demo-004', 'Rent'),
        ('2026-04-12'::date, 'Restaurant dinner', 'Harbour Eats', 88.90::numeric, 'debit', 2804.65::numeric, 'demo-005', 'Dining'),
        ('2026-04-20'::date, 'Utility bill', 'Energy Provider', 185.30::numeric, 'debit', 2619.35::numeric, 'demo-006', 'Utilities'),
        ('2026-04-25'::date, 'Purchase refund', 'Online Store', 64.99::numeric, 'credit', 2684.34::numeric, 'demo-007', 'Refunds')
) AS v(transaction_date, description, merchant_name, amount, transaction_type, balance_after, external_reference, category_name)
JOIN transaction_categories c ON c.name = v.category_name
WHERE NOT EXISTS (
    SELECT 1
    FROM statement_transactions t
    WHERE t.external_reference = v.external_reference
);

INSERT INTO analysis_runs (
    user_id,
    bank_account_id,
    period_start,
    period_end,
    total_credit,
    total_debit,
    net_cashflow,
    is_cashflow_positive,
    summary
) VALUES (
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222',
    '2026-04-01',
    '2026-04-30',
    4264.99,
    2580.65,
    1684.34,
    TRUE,
    '{"topDebitCategory":"Rent","transactionCount":7}'::jsonb
);
