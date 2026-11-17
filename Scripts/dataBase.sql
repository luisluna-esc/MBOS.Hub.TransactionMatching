-- =====================================================================
-- TRANSACTIONMATCHING - CONSOLIDATED POSTGRESQL SCRIPT
-- Base común de MBOS Hub: seguridad (usuarios, roles, menús, sesiones) y
-- auditoría. Las tablas del negocio se agregan al final, por niveles.
-- Creation order respects Foreign Key dependencies.
-- =====================================================================

-- =====================================================================
-- LEVEL 1: TABLES WITHOUT DEPENDENCIES
-- =====================================================================

CREATE TABLE role (
    role_id BIGSERIAL PRIMARY KEY,
    name VARCHAR(50) NOT NULL,
    description VARCHAR(255),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_role_name UNIQUE (name)
);

CREATE TABLE menu (
    menu_id BIGSERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    translation_key VARCHAR(100),
    route VARCHAR(150),
    icon VARCHAR(50),
    parent_menu_id BIGINT,
    display_order INT NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT fk_menu_parent FOREIGN KEY (parent_menu_id) REFERENCES menu(menu_id),
    CONSTRAINT uq_menu_route UNIQUE (route)
);

CREATE INDEX idx_menu_parent ON menu(parent_menu_id);

-- =====================================================================
-- LEVEL 2: depend on level 1
-- =====================================================================

CREATE TABLE app_user (
    app_user_id BIGSERIAL PRIMARY KEY,
    ci VARCHAR(20),
    password_hash VARCHAR(255) NOT NULL,
    full_name VARCHAR(200) NOT NULL,
    -- Con lo que se entra al sistema: nombre.apellido (minúsculas, sin tildes).
    username VARCHAR(100) NOT NULL,
    phone VARCHAR(30),
    password_changed_at TIMESTAMP,
    failed_login_attempts INT NOT NULL DEFAULT 0,
    locked_until TIMESTAMP,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    last_login TIMESTAMP,
    created_by BIGINT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_app_user_username UNIQUE (username),
    CONSTRAINT fk_app_user_created_by FOREIGN KEY (created_by) REFERENCES app_user(app_user_id)
);

-- =====================================================================
-- LEVEL 3: depend on app_user, menu, role
-- =====================================================================

-- Solo se guarda el SHA-256 del refresh token (64 caracteres hex): el token en claro lo tiene el cliente.
CREATE TABLE refresh_token (
    refresh_token_id BIGSERIAL PRIMARY KEY,
    app_user_id BIGINT NOT NULL,
    token_hash VARCHAR(64) NOT NULL,
    expires_at TIMESTAMP NOT NULL,
    is_revoked BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    created_ip VARCHAR(50),
    CONSTRAINT fk_refresh_token_app_user FOREIGN KEY (app_user_id) REFERENCES app_user(app_user_id) ON DELETE CASCADE,
    CONSTRAINT uq_refresh_token_token_hash UNIQUE (token_hash)
);

CREATE INDEX idx_refresh_token_app_user ON refresh_token(app_user_id);

CREATE TABLE password_reset (
    password_reset_id BIGSERIAL PRIMARY KEY,
    app_user_id BIGINT NOT NULL,
    token_hash VARCHAR(255) NOT NULL,
    expires_at TIMESTAMP NOT NULL,
    is_used BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_password_reset_app_user FOREIGN KEY (app_user_id) REFERENCES app_user(app_user_id) ON DELETE CASCADE,
    CONSTRAINT uq_password_reset_token_hash UNIQUE (token_hash)
);

CREATE INDEX idx_password_reset_app_user ON password_reset(app_user_id);

CREATE TABLE app_user_role (
    app_user_id BIGINT NOT NULL,
    role_id BIGINT NOT NULL,
    assigned_by BIGINT,
    assigned_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_app_user_role PRIMARY KEY (app_user_id, role_id),
    CONSTRAINT fk_app_user_role_app_user FOREIGN KEY (app_user_id) REFERENCES app_user(app_user_id) ON DELETE CASCADE,
    CONSTRAINT fk_app_user_role_role FOREIGN KEY (role_id) REFERENCES role(role_id) ON DELETE CASCADE,
    CONSTRAINT fk_app_user_role_assigned_by FOREIGN KEY (assigned_by) REFERENCES app_user(app_user_id)
);

