-- =====================================================================
-- TRANSACTIONMATCHING - DATOS INICIALES
-- Roles, usuario M-BOS, menú y visibilidad del menú por rol. Se puede volver
-- a correr: cada INSERT ignora lo que ya existe.
--
-- Sin catálogo de permisos: lo que alcanza cada rol se controla con la
-- visibilidad del menú (menu_role) y, en el backend, con
-- [Authorize(Roles = ...)] usando los grupos de RoleNames.cs.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Roles. Los roles del negocio se agregan aquí y en RoleNames.cs.
-- ---------------------------------------------------------------------
INSERT INTO role (name, description, is_active) VALUES
('M-BOS', 'Superusuario: además administra usuarios, roles y menús', TRUE),
('Contabilidad', 'Importa el extracto y el Mayor, coteja y cierra conciliaciones', TRUE),
('Consulta', 'Solo consulta y descarga reportes', TRUE)
ON CONFLICT (name) DO NOTHING;

-- ---------------------------------------------------------------------
-- Cuentas del banco de ASEA-MBO (NIT 121687025): 13 cuentas en 3 bancos, cada una de un
-- departamento. El número va solo con dígitos, como lo comparan los extractos ("1-24770469" →
-- "124770469"). Si la cuenta ya existe (se creó al subir un extracto) solo se le pone el
-- departamento que le falte.
-- ---------------------------------------------------------------------
INSERT INTO bank_account (bank, account_number, name, department) VALUES
('Banco Mercantil Santa Cruz', '1000055642', 'BMSC 5642', 'Oficina Central MBOS'),
('Banco Mercantil Santa Cruz', '1001285455', 'BMSC 5455', 'Franz Tamayo'),
('Banco Mercantil Santa Cruz', '1001285428', 'BMSC 5428', 'Shalom'),
('Banco Mercantil Santa Cruz', '1001285412', 'BMSC 5412', 'Reid Shepard'),
('Banco Unión', '124770469', 'BUSA 0469', 'Viacha'),
('Banco Unión', '124769967', 'BUSA 9967', 'Patacamaya'),
('Banco Unión', '124770568', 'BUSA 0568', 'Oficina Central MBOS'),
('Banco Nacional de Bolivia', '1000327901', 'BNB 7901', 'Oficina Central MBOS'),
('Banco Nacional de Bolivia', '1000327928', 'BNB 7928', 'Franz Tamayo'),
('Banco Nacional de Bolivia', '1000327936', 'BNB 7936', 'Shalom'),
('Banco Nacional de Bolivia', '1000327944', 'BNB 7944', 'Reid Shepard'),
('Banco Nacional de Bolivia', '1000327952', 'BNB 7952', 'Viacha'),
('Banco Nacional de Bolivia', '1000327960', 'BNB 7960', 'Patacamaya')
ON CONFLICT (account_number) DO UPDATE SET department = EXCLUDED.department
WHERE bank_account.department IS NULL;

-- ---------------------------------------------------------------------
-- Usuario M-BOS inicial. Se inicia sesión con el usuario (nombre.apellido).
-- Password en texto plano (solo para referencia, no se guarda así):
--   admin.mbos  ->  mbos12345
-- Cámbiala apenas entres en un ambiente real.
-- ---------------------------------------------------------------------
INSERT INTO app_user (full_name, username, password_hash, is_active) VALUES
('M-BOS', 'admin.mbos', '$2b$11$Ap2hxeTvzarOS3rlKveAhu7TTllP3l7Yy7Mf7GWPkAoDerQCj23ya', TRUE)
ON CONFLICT (username) DO NOTHING;

INSERT INTO app_user_role (app_user_id, role_id)
SELECT u.app_user_id, r.role_id
FROM app_user u
JOIN role r ON r.name = 'M-BOS'
WHERE u.username = 'admin.mbos'
ON CONFLICT (app_user_id, role_id) DO NOTHING;

