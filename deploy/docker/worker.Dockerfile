# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/docker/worker.Dockerfile -t bridgetogether-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Couples.slnx .editorconfig ./
COPY src/ src/
RUN dotnet restore src/Couples.Worker/Couples.Worker.csproj
RUN dotnet publish src/Couples.Worker/Couples.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV DOTNET_ENVIRONMENT=Production
USER $APP_UID
ENTRYPOINT ["dotnet", "Couples.Worker.dll"]