CREATE INDEX idx_app_user_role_role ON app_user_role(role_id);

-- A sidebar menu item is visible to a role only if there is a row here for
-- that (menu, role) pair. A menu with no rows is not visible to anyone.
CREATE TABLE menu_role (
    menu_id BIGINT NOT NULL,
    role_id BIGINT NOT NULL,
    CONSTRAINT pk_menu_role PRIMARY KEY (menu_id, role_id),
    CONSTRAINT fk_menu_role_menu FOREIGN KEY (menu_id) REFERENCES menu(menu_id) ON DELETE CASCADE,
    CONSTRAINT fk_menu_role_role FOREIGN KEY (role_id) REFERENCES role(role_id) ON DELETE CASCADE
);

CREATE INDEX idx_menu_role_role ON menu_role(role_id);

-- record_id has no FK on purpose: it points to a row of whichever table is
-- named in table_name.
CREATE TABLE audit_log (
    audit_log_id BIGSERIAL PRIMARY KEY,
    app_user_id BIGINT,
    action VARCHAR(20) NOT NULL,
    module VARCHAR(100),
    table_name VARCHAR(100) NOT NULL,
    record_id BIGINT,
    description VARCHAR(500),
    old_value JSONB,
    new_value JSONB,
    ip VARCHAR(50),
    user_agent VARCHAR(500),
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_audit_log_app_user FOREIGN KEY (app_user_id) REFERENCES app_user(app_user_id)
);

CREATE INDEX idx_audit_log_app_user ON audit_log(app_user_id);
CREATE INDEX idx_audit_log_table_record ON audit_log(table_name, record_id);


-- =====================================================================

-- Cuentas del banco. Se crean solas al subir el primer extracto de cada cuenta.
CREATE TABLE bank_account (
    bank_account_id BIGSERIAL PRIMARY KEY,
    bank VARCHAR(100) NOT NULL,
    account_number VARCHAR(30) NOT NULL,
    name VARCHAR(100) NOT NULL,
    department VARCHAR(100),
    ledger_subaccount VARCHAR(150),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_bank_account_number UNIQUE (account_number)
);

-- Una conciliación por cuenta y período (el extracto y el Mayor de ese mes).
CREATE TABLE reconciliation (
    reconciliation_id BIGSERIAL PRIMARY KEY,
    bank_account_id BIGINT NOT NULL,
    period_start DATE NOT NULL,
    period_end DATE NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'Open',
    -- Qué se concilia: 'Income' (ingresos), 'Expense' (egresos) o 'All' (los dos).
    scope VARCHAR(10) NOT NULL DEFAULT 'All',
    bank_file_name VARCHAR(255) NOT NULL,
    ledger_file_name VARCHAR(255) NOT NULL,
    bank_opening_balance NUMERIC(14,2),
    bank_closing_balance NUMERIC(14,2),
    ledger_opening_balance NUMERIC(14,2),
    ledger_closing_balance NUMERIC(14,2),
    notes VARCHAR(1000),
    created_by BIGINT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    closed_by BIGINT,
    closed_at TIMESTAMP,
    CONSTRAINT fk_reconciliation_bank_account FOREIGN KEY (bank_account_id) REFERENCES bank_account(bank_account_id),
    CONSTRAINT fk_reconciliation_created_by FOREIGN KEY (created_by) REFERENCES app_user(app_user_id),
    CONSTRAINT fk_reconciliation_closed_by FOREIGN KEY (closed_by) REFERENCES app_user(app_user_id),
    CONSTRAINT uq_reconciliation_period UNIQUE (bank_account_id, period_start, scope),
    CONSTRAINT ck_reconciliation_status CHECK (status IN ('Open', 'Closed')),
    CONSTRAINT ck_reconciliation_scope CHECK (scope IN ('All', 'Income', 'Expense'))
);