-- ---------------------------------------------------------------------
-- Menú lateral. Agregar filas aquí a medida que se arman las pantallas;
-- parent_menu_id anida opciones. El ícono es un nombre de app-icon.
-- ---------------------------------------------------------------------
INSERT INTO menu (name, translation_key, route, icon, parent_menu_id, display_order, is_active) VALUES
('Inicio', 'nav.home', '/', 'home', NULL, 1, TRUE)
ON CONFLICT (route) DO NOTHING;

-- Grupos (sin ruta propia).
INSERT INTO menu (name, translation_key, route, icon, parent_menu_id, display_order, is_active)
SELECT v.name, v.translation_key, NULL, v.icon, NULL, v.display_order, TRUE
FROM (VALUES
    ('Conciliación', 'nav.reconciliation', 'import', 2),
    ('Catálogos', 'nav.catalogs', 'catalog', 3),
    ('Administración', 'nav.admin', 'settings', 99)
) AS v(name, translation_key, icon, display_order)
WHERE NOT EXISTS (SELECT 1 FROM menu m WHERE m.name = v.name AND m.parent_menu_id IS NULL);

INSERT INTO menu (name, translation_key, route, icon, parent_menu_id, display_order, is_active)
SELECT v.name, v.translation_key, v.route, v.icon, m.menu_id, v.display_order, TRUE
FROM menu m
JOIN (VALUES
    ('Conciliación', 'Nueva conciliación', 'nav.newReconciliation', '/reconciliations/new', 'import', 1),
    ('Conciliación', 'Conciliaciones', 'nav.reconciliations', '/reconciliations', 'history', 2),
    ('Catálogos', 'Cuentas bancarias', 'nav.bankAccounts', '/catalogs/bank-accounts', 'catalog', 1),
    ('Administración', 'Usuarios', 'nav.users', '/admin/users', 'users', 1),
    ('Administración', 'Roles', 'nav.roles', '/admin/roles', 'shield', 2),
    ('Administración', 'Auditoría', 'nav.auditLogs', '/admin/audit-logs', 'history', 3)
) AS v(parent_name, name, translation_key, route, icon, display_order)
  ON m.name = v.parent_name AND m.parent_menu_id IS NULL
ON CONFLICT (route) DO NOTHING;

-- ---------------------------------------------------------------------
-- menu_role: una opción se ve solo si tiene fila para el rol. Un grupo
-- también necesita la suya y se muestra si alguna opción de adentro se ve.
-- ---------------------------------------------------------------------

-- M-BOS: ve todos los menús.
INSERT INTO menu_role (menu_id, role_id)
SELECT m.menu_id, r.role_id FROM menu m, role r
WHERE r.name = 'M-BOS'
ON CONFLICT DO NOTHING;

-- Contabilidad: todo menos Administración, salvo la auditoría.
INSERT INTO menu_role (menu_id, role_id)
SELECT m.menu_id, r.role_id
FROM menu m
LEFT JOIN menu p ON p.menu_id = m.parent_menu_id
CROSS JOIN role r
WHERE r.name = 'Contabilidad'
  AND (COALESCE(p.name, m.name) <> 'Administración'
       OR m.route = '/admin/audit-logs'
       OR (m.name = 'Administración' AND m.parent_menu_id IS NULL))
ON CONFLICT DO NOTHING;

-- Consulta: Inicio y el historial de conciliaciones (solo ver y descargar).
INSERT INTO menu_role (menu_id, role_id)
SELECT m.menu_id, r.role_id
FROM menu m
LEFT JOIN menu p ON p.menu_id = m.parent_menu_id
CROSS JOIN role r
WHERE r.name = 'Consulta'
  AND (m.route IN ('/', '/reconciliations')
       OR (m.name = 'Conciliación' AND m.parent_menu_id IS NULL))
ON CONFLICT DO NOTHING;
