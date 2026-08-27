FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CloudOrders.slnx ./
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src ./src

RUN dotnet restore src/CloudOrders.Migrations/CloudOrders.Migrations.csproj
RUN dotnet publish src/CloudOrders.Migrations/CloudOrders.Migrations.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --self-contained false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "CloudOrders.Migrations.dll"]
