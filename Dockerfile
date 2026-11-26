FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/HospitalManagementSystem.Web/HospitalManagementSystem.Web.csproj src/HospitalManagementSystem.Web/
RUN dotnet restore src/HospitalManagementSystem.Web/HospitalManagementSystem.Web.csproj
COPY src/HospitalManagementSystem.Web/ src/HospitalManagementSystem.Web/
RUN dotnet publish src/HospitalManagementSystem.Web/HospitalManagementSystem.Web.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
RUN if ! command -v setpriv >/dev/null || ! command -v mountpoint >/dev/null; then \
        apt-get update && apt-get install -y --no-install-recommends util-linux && rm -rf /var/lib/apt/lists/*; \
    fi && install -d -m 0700 -o app -g app /data/keys /data/documents
COPY --from=build /publish/ ./
COPY deploy/start-container.sh /usr/local/bin/hms-start
USER $APP_UID
ENTRYPOINT ["/bin/sh", "/usr/local/bin/hms-start"]
