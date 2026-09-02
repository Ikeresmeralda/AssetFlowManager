# Imagen para desplegar AssetFlow.Api en un proveedor sin runtime nativo de
# .NET (Render, Fly.io, etc.). No se usa para desarrollo local: ahí sigue
# siendo mejor "dotnet run".

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/AssetFlow.Api/AssetFlow.Api.csproj src/AssetFlow.Api/
RUN dotnet restore src/AssetFlow.Api/AssetFlow.Api.csproj

COPY src/AssetFlow.Api/ src/AssetFlow.Api/
RUN dotnet publish src/AssetFlow.Api/AssetFlow.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Render (y la mayoría de PaaS) asignan el puerto en $PORT en tiempo de
# ejecución, no en build. ASP.NET Core lo espera en ASPNETCORE_URLS, así que
# se traduce aquí con un shell en vez de un ENTRYPOINT en forma de array.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet AssetFlow.Api.dll"]
