FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY ["src/Services/Catalog/ShopSphere.Services.Catalog.Api/ShopSphere.Services.Catalog.Api.csproj", "src/Services/Catalog/ShopSphere.Services.Catalog.Api/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/ShopSphere.Common.Logging.csproj", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Security/ShopSphere.Common.Security.csproj", "src/BuildingBlocks/ShopSphere.Common.Security/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/ShopSphere.Common.Resilience.csproj", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]

RUN dotnet restore "src/Services/Catalog/ShopSphere.Services.Catalog.Api/ShopSphere.Services.Catalog.Api.csproj"

COPY ["src/Services/Catalog/ShopSphere.Services.Catalog.Api/", "src/Services/Catalog/ShopSphere.Services.Catalog.Api/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Security/", "src/BuildingBlocks/ShopSphere.Common.Security/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]

WORKDIR "/src/src/Services/Catalog/ShopSphere.Services.Catalog.Api"
RUN dotnet build "ShopSphere.Services.Catalog.Api.csproj" -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "ShopSphere.Services.Catalog.Api.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
ENTRYPOINT ["dotnet", "ShopSphere.Services.Catalog.Api.dll"]

