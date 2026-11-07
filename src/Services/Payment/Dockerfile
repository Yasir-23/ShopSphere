FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY ["src/Services/Payment/ShopSphere.Services.Payment.csproj", "src/Services/Payment/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/ShopSphere.Common.Logging.csproj", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Security/ShopSphere.Common.Security.csproj", "src/BuildingBlocks/ShopSphere.Common.Security/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/ShopSphere.Common.Resilience.csproj", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]
COPY ["src/BuildingBlocks/ShopSphere.EventBus/ShopSphere.EventBus.csproj", "src/BuildingBlocks/ShopSphere.EventBus/"]

RUN dotnet restore "src/Services/Payment/ShopSphere.Services.Payment.csproj"

COPY ["src/Services/Payment/", "src/Services/Payment/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Security/", "src/BuildingBlocks/ShopSphere.Common.Security/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]
COPY ["src/BuildingBlocks/ShopSphere.EventBus/", "src/BuildingBlocks/ShopSphere.EventBus/"]

WORKDIR "/src/src/Services/Payment"
RUN dotnet build "ShopSphere.Services.Payment.csproj" -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "ShopSphere.Services.Payment.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
ENTRYPOINT ["dotnet", "ShopSphere.Services.Payment.dll"]

