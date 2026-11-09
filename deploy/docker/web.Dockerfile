# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/docker/web.Dockerfile -t bridgetogether-web .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Couples.slnx .editorconfig ./
COPY src/ src/
COPY docs/legal/ docs/legal/
RUN dotnet restore src/Couples.Web/Couples.Web.csproj
RUN dotnet publish src/Couples.Web/Couples.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Chiseled: no shell or package manager, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Couples.Web.dll"]
