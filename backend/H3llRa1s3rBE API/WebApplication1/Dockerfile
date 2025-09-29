# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything and restore
COPY . .
RUN dotnet restore

# Build and publish release
RUN dotnet publish -c Release -o /app

# ===== Runtime stage =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Expose port
EXPOSE 80

# Run app
ENTRYPOINT ["dotnet", "H3lRa1s3r.Api.dll"]