-- Filas del extracto del banco, con todas sus columnas. credit = ingreso, debit = egreso.
CREATE TABLE bank_movement (
    bank_movement_id BIGSERIAL PRIMARY KEY,
    reconciliation_id BIGINT NOT NULL,
    line_number INT NOT NULL,
    movement_date DATE NOT NULL,
    movement_time TIME,
    bank_code VARCHAR(30),
    cheque_number VARCHAR(30),
    template_name VARCHAR(100),
    deposit_code VARCHAR(50),
    depositor_document VARCHAR(30),
    depositor_name VARCHAR(200),
    transaction_type VARCHAR(100),
    description VARCHAR(500),
    office VARCHAR(100),
    bank_name VARCHAR(100),
    deposit_type VARCHAR(100),
    recipient_name VARCHAR(200),
    glosa VARCHAR(500),
    originator VARCHAR(200),
    originator_ach VARCHAR(200),
    city VARCHAR(50),
    debit NUMERIC(14,2) NOT NULL DEFAULT 0,
    credit NUMERIC(14,2) NOT NULL DEFAULT 0,
    balance NUMERIC(14,2),
    note VARCHAR(500),
    CONSTRAINT fk_bank_movement_reconciliation FOREIGN KEY (reconciliation_id) REFERENCES reconciliation(reconciliation_id) ON DELETE CASCADE
);

CREATE INDEX idx_bank_movement_reconciliation ON bank_movement(reconciliation_id);

-- Asientos del Mayor de ASSINET. debit = ingreso al banco, credit = egreso. La descripción
-- ("Transferencia 06/10/2026 Lopez Paula") se guarda también partida para el cotejo.
CREATE TABLE ledger_entry (
    ledger_entry_id BIGSERIAL PRIMARY KEY,
    reconciliation_id BIGINT NOT NULL,
    line_number INT NOT NULL,
    accounting_date DATE NOT NULL,
    batch VARCHAR(50),
    description VARCHAR(500) NOT NULL,
    reference VARCHAR(100),
    debit NUMERIC(14,2) NOT NULL DEFAULT 0,
    credit NUMERIC(14,2) NOT NULL DEFAULT 0,
    movement_type VARCHAR(50),
    movement_date DATE,
    counterparty_name VARCHAR(300),
    note VARCHAR(500),
    CONSTRAINT fk_ledger_entry_reconciliation FOREIGN KEY (reconciliation_id) REFERENCES reconciliation(reconciliation_id) ON DELETE CASCADE
);

CREATE INDEX idx_ledger_entry_reconciliation ON ledger_entry(reconciliation_id);

-- Cotejo uno a uno entre una fila del banco y una del Mayor.
CREATE TABLE reconciliation_match (
    reconciliation_match_id BIGSERIAL PRIMARY KEY,
    reconciliation_id BIGINT NOT NULL,
    bank_movement_id BIGINT NOT NULL,
    ledger_entry_id BIGINT NOT NULL,
    score INT NOT NULL DEFAULT 0,
    status VARCHAR(20) NOT NULL,
    note VARCHAR(500),
    created_by BIGINT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_match_reconciliation FOREIGN KEY (reconciliation_id) REFERENCES reconciliation(reconciliation_id) ON DELETE CASCADE,
    CONSTRAINT fk_match_bank_movement FOREIGN KEY (bank_movement_id) REFERENCES bank_movement(bank_movement_id) ON DELETE CASCADE,
    CONSTRAINT fk_match_ledger_entry FOREIGN KEY (ledger_entry_id) REFERENCES ledger_entry(ledger_entry_id) ON DELETE CASCADE,
    CONSTRAINT fk_match_created_by FOREIGN KEY (created_by) REFERENCES app_user(app_user_id),
    CONSTRAINT uq_match_bank_movement UNIQUE (bank_movement_id),
    CONSTRAINT uq_match_ledger_entry UNIQUE (ledger_entry_id),
    CONSTRAINT ck_match_status CHECK (status IN ('Auto', 'Suggested', 'Confirmed', 'Manual'))
);

CREATE INDEX idx_match_reconciliation ON reconciliation_match(reconciliation_id);
