#!/bin/bash

# Ensure data directory exists
mkdir -p /app/data

# Generate a very small self-signed cert if not already existing
if [ ! -f /app/data/cert.pem ] || [ ! -f /app/data/key.pem ]; then
  echo "Generating new TLS certificate..."
  openssl req -x509 -newkey rsa:2048 -keyout /app/data/key.pem -out /app/data/cert.pem -days 365 -nodes -subj "/CN=meshtastic.local"
else
  echo "TLS certificate already exists. Skipping generation."
fi

# Start the .NET app
dotnet Meshtastic.Mqtt.dll
