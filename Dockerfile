# Single serving image: compiled React assets + ASP.NET Core API. Data lives in the /data volume.

FROM node:24-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src/backend
COPY backend/ ./
RUN dotnet publish src/AutoKorsak.Api/AutoKorsak.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /src/frontend/dist ./wwwroot
RUN mkdir -p /data && chown "$APP_UID" /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__DatabasePath=/data/autokorsak.db
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AutoKorsak.Api.dll"]
