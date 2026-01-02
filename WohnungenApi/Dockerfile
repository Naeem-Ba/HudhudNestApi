# ===== build stage =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY *.sln .
COPY WohnungenApi/*.csproj WohnungenApi/
RUN dotnet restore

COPY . .
WORKDIR /src/WohnungenApi
RUN dotnet publish -c Release -o /app/publish

# ===== runtime stage =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WohnungenApi.dll"]
