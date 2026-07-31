FROM mcr.azure.cn/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.azure.cn/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Dotnet /src/Dotnet
COPY shared /src/shared
WORKDIR /src/Dotnet
RUN dotnet restore "ACT-R-Harness.slnx"
RUN dotnet build "src/Harness.Host/Harness.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src/Dotnet
RUN dotnet publish "src/Harness.Host/Harness.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=build /src/shared /src/shared
ENTRYPOINT ["dotnet", "Harness.Host.dll"]
