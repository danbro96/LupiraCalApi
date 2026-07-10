# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy both project files before restore (the host references Core) so layer caching works.
COPY src/LupiraCalApi/LupiraCalApi.csproj src/LupiraCalApi/
COPY src/LupiraCalApi.Core/LupiraCalApi.Core.csproj src/LupiraCalApi.Core/
RUN dotnet restore src/LupiraCalApi/LupiraCalApi.csproj
COPY . .
# OpenApiGenerateDocuments=false: doc-gen boots the app (needs the dev Postgres); the committed
# docs/openapi is regenerated locally, not in the image.
RUN dotnet publish src/LupiraCalApi/LupiraCalApi.csproj -c Release -o /app --no-restore /p:OpenApiGenerateDocuments=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl: compose healthcheck (curl -fsS .../readyz).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LupiraCalApi.dll"]
