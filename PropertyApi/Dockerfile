FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first to maximize Docker layer caching.
COPY PropertyApi.sln .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY NuGet.config .
COPY PropertyApi/PropertyApi.csproj PropertyApi/
COPY PropertyApi/packages.lock.json PropertyApi/
COPY PropertyApi.Application/PropertyApi.Application.csproj PropertyApi.Application/
COPY PropertyApi.Application/packages.lock.json PropertyApi.Application/
COPY PropertyApi.Domain/PropertyApi.Domain.csproj PropertyApi.Domain/
COPY PropertyApi.Infrastructure/PropertyApi.Infrastructure.csproj PropertyApi.Infrastructure/
COPY PropertyApi.Infrastructure/packages.lock.json PropertyApi.Infrastructure/

RUN dotnet restore "PropertyApi/PropertyApi.csproj" --locked-mode

COPY . .

WORKDIR /src/PropertyApi

RUN dotnet publish "PropertyApi.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled-extra AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "PropertyApi.dll"]
