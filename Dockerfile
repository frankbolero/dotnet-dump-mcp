# Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy source
COPY . .

# Build
RUN dotnet publish src/DotNetDump.Server/DotNetDump.Server.csproj -c Release -o /app/out

# Runtime image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS runtime
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