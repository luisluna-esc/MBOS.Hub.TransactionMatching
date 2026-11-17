#!/bin/bash
# Instala TransactionMatching por PRIMERA VEZ en el servidor (148.230.73.226), con el mismo esquema
# que FundTrack y ClockMetrics. Para actualizar después se usa deploy.sh, no este script.
#
# Uso, como root en el servidor:  bash setup-server.sh
#
# Se puede volver a correr: lo que ya existe (código, base, configuración, nginx, certificado) se salta.
# Requisito: el registro DNS A "transactionmatching" → 148.230.73.226 ya creado.
set -euo pipefail

DOMAIN=transactionmatching.iasdmbos.cloud
ROOT=/opt/transactionmatching
ENV_FILE=/root/transactionmatching-backend.env
DB=transactionmatching
DB_USER=transactionmatching
BACKEND_REPO=https://github.com/luisluna-esc/MBOS.Hub.TransactionMatching.git
FRONTEND_REPO=https://github.com/luisluna-esc/MBOS.Hub.TransactionMatching.Web.git
NGINX_SITE=/etc/nginx/sites-available/transactionmatching.conf

if [ "$(id -u)" -ne 0 ]; then echo "Correr como root."; exit 1; fi

echo "== 1. Código en $ROOT"
mkdir -p "$ROOT"
[ -d "$ROOT/backend/.git" ]  || git clone -q "$BACKEND_REPO"  "$ROOT/backend"
[ -d "$ROOT/frontend/.git" ] || git clone -q "$FRONTEND_REPO" "$ROOT/frontend"
echo "   backend:  $(git -C "$ROOT/backend" log --oneline -1)"
echo "   frontend: $(git -C "$ROOT/frontend" log --oneline -1)"

echo "== 2. Base de datos $DB"
if [ -f "$ENV_FILE" ]; then
  DB_PASS=$(grep ^ConnectionStrings "$ENV_FILE" | sed 's/.*Password=//')
  echo "   ya hay configuración ($ENV_FILE): se usa su contraseña"
else
  DB_PASS=$(openssl rand -hex 24)
fi

if sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='$DB_USER'" | grep -q 1; then
  echo "   el usuario $DB_USER ya existe"
  sudo -u postgres psql -q -c "ALTER USER $DB_USER WITH PASSWORD '$DB_PASS';"
else
  sudo -u postgres psql -q -v ON_ERROR_STOP=1 -c "CREATE USER $DB_USER WITH PASSWORD '$DB_PASS';"
fi

if sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='$DB'" | grep -q 1; then
  echo "   la base $DB ya existe: no se vuelven a crear las tablas"
  NEW_DB=0
else
  sudo -u postgres psql -q -v ON_ERROR_STOP=1 -c "CREATE DATABASE $DB OWNER $DB_USER;"
  NEW_DB=1
fi

# Los contenedores entran por host.docker.internal desde una red de Docker (172.16.0.0/12).
HBA=$(sudo -u postgres psql -tAc "SHOW hba_file")
if ! grep -qE "^host\s+$DB\s+$DB_USER\s+172\.16\.0\.0/12" "$HBA"; then
  echo "host    $DB    $DB_USER    172.16.0.0/12    scram-sha-256" >> "$HBA"
  systemctl reload postgresql
  echo "   permiso agregado en $HBA para las redes de Docker"
fi

export PGPASSWORD="$DB_PASS"
PSQL="psql -h 127.0.0.1 -U $DB_USER -d $DB -v ON_ERROR_STOP=1 -q"
if [ "$NEW_DB" = 1 ]; then
  $PSQL -f "$ROOT/backend/Scripts/dataBase.sql"
  echo "   tablas creadas"
fi
$PSQL -f "$ROOT/backend/Scripts/storedProcedures.sql"
$PSQL -f "$ROOT/backend/Scripts/seedData.sql"
echo "   procedimientos y datos iniciales cargados (usuario admin.mbos)"
unset PGPASSWORD

echo "== 3. Configuración del backend ($ENV_FILE)"
if [ ! -f "$ENV_FILE" ]; then
  JWT_KEY=$(openssl rand -base64 64 | tr -d '\n')
  umask 077
  cat > "$ENV_FILE" <<EOF
ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;Database=$DB;Username=$DB_USER;Password=$DB_PASS
Jwt__Key=$JWT_KEY
Cors__AllowedOrigins__0=https://$DOMAIN
EOF
  chmod 600 "$ENV_FILE"
  echo "   creada (solo root la puede leer)"
else
  echo "   ya existe: no se toca"
fi

echo "== 4. nginx ($DOMAIN → 127.0.0.1:5111)"
if [ ! -f "$NGINX_SITE" ]; then
  cat > "$NGINX_SITE" <<EOF
server {
    listen 80;
    server_name $DOMAIN;

    # El extracto y el Mayor se suben juntos (hasta 10 MB cada uno).
    client_max_body_size 25m;

    location / {
        proxy_pass http://127.0.0.1:5111;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 300s;
    }
}
EOF
  ln -sf "$NGINX_SITE" /etc/nginx/sites-enabled/transactionmatching.conf
  nginx -t
  systemctl reload nginx
  echo "   sitio creado"
else
  echo "   ya existe: no se toca"
fi

echo "== 5. Contenedores (deploy.sh)"
# deploy.sh baja lo último, construye y cambia los contenedores; al final revisa el sitio por https,
# que todavía puede no tener certificado: por eso no se corta aquí si esa revisión falla.
bash "$ROOT/backend/deploy.sh" || true

echo "== 6. Certificado HTTPS"
if [ ! -d "/etc/letsencrypt/live/$DOMAIN" ]; then
  certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --redirect --register-unsafely-without-email || \
    certbot --nginx -d "$DOMAIN" --non-interactive --redirect
else
  echo "   ya existe"
fi

echo "== 7. Revisión"
sleep 3
docker ps --format "   {{.Names}}: {{.Status}}" | grep transactionmatching || true
echo "   sitio: $(curl -s -o /dev/null -w "%{http_code}" https://$DOMAIN/)"
echo "   api:   $(curl -s -o /dev/null -w "%{http_code}" -X POST https://$DOMAIN/api/auth/login -H 'Content-Type: application/json' -d '{"username":"x","password":"x"}') (401 o 400 = el backend responde)"
echo
echo "Listo: https://$DOMAIN  (usuario admin.mbos / mbos12345: cambia la clave al entrar)"
