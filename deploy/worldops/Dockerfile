# Extend a verified native MutSea standalone image; retain its runtime and entrypoint.
# Supply an immutable base reference for distribution. The default is a local dev tag.
ARG MUTSEA_IMAGE=mutsea:verify
FROM ${MUTSEA_IMAGE} AS inherited

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src
COPY --from=inherited /opt/mutsea/ /mutsea-bin/
COPY Finalverse/World/ Finalverse/World/
COPY tools/Finalverse.WorldTests/ tools/Finalverse.WorldTests/
COPY tools/MutSea.ContentTests/ tools/MutSea.ContentTests/
COPY tools/MutSea.ContentNativeTests/ tools/MutSea.ContentNativeTests/
RUN dotnet run --project tools/Finalverse.WorldTests -c Release \
    && dotnet run --project tools/MutSea.ContentTests -c Release \
    && dotnet run --project tools/MutSea.ContentNativeTests -c Release -p:MutSeaBin=/mutsea-bin \
    && dotnet build Finalverse/World/Finalverse.World.csproj -c Release -p:MutSeaBin=/mutsea-bin -o /module

FROM inherited
COPY --from=build --chown=1000:1000 /module/Finalverse.World.dll /opt/mutsea/Finalverse.World.dll
