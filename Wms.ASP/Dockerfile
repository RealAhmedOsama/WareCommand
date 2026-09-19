# syntax=docker/dockerfile:1

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS base
ARG APP_UID=1654
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
ARG WARECOMMAND_VERSION=0.0.0-local
ARG WARECOMMAND_REVISION=local
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["Directory.Packages.props", "."]
COPY [".editorconfig", "."]
COPY ["Wms.ASP/Wms.ASP.csproj", "Wms.ASP/"]
COPY ["Wms.Application/Wms.Application.csproj", "Wms.Application/"]
COPY ["Wms.Domain/Wms.Domain.csproj", "Wms.Domain/"]
COPY ["Wms.Infrastructure/Wms.Infrastructure.csproj", "Wms.Infrastructure/"]
RUN dotnet restore "./Wms.ASP/Wms.ASP.csproj"
COPY . .
WORKDIR "/src/Wms.ASP"
RUN dotnet build "./Wms.ASP.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG WARECOMMAND_VERSION=0.0.0-local
ARG WARECOMMAND_REVISION=local
RUN dotnet publish "./Wms.ASP.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish \
    /p:UseAppHost=false \
    /p:Version=$WARECOMMAND_VERSION \
    /p:InformationalVersion=$WARECOMMAND_VERSION-$WARECOMMAND_REVISION

FROM base AS final
ARG APP_UID=1654
ARG WARECOMMAND_VERSION=0.0.0-local
ARG WARECOMMAND_REVISION=local
ARG WARECOMMAND_BUILD_DATE=unknown
LABEL org.opencontainers.image.title="WareCommand WMS" \
      org.opencontainers.image.description="WareCommand ASP.NET Core warehouse management web host" \
      org.opencontainers.image.version="$WARECOMMAND_VERSION" \
      org.opencontainers.image.revision="$WARECOMMAND_REVISION" \
      org.opencontainers.image.created="$WARECOMMAND_BUILD_DATE"
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN mkdir -p /var/lib/warecommand/keys \
    && chmod 0555 /app \
    && chown -R $APP_UID:$APP_UID /var/lib/warecommand \
    && chmod 0700 /var/lib/warecommand/keys
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "/app/Wms.ASP.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Wms.ASP.dll"]
