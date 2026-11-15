# GLMS Part 3 – Technical Reflection Report

Nobesuthu Gongo · ST10545758 · EAPD7111

> Draft based on what was built in this repository. Rewrite it in your own words, add your own
> experiences (what broke, what surprised you), then export it to PDF for the submission.

## 1. DevOps and testing: why automated tests are critical in a CI/CD pipeline

A CI/CD pipeline exists to deliver changes quickly and often. That speed is only safe if every
change is checked the same way every time, without someone remembering to do it. In GLMS the check
is the GitHub Actions workflow `.github/workflows/dotnet-tests.yml`, which runs on every push and pull request.
A change only counts as releasable when all three stages pass:

1. **Unit tests (294).** These prove the business rules in isolation: the currency maths, the contract
   workflow (Draft → Active → On hold → Expired), the PDF checks, and the Strategy, Observer and Factory
   Method patterns. They run in seconds and point straight at the rule that broke.
2. **API integration tests (51).** Unit tests use fakes, so they cannot see mistakes in the parts that
   join the system together: routing, JSON shape, authentication, EF Core mapping, SQL constraints,
   migrations. The integration tests call the real API over HTTP against a real SQL Server (started by
   Testcontainers) and assert on what a client actually receives: `GET /api/contracts` returns **200**
   with a non-null JSON list; a contract that is POSTed (**201**) is read back with every field
   intact; an activation without a signed PDF returns **400**; a stale `rowVersion` returns **409**; a
   Logistics Manager deleting a client gets **403**.
3. **System test on the containers.** The pipeline builds both Docker images, starts the full stack
   with `docker compose up --wait`, and runs the same integration tests against the running
   `glms-backend-api` container. This catches problems that only exist in the deployed form, such as a
   wrong connection string, a missing environment variable, or a container that cannot reach another.

**How this stops bugs reaching production.** Each test is a *contract* that the next change must
honour. Some examples from this project:

* The MVC app no longer shares code paths with the database. It depends entirely on the API's JSON. If
  someone renamed `clientName` in a DTO, the front end would silently show blank names. The
  create-then-read tests fail first, in the pipeline, before anyone deploys.
* Moving the EF Core migrations from GLMS.Web to GLMS.Api changed their namespace. If EF could not find
  them, the API would start with no tables. Every integration test fails immediately, because the API
  cannot seed or serve data.
* The business rule "a contract can't be activated until its signed agreement is uploaded" is tested
  through the API, not just in GLMS.Core. If a future endpoint skipped the service layer, the HTTP-level
  test would catch it even though the unit tests still passed.

The key point is **feedback at the moment of change**. A bug found by the pipeline costs one red build
and a quick fix by the person who just made the change. The same bug found in production costs wrong
contract data, a rollback, and a client who lost trust. Automated tests turn "it should work" into
evidence that it works, and the pipeline makes collecting that evidence non-optional.

## 2. Containerisation: how Docker ensures consistency across Dev, Test and Prod

"It works on my machine" happens when the application depends on things that differ between machines:
the .NET runtime version, OS libraries, time zone and culture settings, which SQL Server is installed,
file paths, and configuration values typed in by hand. Docker removes those differences by shipping the
application **together with its environment**.

**One image, every environment.** `src/GLMS.Api/Dockerfile` builds the API in the `dotnet/sdk:8.0`
image and copies only the published output into the `dotnet/aspnet:8.0` runtime image. That image
pins the exact runtime, OS packages, time zone (`TZ=Africa/Johannesburg`) and port (8080). The image
my laptop runs is byte-for-byte the image the CI pipeline tests and the image a server would run. Nothing
is "installed" on the target machine except Docker.

**Configuration outside the image.** Settings that should differ between environments (connection string,
JWT signing key, seeded passwords, live or fixed exchange rates) are *not* baked in. Docker Compose passes
them in as environment variables, for example
`ConnectionStrings__GlmsDatabase=Server=sql-server-db,1433;...` and
`GlmsApi__BaseUrl=http://glms-backend-api:8080/`. Dev, Test and Prod run the same image with a different
`.env` file, so a release is promoted, not rebuilt.

**The dependencies are part of the definition.** Before Docker, running GLMS needed SQL Server LocalDB
installed on Windows. Now `docker-compose.yml` declares the database as `sql-server-db` (SQL Server
2022), so every developer and the pipeline get the same database version with one command:
`docker compose up -d --build`. The containers find each other by service name over the private
`glms-net` network, so nothing depends on `localhost` or machine-specific ports.

**Start-up order is explicit, not luck.** Health checks make the API wait until SQL Server accepts logins
(`sqlcmd SELECT 1`), and the front end wait until the API's `/health` endpoint reports the database is
reachable. On a slow laptop or a fast CI server, the order is the same.

**Repeatable builds.** Each Dockerfile copies the `.csproj` files and runs `dotnet restore` before copying
the source, so the package layer is cached and identical between builds. `.dockerignore` keeps local
`bin/` and `obj/` folders out, so a developer's half-built files can't leak into the image.

The result: if it works in the container on my machine, it works in the container in the pipeline,
because they are the same container. The pipeline's third stage proves this on every push by
starting the exact images and testing them.
