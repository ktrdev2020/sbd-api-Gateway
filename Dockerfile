FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file + NuGet config for restore
COPY Gateway.csproj nuget.config ./

# Authenticate with GitHub Packages NuGet feed and restore
RUN --mount=type=secret,id=GITHUB_TOKEN \
    sed -i "s/%GITHUB_TOKEN%/$(cat /run/secrets/GITHUB_TOKEN)/g" nuget.config \
    && dotnet restore Gateway.csproj

# Copy all source and publish
COPY . .
RUN --mount=type=secret,id=GITHUB_TOKEN \
    sed -i "s/%GITHUB_TOKEN%/$(cat /run/secrets/GITHUB_TOKEN)/g" nuget.config \
    && dotnet publish Gateway.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore \
    && rm -f nuget.config

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
