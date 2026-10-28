# Builds DateNight (server + Blazor client) and runs it. Used by Render; works with any Docker host.

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package downloads are cached between deploys when only code changes.
COPY DateNight.Shared/DateNight.Shared.csproj DateNight.Shared/
COPY DateNight.Client/DateNight.Client.csproj DateNight.Client/
COPY DateNight.Server/DateNight.Server.csproj DateNight.Server/
RUN dotnet restore DateNight.Server/DateNight.Server.csproj

COPY . .
RUN dotnet publish DateNight.Server/DateNight.Server.csproj -c Release -o /app --no-restore

# ---- Run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Render terminates HTTPS in front of the app; trust its X-Forwarded-* headers so the app knows it's on HTTPS.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_ENVIRONMENT=Production \
    PORT=10000
EXPOSE 10000

USER $APP_UID
ENTRYPOINT ["dotnet", "DateNight.Server.dll"]
