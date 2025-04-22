#!/bin/bash

# Ensure data directory exists
mkdir -p /app/data

# Check if mounted certs exist
if [ -f /app/data/cert.pem ] && [ -f /app/data/key.pem ]; then
  echo "Using mounted TLS certificates."
else
  echo "Mounted certs not found, generating temporary self-signed certificate..."
  openssl req -x509 -newkey rsa:2048 -keyout /app/data/key.pem -out /app/data/cert.pem -days 365 -nodes -subj "/CN=meshtastic.local"
fi

# Start the .NET app
dotnet Meshtastic.Mqtt.dll