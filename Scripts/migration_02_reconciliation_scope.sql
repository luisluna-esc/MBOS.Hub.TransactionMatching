-- Migración 02: ingresos y egresos por separado. Cada conciliación dice qué parte concilia
-- ('Income', 'Expense' o 'All'); el mismo mes puede tener una de ingresos y otra de egresos.
ALTER TABLE reconciliation ADD COLUMN IF NOT EXISTS scope VARCHAR(10) NOT NULL DEFAULT 'All';

ALTER TABLE reconciliation DROP CONSTRAINT IF EXISTS uq_reconciliation_period;
ALTER TABLE reconciliation ADD CONSTRAINT uq_reconciliation_period UNIQUE (bank_account_id, period_start, scope);

ALTER TABLE reconciliation DROP CONSTRAINT IF EXISTS ck_reconciliation_scope;
ALTER TABLE reconciliation ADD CONSTRAINT ck_reconciliation_scope CHECK (scope IN ('All', 'Income', 'Expense'));
