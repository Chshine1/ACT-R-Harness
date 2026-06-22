FROM mcr.azure.cn/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.azure.cn/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Harness.Host/Harness.Host.csproj", "src/Harness.Host/"]
COPY ["src/Harness.Core/Harness.Core.csproj", "src/Harness.Core/"]
RUN dotnet restore "src/Harness.Host/Harness.Host.csproj"
COPY . .
WORKDIR "/src/src/Harness.Host"
RUN dotnet build "./Harness.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Harness.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Harness.Host.dll"]
