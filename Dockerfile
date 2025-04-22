FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# ensure clean NuGet cache fetch
# RUN dotnet nuget add source https://api.nuget.org/v3/index.json

# Copy everything under /src
COPY src/MeshtasticMqtt/ ./MeshtasticMqtt/
WORKDIR /src/MeshtasticMqtt

# Restore and publish
RUN dotnet restore Meshtastic.Mqtt.csproj
RUN dotnet publish Meshtastic.Mqtt.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app ./

EXPOSE 8883

# Add script that generates certs at container startup
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Set entrypoint
ENTRYPOINT ["/app/entrypoint.sh"]