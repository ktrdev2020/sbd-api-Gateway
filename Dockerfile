FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Plan #47 — fonts-thai-tlwg + fontconfig so SkiaSharp can render Thai glyphs
# in the org-structure DOCX image (otherwise text is blank squares).
# --no-install-recommends keeps image growth small (~10MB for the fonts alone).
RUN apt-get update && apt-get install -y --no-install-recommends \
        fonts-thai-tlwg \
        fontconfig \
    && fc-cache -fv \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file + NuGet config for restore
COPY Gateway.csproj nuget.config ./

# Cache buster — passed by the CI workflow as a build arg (typically the
# workflow run number) so that this layer is rebuilt on every workflow run.
#
# Why this is needed: Gateway.csproj declares `SBD.Messaging Version="1.0.*"`
# which resolves the wildcard at restore time. But because the Dockerfile's
# build context is `apps/dotnet/Gateway` (the submodule itself), nothing in
# the COPY layer changes when an upstream NuGet package on the registry is
# republished. Without a per-build cache key, BuildKit happily reuses the
# cached restore layer that was built against the OLD package, and the
# subsequent `dotnet publish` fails with type-not-found errors for symbols
# that exist in the new package but not the cached one.
#
# Referencing $CACHE_BUST inside the RUN command makes the value part of
# the layer hash, so a new build arg => new layer => fresh restore.
ARG CACHE_BUST=0

# Authenticate with GitHub Packages NuGet feed and restore.
# Token is passed via Docker BuildKit secret mount (never in image layers).
# `--force --no-cache` makes dotnet itself re-evaluate the 1.0.* wildcard
# instead of trusting the local NuGet HTTP cache directory.
RUN --mount=type=secret,id=GITHUB_TOKEN \
    echo "Cache bust: $CACHE_BUST"; \
    TOKEN="$(cat /run/secrets/GITHUB_TOKEN 2>/dev/null)"; \
    if [ -n "$TOKEN" ]; then \
      dotnet nuget update source github-ktrdev2020 \
        --configfile nuget.config \
        --username ktrdev2020 \
        --password "$TOKEN" \
        --store-password-in-clear-text; \
    else \
      echo "WARNING: No GITHUB_TOKEN secret provided, NuGet restore may fail for private packages"; \
    fi \
    && dotnet restore Gateway.csproj --force --no-cache -p:USE_NUGET=true

# Copy all source and publish
COPY . .
RUN dotnet publish Gateway.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:USE_NUGET=true --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
