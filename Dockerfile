# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled-composite-extra AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_24.x | bash - && \
    apt-get install -y nodejs

WORKDIR /src
COPY ["NekoTrace.Web/NekoTrace.Web.csproj", "NekoTrace.Web/"]
COPY "Directory.Packages.props" .
RUN dotnet restore "./NekoTrace.Web/NekoTrace.Web.csproj"
COPY "NekoTrace.Web/" "NekoTrace.Web/"
WORKDIR "/src/NekoTrace.Web"
RUN dotnet build "./NekoTrace.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NekoTrace.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8347
EXPOSE 4137

ENTRYPOINT ["dotnet", "NekoTrace.Web.dll"]