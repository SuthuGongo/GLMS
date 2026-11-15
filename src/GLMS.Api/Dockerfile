# syntax=docker/dockerfile:1
# =====================================================================================================
# glms-backend-api : ASP.NET Core Web API (the only service that talks to SQL Server)
# Build from the repository root:  docker build -f src/GLMS.Api/Dockerfile -t glms-backend-api .
# Multi-stage: the large SDK image compiles; only the small ASP.NET runtime image ships.
# =====================================================================================================

# ---------- Stage 1: build (full .NET 8 SDK) ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, using ONLY the project files. This layer is cached and re-used on every build
# until a .csproj changes, so editing C# code does not re-download the NuGet packages.
COPY src/GLMS.Shared/GLMS.Shared.csproj src/GLMS.Shared/
COPY src/GLMS.Core/GLMS.Core.csproj     src/GLMS.Core/
COPY src/GLMS.Api/GLMS.Api.csproj       src/GLMS.Api/
RUN dotnet restore src/GLMS.Api/GLMS.Api.csproj

# Then the source code, and publish a Release build.
COPY src/GLMS.Shared/ src/GLMS.Shared/
COPY src/GLMS.Core/   src/GLMS.Core/
COPY src/GLMS.Api/    src/GLMS.Api/
RUN dotnet publish src/GLMS.Api/GLMS.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Stage 2: runtime (ASP.NET Core runtime only, no SDK, no source code) ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Africa/Johannesburg

# Signed agreement PDFs are written here (mounted as a named volume by docker-compose).
RUN mkdir -p /app/App_Data/agreements && chown -R "$APP_UID" /app/App_Data

COPY --from=build /app/publish .

# Run as the image's built-in non-root user, not root.
USER $APP_UID
EXPOSE 8080

# Healthy once the API answers /health (which also checks the SQL Server connection).
# Uses bash's /dev/tcp because the slim runtime image has no curl.
HEALTHCHECK --interval=10s --timeout=5s --start-period=40s --retries=12 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "GLMS.Api.dll"]
