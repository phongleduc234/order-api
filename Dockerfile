# Runtime base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["OrderApi/OrderApi.csproj", "OrderApi/"]
RUN dotnet restore "./OrderApi/OrderApi.csproj"

COPY . .
WORKDIR "/src/OrderApi"
RUN dotnet build "./OrderApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./OrderApi.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:GenerateDocumentationFile=true \
    /p:PublishTrimmed=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["dotnet", "OrderApi.dll"]