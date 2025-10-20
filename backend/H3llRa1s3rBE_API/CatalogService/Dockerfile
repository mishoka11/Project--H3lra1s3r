# ----------------------------
# 1. Build Stage
# ----------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the csproj file first
COPY ["CatalogService.csproj", "./"]
RUN dotnet restore

# Copy everything else *except* Dockerfile and .dockerignore
COPY . ./

# Ensure Dockerfile isn't treated as source (extra safety)
RUN rm -f Dockerfile

RUN dotnet publish -c Release -o /app/publish

# ----------------------------
# 2. Runtime Stage
# ----------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CatalogService.dll"]
