# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files first
COPY ["OrderApi/OrderApi.csproj", "OrderApi/"]
COPY ["SharedContracts/SharedContracts.csproj", "SharedContracts/"]
COPY ["OrderApi.sln", "."]

# Restore
RUN dotnet restore "OrderApi/OrderApi.csproj"

# Copy entire repo
COPY . .

# Build
WORKDIR "/src/OrderApi"
RUN dotnet build "OrderApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OrderApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderApi.dll"]
