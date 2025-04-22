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

# Copy cert files into the final container
COPY cert.pem ./cert.pem
COPY key.pem ./key.pem

EXPOSE 1883 8883

ENTRYPOINT ["dotnet", "Meshtastic.Mqtt.dll"]