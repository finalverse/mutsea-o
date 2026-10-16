ARG MUTSEA_NATIVE_IMAGE
FROM ${MUTSEA_NATIVE_IMAGE} AS native
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80 AS build
WORKDIR /source
COPY --from=native /opt/mutsea /native
COPY apps/MutSea.AccountPortal apps/MutSea.AccountPortal
COPY apps/MutSea.Registration apps/MutSea.Registration
COPY apps/MutSea.AccountPortal.Tests apps/MutSea.AccountPortal.Tests
RUN dotnet publish apps/MutSea.AccountPortal/MutSea.AccountPortal.csproj -c Release -p:MutSeaBin=/native -o /portal
RUN dotnet build apps/MutSea.AccountPortal.Tests/MutSea.AccountPortal.Tests.csproj -c Release -p:MutSeaBin=/native -o /tests
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim@sha256:1601380775673021a23ea5985669f1daf03719faaa684f839da1752fb48cfc7f AS runtime
WORKDIR /app
COPY --from=build /portal /app
COPY --from=native /opt/mutsea/*.dll /app/
COPY --from=build /tests /tests
USER 1000:33
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
ENTRYPOINT ["dotnet","/app/MutSea.AccountPortal.dll","/config/portal.json"]
