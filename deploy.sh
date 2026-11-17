#!/bin/bash
# Actualiza TransactionMatching en el servidor con lo último de GitHub (backend y frontend).
# Uso, en el servidor:  bash /opt/transactionmatching/backend/deploy.sh
# No aplica cambios de base de datos: esos se corren antes, a mano (ver DEPLOY.md).
set -euo pipefail

ROOT=/opt/transactionmatching

echo "== Bajando el código"
git -C "$ROOT/backend" pull --ff-only
git -C "$ROOT/frontend" pull --ff-only
echo "   backend:  $(git -C "$ROOT/backend" log --oneline -1)"
echo "   frontend: $(git -C "$ROOT/frontend" log --oneline -1)"

echo "== Construyendo las imágenes (la versión actual sigue funcionando mientras tanto)"
docker build -q -t transactionmatching-backend:latest "$ROOT/backend" > /dev/null
docker build -q -t transactionmatching-frontend:latest "$ROOT/frontend" > /dev/null

echo "== Cambiando los contenedores"
docker network inspect transactionmatching-net > /dev/null 2>&1 || docker network create transactionmatching-net > /dev/null
docker rm -f transactionmatching-backend transactionmatching-frontend > /dev/null 2>&1 || true

docker run -d --name transactionmatching-backend --network transactionmatching-net --restart unless-stopped \
  --env-file /root/transactionmatching-backend.env --add-host host.docker.internal:host-gateway \
  -p 127.0.0.1:5110:8080 transactionmatching-backend:latest > /dev/null

docker run -d --name transactionmatching-frontend --network transactionmatching-net --restart unless-stopped \
  -p 127.0.0.1:5111:80 transactionmatching-frontend:latest > /dev/null

docker image prune -f > /dev/null

echo "== Revisando"
sleep 8
docker ps --format "   {{.Names}}: {{.Status}}" | grep transactionmatching
echo "   sitio: $(curl -s -o /dev/null -w "%{http_code}" https://transactionmatching.iasdmbos.cloud/)"
echo "Listo: https://transactionmatching.iasdmbos.cloud"
