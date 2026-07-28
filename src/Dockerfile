# Multi-stage, multi-target build. Two final images share everything except their entrypoint and
# which publish output they carry:
#   docker build --target server ...  (default if --target is omitted) -- the MCP server
#   docker build --target cli    ...  -- dndump only, no MCP server bits, much smaller pull for
#                                        shell-driven/agent workflows that never need MCP at all
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

# `dotnet tool install` needs the SDK, so it happens here even though nothing in this stage
# otherwise needs building -- its output is just copied into the (SDK-less) runtime-base stage
# below. dotnet-symbol targets net8.0 with roll-forward to the latest major, so it runs fine
# against the plain `dotnet/runtime` image used there -- it needs no SDK/ASP.NET Core at runtime.
RUN dotnet tool install --global dotnet-symbol

# Shared runtime base for both final images: the actual .NET *runtime* (not the ~900MB SDK image
# the single-image build used to reuse for convenience) plus the DAC-fetching tool and dndump
# itself, since dndump is useful/needed from both the server image (agents with shell access
# inside the MCP container) and the CLI-only image (its whole purpose).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime-base
WORKDIR /app

COPY --from=build /root/.dotnet/tools /root/.dotnet/tools
ENV PATH="$PATH:/root/.dotnet/tools"

# Copy the dndump CLI into its own directory on PATH. Kept separate from the server's /app
# output (rather than merged in) so the two publishes -- which duplicate some of the same Core/
# ClrMD assemblies -- can never clobber or partially overwrite one another's files.
COPY --from=build /app/cli-out /app/cli
ENV PATH="$PATH:/app/cli"

# Create a volume point for dumps
VOLUME /dumps

# DAC/symbol cache volume (docs/CLI_DESIGN.md §8.2). Mount `-v dndump-symcache:/symcache` so
# dotnet-symbol (run below by entrypoint.sh/entrypoint-cli.sh, and optionally re-run by ClrMD
# itself if DOTNETDUMP_SYMBOL_PATHS is set at runtime -- see DumpContext.SymbolCacheVariable)
# reuses previously fetched DAC/DBI files across container runs and `docker exec`s instead of
# hitting the network again. DOTNETDUMP_SYMBOL_CACHE is Core's existing env var for this; setting
# it here just repoints its default (normally /dumps/.symcache) at the dedicated volume for this
# image.
ENV DOTNETDUMP_SYMBOL_CACHE=/symcache
VOLUME /symcache

# --- CLI-only image: dndump plus the DAC tooling, nothing MCP-specific. -----------------------
FROM runtime-base AS cli

COPY src/entrypoint-cli.sh .
RUN chmod +x entrypoint-cli.sh

ENTRYPOINT ["./entrypoint-cli.sh"]

# --- MCP server image (default target): everything above, plus the server itself. -------------
FROM runtime-base AS server

# Copy build artifacts (MCP server)
COPY --from=build /app/out .

# Entrypoint script to handle DACs
COPY src/entrypoint.sh .
RUN chmod +x entrypoint.sh

# The app listens on stdio for MCP
ENTRYPOINT ["./entrypoint.sh"]
