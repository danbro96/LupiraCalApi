# syntax=docker/dockerfile:1
# Build context = repo root (release.yml matrix passes this file explicitly).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LupiraCalApi.Worker/LupiraCalApi.Worker.csproj src/LupiraCalApi.Worker/
COPY src/LupiraCalApi.Core/LupiraCalApi.Core.csproj src/LupiraCalApi.Core/
RUN dotnet restore src/LupiraCalApi.Worker/LupiraCalApi.Worker.csproj
COPY . .
RUN dotnet publish src/LupiraCalApi.Worker/LupiraCalApi.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl: compose healthcheck (curl -fsS .../livez). No LDAP here — DAV lives in the API host.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LupiraCalApi.Worker.dll"]
