FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy all csproj files for restore
COPY apps/dotnet/Gateway/Gateway.csproj apps/dotnet/Gateway/
COPY apps/dotnet/AuthService/AuthService.csproj apps/dotnet/AuthService/
COPY apps/dotnet/AiService/AiService.csproj apps/dotnet/AiService/
COPY apps/dotnet/RealtimeService/RealtimeService.csproj apps/dotnet/RealtimeService/
COPY apps/dotnet/WorkerService/WorkerService.csproj apps/dotnet/WorkerService/
COPY libs/dotnet/SBD.Domain/SBD.Domain.csproj libs/dotnet/SBD.Domain/
COPY libs/dotnet/SBD.Application/SBD.Application.csproj libs/dotnet/SBD.Application/
COPY libs/dotnet/SBD.Infrastructure/SBD.Infrastructure.csproj libs/dotnet/SBD.Infrastructure/
COPY libs/dotnet/SBD.Messaging/SBD.Messaging.csproj libs/dotnet/SBD.Messaging/
COPY libs/dotnet/SBD.ServiceRegistry/SBD.ServiceRegistry.csproj libs/dotnet/SBD.ServiceRegistry/
RUN dotnet restore apps/dotnet/Gateway/Gateway.csproj

# Copy source
COPY apps/dotnet/ apps/dotnet/
COPY libs/dotnet/ libs/dotnet/
RUN dotnet publish apps/dotnet/Gateway/Gateway.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
