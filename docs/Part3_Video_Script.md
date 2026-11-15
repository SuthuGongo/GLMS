# Part 3 video – suggested order (about 12–15 minutes)

The lecturer marks from this video, so show each rubric item working and say *where* it lives in the code.

1. **Architecture (1 min)** – Show the diagram in README.md. "The MVC app is the presentation layer, the
   API is the service layer and the only thing that talks to SQL Server."
2. **Docker (2 min)** – In a terminal: `docker compose up -d --build`, then `docker compose ps` (3 healthy).
   Open Docker Desktop → Containers → `glms` (sql-server-db, glms-backend-api, glms-frontend-web).
   Open `docker-compose.yml`: service names, `depends_on … service_healthy`, environment variables
   (`ConnectionStrings__GlmsDatabase` uses `sql-server-db`, `GlmsApi__BaseUrl` uses `glms-backend-api`).
   Open one Dockerfile: the two stages, the restore layer, non-root user, HEALTHCHECK, ENTRYPOINT.
3. **Swagger (2 min)** – http://localhost:5080/swagger. Call `GET /api/contracts` → 401. Log in with
   `POST /api/auth/login`, click Authorize, call `GET /api/contracts?status=Active` → 200 JSON.
   `POST /api/contracts` → 201 with Location. `PATCH /api/contracts/{id}/status` to Active without a PDF → 400.
   `GET /api/contracts/999999` → 404.
4. **API code (2 min)** – `ContractsController`: thin actions, route attributes, `ProducesResponseType`.
   `GlobalExceptionHandler`: exceptions → 400/404/409/503. `Program.cs`: the Strategy / Observer /
   Factory wiring moved unchanged from Part 2. `Data/`: repositories, the only code using `GlmsDbContext`.
5. **MVC decoupled (3 min)** – Show `GLMS.Web.csproj` (no EF, no GLMS.Core). `ApiClients/GlmsApiClients.cs`:
   typed HttpClients. `ContractsController` calling `_contracts.CreateAsync(...)`. `BearerTokenHandler`.
   In the browser: sign in at http://localhost:5000, home page, create a contract, upload a PDF, activate
   it, raise a service request (ZAR calculated by the API), try uploading a renamed .exe (rejected).
   Then `docker stop glms-backend-api` and refresh: the "service unavailable" page (error handling),
   then `docker start glms-backend-api`.
6. **Authentication (1 min)** – Sign in as `manager` and try to delete a client → "not allowed" (403 from
   the API). Sign out → every page redirects to the login page.
7. **Integration tests (2 min)** – Test Explorer → run `GLMS.IntegrationTests` (Testcontainers starts SQL
   Server in Docker; point at it in Docker Desktop while it runs). Open
   `CreateContract_ThenRead_ReturnsTheSameData` and `GetContracts_Returns200_AndANonNullJsonList`.
   Optionally run them against the containers: `$env:GLMS_API_BASE_URL="http://localhost:5080"; dotnet test tests/GLMS.IntegrationTests`.
8. **CI/CD (1 min)** – GitHub → Actions: the three stages green (unit → integration → docker compose).
9. **Wrap-up (30 s)** – How Docker solves "works on my machine", and how the pipeline stops breaking changes.
