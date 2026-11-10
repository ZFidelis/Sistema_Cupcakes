FROM node:24-bookworm-slim AS frontend-build
WORKDIR /src/UI/cupcakes-web
COPY UI/cupcakes-web/package.json UI/cupcakes-web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY UI/cupcakes-web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY global.json ./
COPY API/Cupcakes.Api/Cupcakes.Api.csproj API/Cupcakes.Api/
RUN dotnet restore API/Cupcakes.Api/Cupcakes.Api.csproj
COPY API/Cupcakes.Api/ API/Cupcakes.Api/
COPY --from=frontend-build /src/UI/cupcakes-web/dist/cupcakes-web/browser/ API/Cupcakes.Api/wwwroot/
RUN dotnet publish API/Cupcakes.Api/Cupcakes.Api.csproj --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080
COPY --from=api-build /out/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet Cupcakes.Api.dll --urls \"http://0.0.0.0:${PORT:-8080}\""]
