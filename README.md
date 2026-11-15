# Global Logistics Management System (GLMS) – Part 3

**TechMove Logistics · Modernisation, Docker & Automated Testing**
Nobesuthu Gongo · ST10545758 · EAPD7111

Part 3 turns the Part 2 prototype into a **service-oriented system**: a Web API that owns the
database, an MVC front end that only talks to the API over HTTP, automated API integration tests,
and Docker containers for all three parts, orchestrated with Docker Compose.

```
            Browser
               │  HTTP (cookie sign-in)
               ▼
  ┌───────────────────────────┐   HTTP + JWT (HttpClient)   ┌───────────────────────────┐   EF Core   ┌──────────────────┐
  │ glms-frontend-web  :5000   │ ──────────────────────────▶ │ glms-backend-api  :5080   │ ──────────▶ │ sql-server-db    │
  │ GLMS.Web (MVC)            │   http://glms-backend-api   │ GLMS.Api (Web API)        │  port 1433  │ SQL Server 2022  │
  │ Presentation layer        │          :8080              │ Service layer + GLMS.Core │             │                  │
  │ no DB, no GLMS.Core       │                             │ the ONLY DB client        │             │                  │
  └───────────────────────────┘                             └───────────────────────────┘             └──────────────────┘
                                 all three on the private Docker network "glms-net"
```

---

## 1. Solution structure

```
GLMS.sln
├── src/GLMS.Shared          API contract: DTOs, enums, validation limits, JSON settings (no dependencies)
├── src/GLMS.Core            Domain, business rules, Strategy / Observer / Factory Method (unchanged logic from Part 2)
├── src/GLMS.Api             NEW  ASP.NET Core Web API – controllers, EF Core DbContext, repositories, migrations, JWT, Swagger
│   └── Dockerfile
├── src/GLMS.Web             MVC front end – controllers call the API through typed HttpClients (ApiClients/)
│   └── Dockerfile
├── tests/GLMS.Tests         294 unit tests from Part 2 (still pass: GLMS.Core did not change behaviour)
├── tests/GLMS.IntegrationTests  NEW  automated API integration tests (real HTTP, real SQL Server)
├── docker-compose.yml       sql-server-db + glms-backend-api + glms-frontend-web
├── .github/workflows/       CI: build → unit tests → integration tests → docker compose system test
└── Database/                SQL scripts of the EF Core migrations
```

| Project | References | Talks to |
|---|---|---|
| GLMS.Web | GLMS.Shared **only** | GLMS.Api over HTTP |
| GLMS.Api | GLMS.Core, GLMS.Shared, EF Core | SQL Server |
| GLMS.Core | GLMS.Shared | nothing (pure logic, injected repositories) |

### What moved where (Part 2 → Part 3)

| Part 2 (in GLMS.Web) | Part 3 |
|---|---|
| `Data/GlmsDbContext.cs`, `Repositories.cs`, `DbSeeder.cs` | `GLMS.Api/Data/` |
| `Migrations/` | `GLMS.Api/Migrations/` (+ new `AddAppUsers` migration) |
| `Infrastructure/ContractExpiryWorker.cs` | `GLMS.Api/Infrastructure/` |
| Strategy / Observer / Factory wiring in `Program.cs` | `GLMS.Api/Program.cs` (unchanged) |
| Controllers calling `ContractService` etc. | Controllers calling `IContractsApi` etc. (`GLMS.Web/ApiClients/`) |
| `GLMS.Core/Domain/Enums.cs` | `GLMS.Shared/Enums.cs` (one definition for API and front end) |

## 2. Run it with Docker (the whole system)

**Prerequisite:** Docker Desktop running (Linux containers).

```bash
docker compose up -d --build
```

The first build takes a few minutes (it downloads the .NET SDK and SQL Server images). Then:

| What | URL |
|---|---|
| MVC site | http://localhost:5000 |
| Swagger UI (API) | http://localhost:5080/swagger |
| API health | http://localhost:5080/health |
| SQL Server (SSMS / Azure Data Studio) | `localhost,14330` · user `sa` · password `Glms_Strong!Passw0rd` |

**Demo sign-ins:** `admin` / `Admin@12345` (can delete clients) and `manager` / `Manager@12345`.

Start-up order is enforced by health checks: the API waits until SQL Server accepts logins, applies
the EF Core migrations, seeds the demo data, and only when `/health` is green does the web container start.

```bash
docker compose ps            # all three "healthy"  ← screenshot this together with Docker Desktop
docker compose logs -f glms-backend-api
docker compose down          # stop (data kept in volumes)
docker compose down -v       # stop and wipe the database
```

