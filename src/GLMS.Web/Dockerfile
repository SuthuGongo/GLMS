# syntax=docker/dockerfile:1
# =====================================================================================================
# glms-frontend-web : ASP.NET Core MVC front end (calls glms-backend-api over HTTP, no database access)
# Build from the repository root:  docker build -f src/GLMS.Web/Dockerfile -t glms-frontend-web .
# =====================================================================================================

# ---------- Stage 1: build (full .NET 8 SDK) ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore layer (cached until a .csproj changes). Note: GLMS.Core is NOT copied; the front end doesn't need it.
COPY src/GLMS.Shared/GLMS.Shared.csproj src/GLMS.Shared/
COPY src/GLMS.Web/GLMS.Web.csproj       src/GLMS.Web/
RUN dotnet restore src/GLMS.Web/GLMS.Web.csproj

COPY src/GLMS.Shared/ src/GLMS.Shared/
COPY src/GLMS.Web/    src/GLMS.Web/
RUN dotnet publish src/GLMS.Web/GLMS.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Africa/Johannesburg

# Keys that encrypt the sign-in cookie (mounted as a volume so sessions survive restarts).
RUN mkdir -p /app/keys && chown -R "$APP_UID" /app/keys

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]

ENTRYPOINT ["dotnet", "GLMS.Web.dll"]
