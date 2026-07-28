# Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
ARG VERSION=0.0.0-local.0
ARG ASSEMBLY_VERSION=0.0.0.0

# Copy source
COPY . .

# Build the MCP server (existing path, unchanged)
RUN dotnet publish src/DotNetDump.Server/DotNetDump.Server.csproj -c Release -o /app/out --framework net10.0 /p:Version=$VERSION /p:AssemblyVersion=$ASSEMBLY_VERSION /p:FileVersion=$ASSEMBLY_VERSION /p:InformationalVersion=$VERSION

# Build the dndump CLI (docs/CLI_DESIGN.md §2/§8.2). DotNetDump.Cli sets PackAsTool for the
# `dotnet tool install` distribution path (§8.1), but a plain `dotnet publish` with no
# RuntimeIdentifier still produces a normal framework-dependent apphost named `dndump` for the
# build image's own architecture -- no `dotnet tool install`/nupkg step is needed to run it here.
RUN dotnet publish src/DotNetDump.Cli/DotNetDump.Cli.csproj -c Release -o /app/cli-out --framework net10.0 /p:Version=$VERSION /p:AssemblyVersion=$ASSEMBLY_VERSION /p:FileVersion=$ASSEMBLY_VERSION /p:InformationalVersion=$VERSION

# Runtime image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
WORKDIR /app

# Install dotnet-symbol tool globally
RUN dotnet tool install --global dotnet-symbol
ENV PATH="$PATH:/root/.dotnet/tools"

# Copy build artifacts (MCP server)
COPY --from=build /app/out .

# Copy the dndump CLI into its own directory on PATH. Kept separate from the server's /app
# output (rather than merged in) so the two publishes -- which duplicate some of the same Core/
# ClrMD assemblies -- can never clobber or partially overwrite one another's files.
COPY --from=build /app/cli-out /app/cli
ENV PATH="$PATH:/app/cli"

# Create a volume point for dumps
VOLUME /dumps

# DAC/symbol cache volume (docs/CLI_DESIGN.md §8.2). Mount `-v dndump-symcache:/symcache` so
# dotnet-symbol (run below by entrypoint.sh, and optionally re-run by ClrMD itself if
# DOTNETDUMP_SYMBOL_PATHS is set at runtime -- see DumpContext.SymbolCacheVariable) reuses
# previously fetched DAC/DBI files across container runs and `docker exec`s instead of hitting
# the network again. DOTNETDUMP_SYMBOL_CACHE is Core's existing env var for this; setting it here
# just repoints its default (normally /dumps/.symcache) at the dedicated volume for this image.
ENV DOTNETDUMP_SYMBOL_CACHE=/symcache
VOLUME /symcache

# Entrypoint script to handle DACs
COPY src/entrypoint.sh .
RUN chmod +x entrypoint.sh

# The app listens on stdio for MCP
ENTRYPOINT ["./entrypoint.sh"]
