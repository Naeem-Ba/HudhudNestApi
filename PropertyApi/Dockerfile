
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# نسخ ملفات المشروع للاستفادة من Docker layer caching
COPY PropertyApi.sln .
COPY PropertyApi/PropertyApi.csproj PropertyApi/
COPY PropertyApi.Application/PropertyApi.Application.csproj PropertyApi.Application/
COPY PropertyApi.Domain/PropertyApi.Domain.csproj PropertyApi.Domain/
COPY PropertyApi.Infrastructure/PropertyApi.Infrastructure.csproj PropertyApi.Infrastructure/

RUN dotnet restore

COPY . .
WORKDIR /src/PropertyApi
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PropertyApi.dll"]