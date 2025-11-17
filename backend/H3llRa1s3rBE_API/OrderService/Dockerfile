# ======================
# 1. Build Stage
# ======================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["OrderService.csproj", "./"]
RUN dotnet restore

# Copy everything else
COPY . ./

# ⚠️ Ensure migrations are included
RUN dotnet build -c Release
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ======================
# 2. Runtime Stage
# ======================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderService.dll"]
