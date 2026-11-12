# syntax=docker/dockerfile:1
# One Dockerfile reused by every .NET service. Build context MUST be the repo root.
#   docker build -f infra/docker/dotnet.Dockerfile --build-arg PROJECT=src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj --build-arg DLL=LankaMart.Gateway.dll -t lankamart/gateway:dev .

# ---------- Stage 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /repo
COPY global.json Directory.Build.props ./
COPY src ./src
# e.g. src/gateway/LankaMart.Gateway/LankaMart.Gateway.csproj
ARG PROJECT
RUN dotnet publish "$PROJECT" -c Release -o /out --nologo

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /out .
# e.g. LankaMart.Gateway.dll
ARG DLL
ENV APP_DLL=$DLL
# Identity writes its signing key to /keys (mounted volume); the non-root user needs a writable directory (§1.19)
RUN mkdir -p /keys && chown $APP_UID /keys
# built into Microsoft's images: a non-root user
USER $APP_UID
EXPOSE 8080
# "$@" forwards container args to the app, e.g. `--migrate-only` for the Kubernetes migration Job (Phase 2).
# The trailing "--" becomes $0 of the inline script, so the first real argument lands in $1.
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL \"$@\"", "--"]
