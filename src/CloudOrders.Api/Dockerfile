FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CloudOrders.slnx ./
# The SDK image supplies the current .NET 10 SDK; global.json pins developer/CI SDKs.
# Omitting it here allows the image to build when the registry lags one feature band.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src ./src

RUN dotnet restore src/CloudOrders.Api/CloudOrders.Api.csproj
RUN dotnet publish src/CloudOrders.Api/CloudOrders.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "CloudOrders.Api.dll"]