To change passwords or the JWT key, copy `.env.example` to `.env` and edit it. Set `CURRENCY_MODE=Fixed`
to demo without internet access.

### How the containers find each other

* The API's connection string uses the **service name** `sql-server-db`, not `localhost`:
  `ConnectionStrings__GlmsDatabase=Server=sql-server-db,1433;...`
* The web app calls the API by its service name: `GlmsApi__BaseUrl=http://glms-backend-api:8080/`
* Docker's internal DNS on the `glms-net` bridge network resolves these names. Only the ports listed
  under `ports:` are published to your machine.
* All settings are passed in as **environment variables** from `docker-compose.yml` (`__` maps to `:` in
  ASP.NET Core configuration), so the same image runs in Dev, Test and Prod with different settings.

## 3. Run it from Visual Studio (without Docker)

1. Open `GLMS.sln`. LocalDB is used by default (`src/GLMS.Api/appsettings.json`).
2. Pick the launch profile **"GLMS: API + Web"** in the toolbar (or right-click the solution →
   *Configure Startup Projects* → *Multiple*: `GLMS.Api` and `GLMS.Web` both *Start*).
3. Press **F5**. The API opens Swagger on http://localhost:5080/swagger and the site runs on http://localhost:5086.

The API migrates and seeds the database itself on start-up. If you still have the Part 2 `GLMS_Db`
database, the new `AddAppUsers` migration is added to it automatically.

## 4. The Web API

All endpoints except login and `/health` need `Authorization: Bearer <token>`.
In Swagger: run `POST /api/auth/login`, copy `accessToken`, click **Authorize**, paste it.

| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| POST | `/api/auth/login` | Sign in, get a JWT | 200 | 400, 401 |
| GET | `/api/auth/me` | Who am I | 200 | 401 |
| GET | `/api/contracts?fromDate=&toDate=&status=&serviceLevel=&clientId=&keyword=` | **Filter** contracts | 200 | 400 (From after To), 401 |
| GET | `/api/contracts/open-for-requests` | Active, within term | 200 | 401 |
| GET | `/api/contracts/{id}` | Details + requests, audit, milestones, allowed next statuses | 200 | 404 |
| POST | `/api/contracts` | **Create** (JSON, starts as Draft) | **201** + Location | 400 |
| POST | `/api/contracts/with-agreement` | Create with PDF (+ optional activate), multipart | 201 | 400 |
| PUT | `/api/contracts/{id}` | Update details (send `rowVersion`) | 200 | 400, 404, **409** |
| PATCH | `/api/contracts/{id}/status` | **Approve** (Active) / hold / **decline** (Expired) | 200 | 400 (illegal move / no PDF), 404, 409 |
| PUT | `/api/contracts/{id}/agreement` | Upload / replace signed PDF | 200 | 400 (not a real PDF), 404 |
| GET | `/api/contracts/{id}/agreement` | Download PDF | 200 | 404 |
| GET | `/api/contracts/{id}/audit` | Compliance audit trail (Observer) | 200 | 404 |
| GET/POST | `/api/clients` | List / create | 200 / 201 | 400 |
| GET/PUT/DELETE | `/api/clients/{id}` | Read / update / delete (**Admin only**) | 200 / 200 / 204 | 400, 403, 404 |
| GET/POST | `/api/service-requests?status=&contractId=` | List / raise (USD→ZAR in the API) | 200 / 201 | 400, 503 (no rate) |
| GET | `/api/service-requests/{id}` | Read | 200 | 404 |
| PATCH | `/api/service-requests/{id}/status` | InProgress / Completed / Cancelled | 200 | 400, 404 |
| GET | `/api/rates/usd-zar` | Live rate (Strategy) | 200 | 503 |
| GET | `/api/dashboard` | Home page data in one call | 200 | 401 |
| GET | `/health` | Liveness + database check | 200 | 503 |

**How errors work.** Controllers contain no `try/catch` and no business logic. GLMS.Core throws its
existing exceptions and `GlobalExceptionHandler` turns them into RFC 7807 problem details:
`NotFoundException → 404`, `BusinessRuleException → 400`, `ConcurrencyException → 409`,
`CurrencyConversionException → 503`. Malformed input is rejected with 400 by `[ApiController]` +
DataAnnotations on the DTOs in GLMS.Shared before it reaches the services.

**Layers inside the API:** Controller → GLMS.Core service (business rules, patterns) → repository
interface → EF Core repository → SQL Server. Everything is wired with dependency injection in `Program.cs`.

## 5. The MVC front end (decoupled)

