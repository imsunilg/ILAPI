# ILAPI

.NET 9 API for the IL Insurance Portal. One backend for both front ends (ILWEBFlutter = portal shell, ILWEBAngular = Travel/Liability modules). It talks to the PostgreSQL database created by the **ILDB** repository.

Projects: `ILAPI.Core` (models, premium calculator, permission codes), `ILAPI.Infrastructure` (Npgsql/Dapper repositories, health check), `ILAPI.API` (controllers, JWT, policies), `ILAPI.Tests`.

## Run
Secrets are never committed. Once, inside `ILAPI.API`:
```
dotnet user-secrets set "ConnectionStrings:PostgreSQL" "Host=localhost;Port=5432;Database=ILPortal;Username=postgres;Password=<pw>"
dotnet user-secrets set "Jwt:Key" "<random string, 32+ chars>"
```
Then `dotnet run --project ILAPI.API --urls http://localhost:5080` (health: `/health`).
CORS origins are in `appsettings.json` (`Cors:Origins`): Angular on 4200, Flutter on 8080.

Tests: `dotnet test`. Role/authorization check against a running API: `scripts/verify-roles.ps1`.

## Auth
Roles: ADMIN, AGENT, UNDERWRITER, MANAGER, CUSTOMER. Permissions come from the database and are issued as `permission` claims in the JWT; each endpoint is protected by a policy.

- `POST /api/v1/auth/{login,register,refresh,logout,handoff/exchange}`, `GET /api/v1/auth/me`
- `POST /api/v1/frontend-session` (alias `/api/v1/auth/handoff`): short-lived single-use token the Flutter shell passes to an embedded Angular module (never in a URL)
- Responses: `{ success, message, data }`
- Quotes: `POST /api/quotes/{motor,home,travel,liability}`, `GET /api/quotes`; admin reads: `/api/customers`, `/api/users`, `/api/roles`, `/api/reports/summary`, `/api/underwriting/queue`

Demo users (development only, created by ILDB): admin/Admin@123, agent/Agent@123, underwriter/Underwriter@123, manager/Manager@123, customer/Customer@123.
