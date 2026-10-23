FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ ./src/
RUN dotnet restore src/Labaik.FakeGateway/Labaik.FakeGateway.csproj
RUN dotnet publish src/Labaik.FakeGateway/Labaik.FakeGateway.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS final
# curl is only for the container readiness probe; the process itself runs as the built-in unprivileged user.
USER root
RUN apt-get update && apt-get install --no-install-recommends -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/ ./
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=6 CMD ["curl", "--fail", "--silent", "--output", "/dev/null", "http://127.0.0.1:8080/health/ready"]
ENTRYPOINT ["dotnet", "Labaik.FakeGateway.dll"]