* `GLMS.Web.csproj` has **no EF Core package, no connection string and no reference to GLMS.Core**.
* `ApiClients/` is the front end's service layer: `IContractsApi`, `IClientsApi`, `IServiceRequestsApi`,
  `IRatesApi`, `IDashboardApi`, `IAuthApi`, each a typed `HttpClient` registered with `IHttpClientFactory`.
* `ApiClientBase` turns HTTP results into exceptions controllers can act on (`ApiValidationException`,
  `ApiConflictException`, `ApiNotFoundException`, `ApiUnauthorizedException`, `ApiForbiddenException`,
  `ApiUnavailableException`).
* **Failed API calls** are handled in one place, `Filters/ApiExceptionFilter`: API down or timed out → a
  friendly *service unavailable* page (503); token expired → back to the login page; 403 → message; 404 → not-found page.
  Validation messages from the API appear on the form, under the right field.
* **Authentication:** the login page posts to `POST /api/auth/login`. The returned JWT is stored inside the
  encrypted, HttpOnly sign-in cookie, and `BearerTokenHandler` adds it to every API call. The browser
  never sees the token. Every page requires sign-in (fallback authorization policy).

## 6. Tests

```bash
dotnet test tests/GLMS.Tests                     # 294 unit tests (no database)
dotnet test tests/GLMS.IntegrationTests          # API integration tests (needs Docker running)
```

**Integration tests** (`tests/GLMS.IntegrationTests`, 51 test cases) call the API over HTTP and assert status codes
and JSON. By default they start a real **SQL Server 2022 container with Testcontainers** and host the
real API in memory (`WebApplicationFactory<Program>`). Migrations, seeding, JWT authentication,
controllers, services and EF Core all run exactly as in production.

To run the same tests against the **running Docker containers** instead:

```powershell
docker compose up -d --build
$env:GLMS_API_BASE_URL = "http://localhost:5080"
dotnet test tests/GLMS.IntegrationTests
```

What they cover:

| Area | Examples |
|---|---|
| The brief's example | `GET /api/contracts` → **200 OK** and the JSON is **not null** |
| Data integrity (create then read) | POST a contract → **201** + Location → GET it → every field matches; same for clients and service requests |
| Filtering | by status, client + keyword, date-range overlap; From after To → 400 |
| Status codes | 400 (validation and business rules), 401 (no token or tampered token), 403 (manager deletes a client), 404, **409** (stale rowVersion) |
| Workflow | activate without PDF → 400; upload PDF → activate → audit entry written; Expired → Active → 400; on hold pauses open requests (Observer) |
| Files | `.exe` renamed to `.pdf` → 400 and nothing stored; download returns the exact bytes uploaded |
| Currency + SLA | ZAR = USD × rate rounded to cents; Priority Logistics + urgent → Critical, 2-hour response (Factory Method) |
| Platform | login, `/health`, Swagger document lists the endpoints, dashboard |

**Evidence for the submission:** *Test → Test Explorer → Run All* (both projects green), and the
GitHub **Actions** tab showing the pipeline below.

### CI/CD pipeline (`.github/workflows/dotnet-tests.yml`)

1. Build the solution, run the unit tests.
2. Run the integration tests against a real SQL Server (Testcontainers).
3. Build both Docker images, `docker compose up --wait` (all three healthy), check the site answers,
   and run the integration tests again against the **running containers**.

A push that breaks any of these turns the pipeline red, so the change is never deployed.

## 7. Docker files

| File | What it does |
|---|---|
| `src/GLMS.Api/Dockerfile` | Multi-stage: `sdk:8.0` restores (cached layer from the `.csproj` files only) and publishes; `aspnet:8.0` runtime image runs it as the non-root `app` user on port 8080, with a `HEALTHCHECK` on `/health` and `ENTRYPOINT ["dotnet","GLMS.Api.dll"]`. |
| `src/GLMS.Web/Dockerfile` | Same pattern; does not even copy GLMS.Core into the build. |
| `.dockerignore` | Keeps `bin/`, `obj/`, `.git`, tests and runtime data out of the build context. |
| `docker-compose.yml` | The three containers, the `glms-net` network, health-check-based start order, environment variables, and volumes for the database, the uploaded PDFs and the cookie keys. |

## 8. Submission checklist (Part 3)

- [ ] Push everything to GitHub (`git add -A`, `git commit`, `git push`); check the Actions tab is green.
- [ ] Screenshots: Docker Desktop *Containers* view with the `glms` stack (3 containers, healthy), the
      *Images* view, Swagger UI, the site running on localhost:5000, Test Explorer with all tests passing.
- [ ] Technical reflection report → export `docs/Part3_Technical_Reflection.md` to PDF (edit it into your own words first).
- [ ] Video: see `docs/Part3_Video_Script.md` for the order to show the flow.
