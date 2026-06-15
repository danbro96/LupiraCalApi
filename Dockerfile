# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LupiraCalApi/LupiraCalApi.csproj src/LupiraCalApi/
RUN dotnet restore src/LupiraCalApi/LupiraCalApi.csproj
COPY . .
RUN dotnet publish src/LupiraCalApi/LupiraCalApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl: compose healthcheck (curl -fsS .../readyz). libldap: System.DirectoryServices.Protocols (DAV Basic -> LDAP bind).
RUN apt-get update && apt-get install -y --no-install-recommends curl libldap-2.5-0 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LupiraCalApi.dll"]
