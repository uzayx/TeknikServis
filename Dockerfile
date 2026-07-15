# ============================================================
# Multi-stage build:
# Asama 1 (build): SDK imaji (~800 MB) ile restore + publish.
# Asama 2 (final): yalnizca runtime (~220 MB) + publish ciktisi.
# Kucuk imaj, icinde derleyici olmayan container.
# ============================================================

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Once yalnizca proje dosyalari kopyalanir: kod degisse bile
# restore katmani cache'ten gelir (Docker layer caching).
COPY TeknikServis.sln .
COPY TeknikServis.Api/TeknikServis.Api.csproj TeknikServis.Api/
COPY TeknikServis.Application/TeknikServis.Application.csproj TeknikServis.Application/
COPY TeknikServis.Infrastructure/TeknikServis.Infrastructure.csproj TeknikServis.Infrastructure/
RUN dotnet restore TeknikServis.Api/TeknikServis.Api.csproj

COPY TeknikServis.Api/ TeknikServis.Api/
COPY TeknikServis.Application/ TeknikServis.Application/
COPY TeknikServis.Infrastructure/ TeknikServis.Infrastructure/
RUN dotnet publish TeknikServis.Api/TeknikServis.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TeknikServis.Api.dll"]
