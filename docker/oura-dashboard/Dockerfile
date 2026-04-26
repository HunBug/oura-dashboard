# ─── Build stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution-level files first (affects restore, good layer cache boundary)
COPY Directory.Build.props Directory.Packages.props OuraDashboard.slnx ./

# Copy only .csproj files to cache the restore layer separately from source changes
COPY src/OuraDashboard.Data/OuraDashboard.Data.csproj       src/OuraDashboard.Data/
COPY src/OuraDashboard.Sync/OuraDashboard.Sync.csproj       src/OuraDashboard.Sync/
COPY src/OuraDashboard.Web/OuraDashboard.Web.csproj          src/OuraDashboard.Web/

RUN dotnet restore src/OuraDashboard.Web/OuraDashboard.Web.csproj

# Copy full source and publish
COPY src/ src/

ARG VERSION=1.0.0
RUN dotnet publish src/OuraDashboard.Web/OuraDashboard.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:Version="$VERSION" \
    -p:UseAppHost=false

# ─── Runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Don't run as root
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

ENV ASPNETCORE_URLS=http://+:8085
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8085

ENTRYPOINT ["dotnet", "OuraDashboard.Web.dll"]
