# Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
ARG VERSION=0.0.0-local.0
ARG ASSEMBLY_VERSION=0.0.0.0

# Copy source
COPY . .

# Build
RUN dotnet publish DotNetDump.Server/DotNetDump.Server.csproj -c Release -o /app/out --framework net9.0 /p:Version=$VERSION /p:AssemblyVersion=$ASSEMBLY_VERSION /p:FileVersion=$ASSEMBLY_VERSION /p:InformationalVersion=$VERSION

# Runtime image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
WORKDIR /app

# Install dotnet-symbol tool globally
RUN dotnet tool install --global dotnet-symbol
ENV PATH="$PATH:/root/.dotnet/tools"

# Copy build artifacts
COPY --from=build /app/out .

# Create a volume point for dumps
VOLUME /dumps

# Entrypoint script to handle DACs
COPY entrypoint.sh .
RUN chmod +x entrypoint.sh

# The app listens on stdio for MCP
ENTRYPOINT ["./entrypoint.sh"]
