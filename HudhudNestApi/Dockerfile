FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first to maximize Docker layer caching.
COPY HudhudNestApi.sln .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY NuGet.config .
COPY HudhudNestApi/HudhudNestApi.csproj HudhudNestApi/
COPY HudhudNestApi/packages.lock.json HudhudNestApi/
COPY HudhudNestApi.Application/HudhudNestApi.Application.csproj HudhudNestApi.Application/
COPY HudhudNestApi.Application/packages.lock.json HudhudNestApi.Application/
COPY HudhudNestApi.Domain/HudhudNestApi.Domain.csproj HudhudNestApi.Domain/
COPY HudhudNestApi.Infrastructure/HudhudNestApi.Infrastructure.csproj HudhudNestApi.Infrastructure/
COPY HudhudNestApi.Infrastructure/packages.lock.json HudhudNestApi.Infrastructure/

RUN dotnet restore "HudhudNestApi/HudhudNestApi.csproj" --locked-mode

COPY . .

WORKDIR /src/HudhudNestApi

RUN dotnet publish "HudhudNestApi.csproj" \
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

ENTRYPOINT ["dotnet", "HudhudNestApi.dll"]
