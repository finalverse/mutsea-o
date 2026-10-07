FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src
COPY . .
RUN bash runprebuild.sh && dotnet build -c Release MutSea.sln -m:2

FROM mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim@sha256:47c36b770db8f712ceb03e9220d799b068c769b8bc89c61d62cd41600633d9c2
RUN sed -i 's|http://deb.debian.org|https://deb.debian.org|g' /etc/apt/sources.list.d/debian.sources \
    && apt-get -o Acquire::ForceIPv4=true update \
    && apt-get -o Acquire::ForceIPv4=true install -y --no-install-recommends libgdiplus libc6-dev libsqlite3-0 ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 1000 --create-home mutsea
COPY --from=build --chown=1000:1000 /src/bin/ /opt/mutsea/
COPY LICENSE.txt CONTRIBUTORS.txt /usr/share/doc/mutsea/
COPY deploy/standalone/entrypoint.sh /usr/local/bin/mutsea-entrypoint
RUN chmod 755 /usr/local/bin/mutsea-entrypoint && mkdir /data && chown 1000:1000 /data
WORKDIR /opt/mutsea
USER 1000:1000
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
EXPOSE 443/tcp 19000/udp
ENTRYPOINT ["/usr/local/bin/mutsea-entrypoint"]
