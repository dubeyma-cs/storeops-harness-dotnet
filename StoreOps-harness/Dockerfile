# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------------------
# Build stage
#
# Restores against the solution so the gate tools are available in the image build too —
# a container that cannot run `StoreOps.ArchCheck` cannot reproduce the harness's verdict.
# ---------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy manifests first so `dotnet restore` is cached independently of source changes.
COPY StoreOps.sln Directory.Build.props .editorconfig ./
COPY src/StoreOps.Api/StoreOps.Api.csproj                 src/StoreOps.Api/
COPY tests/StoreOps.Api.Tests/StoreOps.Api.Tests.csproj    tests/StoreOps.Api.Tests/
COPY tools/StoreOps.ArchCheck/StoreOps.ArchCheck.csproj    tools/StoreOps.ArchCheck/
COPY tools/StoreOps.CoverageGate/StoreOps.CoverageGate.csproj tools/StoreOps.CoverageGate/
RUN dotnet restore StoreOps.sln

COPY . .

# The architecture gate runs inside the image build, so an image can never be produced
# from a tree that violates SO-001..SO-007.
RUN dotnet run --project tools/StoreOps.ArchCheck --no-restore -- --src ./src

RUN dotnet publish src/StoreOps.Api/StoreOps.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# ---------------------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl is here for the health probe below and for demonstrating the API from inside the
# container; remove both if a smaller attack surface matters more than the convenience.
RUN apt-get update \
    && apt-get install --no-install-recommends --assume-yes curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Run as the non-root user the aspnet image provides.
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "StoreOps.Api.dll"]
