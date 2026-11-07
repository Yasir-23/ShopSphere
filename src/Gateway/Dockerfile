FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY ["src/Gateway/ShopSphere.ApiGateway.csproj", "src/Gateway/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/ShopSphere.Common.Logging.csproj", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/ShopSphere.Common.Resilience.csproj", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]

RUN dotnet restore "src/Gateway/ShopSphere.ApiGateway.csproj"

COPY ["src/Gateway/", "src/Gateway/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]

WORKDIR "/src/src/Gateway"
RUN dotnet build "ShopSphere.ApiGateway.csproj" -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "ShopSphere.ApiGateway.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
ENTRYPOINT ["dotnet", "ShopSphere.ApiGateway.dll"]

