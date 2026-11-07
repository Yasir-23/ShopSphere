FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
USER app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY ["src/Services/Ordering/ShopSphere.Services.Ordering.Api/ShopSphere.Services.Ordering.Api.csproj", "src/Services/Ordering/ShopSphere.Services.Ordering.Api/"]
COPY ["src/Services/Ordering/ShopSphere.Services.Ordering.Application/ShopSphere.Services.Ordering.Application.csproj", "src/Services/Ordering/ShopSphere.Services.Ordering.Application/"]
COPY ["src/Services/Ordering/ShopSphere.Services.Ordering.Infrastructure/ShopSphere.Services.Ordering.Infrastructure.csproj", "src/Services/Ordering/ShopSphere.Services.Ordering.Infrastructure/"]
COPY ["src/Services/Ordering/ShopSphere.Services.Ordering.Domain/ShopSphere.Services.Ordering.Domain.csproj", "src/Services/Ordering/ShopSphere.Services.Ordering.Domain/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/ShopSphere.Common.Logging.csproj", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Security/ShopSphere.Common.Security.csproj", "src/BuildingBlocks/ShopSphere.Common.Security/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/ShopSphere.Common.Resilience.csproj", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]
COPY ["src/BuildingBlocks/ShopSphere.EventBus/ShopSphere.EventBus.csproj", "src/BuildingBlocks/ShopSphere.EventBus/"]

RUN dotnet restore "src/Services/Ordering/ShopSphere.Services.Ordering.Api/ShopSphere.Services.Ordering.Api.csproj"

COPY ["src/Services/Ordering/", "src/Services/Ordering/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Logging/", "src/BuildingBlocks/ShopSphere.Common.Logging/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Security/", "src/BuildingBlocks/ShopSphere.Common.Security/"]
COPY ["src/BuildingBlocks/ShopSphere.Common.Resilience/", "src/BuildingBlocks/ShopSphere.Common.Resilience/"]
COPY ["src/BuildingBlocks/ShopSphere.EventBus/", "src/BuildingBlocks/ShopSphere.EventBus/"]

WORKDIR "/src/src/Services/Ordering/ShopSphere.Services.Ordering.Api"
RUN dotnet build "ShopSphere.Services.Ordering.Api.csproj" -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish "ShopSphere.Services.Ordering.Api.csproj" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER app
ENTRYPOINT ["dotnet", "ShopSphere.Services.Ordering.Api.dll"]

