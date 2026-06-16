FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PropertyApi/PropertyApi.csproj PropertyApi/
COPY PropertyApi.Application/PropertyApi.Application.csproj PropertyApi.Application/
COPY PropertyApi.Domain/PropertyApi.Domain.csproj PropertyApi.Domain/
COPY PropertyApi.Infrastructure/PropertyApi.Infrastructure.csproj PropertyApi.Infrastructure/

RUN dotnet restore "PropertyApi/PropertyApi.csproj"

COPY . .

RUN dotnet publish "PropertyApi/PropertyApi.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PropertyApi.dll"]
