# Despliegue de TransactionMatching

TransactionMatching está en el mismo servidor que InvenTrack y FundTrack (`148.230.73.226`, Ubuntu, Docker) y
se abre en **https://transactionmatching.iasdmbos.cloud** (registro A `transactionmatching` → `148.230.73.226`).

## Cómo está armado

| Pieza | Dónde | Detalle |
|---|---|---|
| Frontend | contenedor `transactionmatching-frontend` | Angular servido por nginx; pasa `/api` al backend. Puerto local `127.0.0.1:5111`. |
| Backend | contenedor `transactionmatching-backend` | .NET 10. Puerto local `127.0.0.1:5110`. Red Docker `transactionmatching-net`. |
| Base de datos | PostgreSQL del servidor | Base `transactionmatching`, usuario `transactionmatching`. El contenedor entra por `host.docker.internal`. |
| Configuración | `/root/transactionmatching-backend.env` | Cadena de conexión, clave JWT y CORS (`https://transactionmatching.iasdmbos.cloud`). Solo root la puede leer. **No va a Git.** Si se cambia, hay que recrear el contenedor del backend (`deploy.sh`). |
| Entrada pública | nginx del servidor, `/etc/nginx/sites-available/transactionmatching.conf` | HTTPS en el 443 (http redirige a https) y pasa todo a `127.0.0.1:5111`. |
| Certificado | Let's Encrypt, `/etc/letsencrypt/live/transactionmatching.iasdmbos.cloud` | Lo emitió y lo renueva solo certbot (`certbot.timer`). |
| Código | `/opt/transactionmatching/backend` y `/opt/transactionmatching/frontend` | Clones de GitHub, desde donde se construyen las imágenes. |

Puertos locales ocupados en el servidor (para no chocar al agregar otro sistema): InvenTrack 5080-5081,
FundTrack 5090-5091, Música 5092, ClockMetrics 5100-5101, TransactionMatching 5110-5111. Cada sistema entra por su dominio en el 443;
TransactionMatching ya no tiene acceso por IP y puerto.

## Actualizar a una nueva versión

1. Sube los cambios a GitHub (backend y frontend).
2. Si hay cambios de base de datos, primero haz un respaldo y después aplica el script:

   ```bash
   sudo -u postgres pg_dump -Fc transactionmatching > /root/transactionmatching_antes_$(date +%Y%m%d_%H%M).dump
   export PGPASSWORD=$(grep ^ConnectionStrings /root/transactionmatching-backend.env | sed 's/.*Password=//')
   psql -h 127.0.0.1 -U transactionmatching -d transactionmatching -v ON_ERROR_STOP=1 -f /opt/transactionmatching/backend/Scripts/<script>.sql
   ```

   `storedProcedures.sql` y `seedData.sql` se pueden volver a correr sin problema; `dataBase.sql` no
   (crea las tablas desde cero): los cambios de tablas van en un `migration_XX_*.sql` nuevo.

3. Actualiza con un solo comando (baja lo último, construye las imágenes y cambia los contenedores):

   ```bash
   bash /opt/transactionmatching/backend/deploy.sh
   ```

4. Revisa que esté bien:

   ```bash
   docker ps | grep transactionmatching
   docker logs --tail 30 transactionmatching-backend
   curl -s -o /dev/null -w "%{http_code}\n" https://transactionmatching.iasdmbos.cloud/
   ```

   El aviso `Cannot load library libgssapi_krb5.so.2` en el log es normal (librería opcional de Kerberos).

## Respaldos

```bash
sudo -u postgres pg_dump -Fc transactionmatching > /root/transactionmatching_$(date +%Y%m%d_%H%M).dump
```

El respaldo de la base es suficiente; conviene guardar también los extractos y Mayores que se suben.
