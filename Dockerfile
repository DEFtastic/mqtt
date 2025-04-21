FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY Meshtastic.Mqtt.csproj ./
RUN dotnet restore

# Copy the rest of the code
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app ./

# Expose ports
EXPOSE 1883 8883

# Set environment variable to control SSL mode
# ENV SSL=true  # Uncomment to enable SSL by default

ENTRYPOINT ["dotnet", "Meshtastic.Mqtt.dll"]