FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file + NuGet config for restore
COPY Gateway.csproj nuget.config ./

# Authenticate with GitHub Packages NuGet feed and restore.
# Token is passed via Docker BuildKit secret mount (never in image layers).
RUN --mount=type=secret,id=GITHUB_TOKEN \
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
    && dotnet restore Gateway.csproj

# Copy all source and publish
COPY . .
RUN dotnet publish Gateway.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
