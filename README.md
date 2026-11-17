# TransactionMatching (backend)

API de TransactionMatching en .NET 10 + PostgreSQL. Sale de la base común de FundTrack: seguridad
(usuarios, roles, menú por rol, JWT con refresh token), auditoría y formato de respuestas. El
frontend está en `MBOS.Hub.TransactionMatching.Web`.

| Proyecto | Qué tiene |
|---|---|
| `TransactionMatching.Core` | Entidades, constantes (`RoleNames`), `QueryFilters`, respuestas (`ResponseGetObject`, `ResponsePost`, `PagedResult`), `IUnitOfWork` |
| `TransactionMatching.Application` | DTOs, interfaces, servicios y validadores (FluentValidation) |
| `TransactionMatching.Infrastructure` | `AppDbContext`, mapeos EF (snake_case), auditoría automática, DI |
| `TransactionMatching.Web` | Controladores, `Program.cs`, purga periódica de la auditoría |

## Primera vez en desarrollo

1. Crear la base y correr los scripts, en este orden:

   ```bash
   psql -U postgres -c 'CREATE DATABASE "TransactionMatching"'
   psql -U postgres -d TransactionMatching -v ON_ERROR_STOP=1 -f Scripts/dataBase.sql
   psql -U postgres -d TransactionMatching -v ON_ERROR_STOP=1 -f Scripts/storedProcedures.sql
   psql -U postgres -d TransactionMatching -v ON_ERROR_STOP=1 -f Scripts/seedData.sql
   ```

2. Los secretos no van en `appsettings.json`. En desarrollo se guardan con user-secrets:

   ```bash
   cd TransactionMatching.Web
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=TransactionMatching;Username=postgres;Password=<clave>"
   dotnet user-secrets set "Jwt:Key" "$(node -e "console.log(require('crypto').randomBytes(64).toString('base64'))")"
   ```

   En el servidor van como variables de entorno: `ConnectionStrings__DefaultConnection`,
   `Jwt__Key` y `Cors__AllowedOrigins__0`.

3. `dotnet run` dentro de `TransactionMatching.Web` (http://localhost:5162, Swagger en `/swagger`).
   Se entra con `admin.mbos` / `mbos12345`: cámbiala en cualquier ambiente real.

## Qué hace

Coteja el extracto del banco (BMSC, `.xls`) con el Mayor de ASSINET (`.xlsx`) de la misma cuenta y
arma la conciliación igual que el Excel de control que se hacía a mano: hoja INGRESO y hoja EGRESO,
banco a la izquierda, ASSINET a la derecha, y lo que falta registrar en ASSINET o en el banco.
El cotejo usa importe exacto, fecha cercana (la del banco contra la que va en la descripción del
Mayor) y el nombre (el banco trae el nombre completo; ASSINET, apellido paterno y nombre).

### Dónde está cada parte

| Archivo | Qué hace |
|---|---|
| `Application/Matching/BankStatementReader.cs` | Lee el extracto del BMSC (`.xls`/`.xlsx`): cuenta, período, filas; columnas por encabezado |
| `Application/Matching/LedgerReader.cs` | Lee el Mayor de ASSINET: subcuenta, saldo inicial, asientos; parte la descripción en tipo, fecha y nombre |
| `Application/Matching/MatchEngine.cs` | Cotejo (función pura): importe igual + fecha (50 %) + nombre (50 %). Desde 80 se coteja solo, de 50 a 79 queda por revisar |
| `Application/Matching/NameMatcher.cs` | Parecido de nombres (Jaro-Winkler por palabra), tolera errores de tipeo |
| `Application/Matching/ReconciliationExcel.cs` | El Excel de salida: RESUMEN, INGRESO y EGRESO, como el control manual |
| `Application/Services/ReconciliationService.cs` | Crear, revisar, emparejar, cerrar, exportar |

### API (`/api/reconciliations`)

`GET /` historial · `GET /{id}` detalle · `POST /` (multipart `bankFile`, `ledgerFile`) ·
`DELETE /{id}` · `POST /{id}/rematch` · `POST /{id}/matches` · `POST /{id}/matches/{matchId}/confirm` ·
`POST /{id}/matches/confirm-all` · `DELETE /{id}/matches/{matchId}` · `PUT /{id}/bank-movements/{m}/note` ·
`PUT /{id}/ledger-entries/{e}/note` · `PUT /{id}/notes` · `POST /{id}/close` · `POST /{id}/reopen` ·
`GET /{id}/export` (xlsx). Cuentas: `GET /api/bankaccounts`, `PUT /api/bankaccounts/{id}`.

Con los archivos de octubre 2026 de la cuenta 5642: 311 filas por lado, 304 cotejadas solas,
5 por revisar, 4 diferencias reales; el saldo conciliado queda igual al del banco.

## Agregar una pantalla del negocio

1. Tabla en `Scripts/dataBase.sql` (y una `migration_XX_*.sql` si la base ya existe).
2. Entidad en `Core/Entities`, mapeo en `Infrastructure/Mapping`, `DbSet` en `AppDbContext`.
3. DTO, interfaz, servicio y validador en `Application` (el modelo es `RoleService`), registrados en
   `DependencyInjection.cs`.
4. Controlador en `Web/Controllers`, con `[Authorize(Roles = RoleNames.Writers)]` en lo que escribe.
5. Fila en `menu` y `menu_role` (`Scripts/seedData.sql`), y un `WHEN` en `sp_get_audit_logs` para que
   la auditoría muestre el nombre del registro.

## Diferencias con FundTrack

- El refresh token se guarda como SHA-256 (`refresh_token.token_hash`), no en claro.
- CORS acepta `localhost` solo en desarrollo; en producción, solo `Cors:AllowedOrigins`.
- Los enums viajan como texto (`"Success"`, `"Error"`).
- `sp_purge_audit_logs` está en `Scripts/storedProcedures.sql` (en FundTrack falta en los scripts).
- `Microsoft.AspNetCore.Http` 2.3.x se cambió por `FrameworkReference` (el paquete viejo traía una
  vulnerabilidad conocida).
