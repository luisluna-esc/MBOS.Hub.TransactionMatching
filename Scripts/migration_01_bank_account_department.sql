-- Migración 01: departamento (unidad o colegio) de cada cuenta del banco.
-- Para bases creadas antes de este cambio; después correr seedData.sql (carga las 13 cuentas).
ALTER TABLE bank_account ADD COLUMN IF NOT EXISTS department VARCHAR(100);
