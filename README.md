# DEFtastic Meshtastic-Aware MQTT Broker

This project is a fork of [Ben's Meshtastic MQTT Boilerplate](https://github.com/meshtastic/mqtt), customized for use by the [DEFtastic](https://github.com/DEFtastic) team at DEF CON.

It provides an MQTT broker specifically designed for Meshtastic mesh network moderation. It handles encrypted mesh packets, validates messages, and can be configured to run with SSL.

## Features

- MQTT server implementation for Meshtastic devices
- Allows for more precise access control than Mosquito ACLs
  - Support for encrypted mesh packet handling and validation
  - Support for validating client connections and subscriptions
- SSL support for secure MQTT connections
- Built using C# / .NET 9.0 with [MQTTnet](https://github.com/dotnet/MQTTnet)
- Multi-platform support
- Easily packaged as a [portable standalone binary](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview?tabs=cli)
- Configurable logging with [Serilog](https://serilog.net/)
- **Dockerized** with automated builds via GitHub Actions
- **Published automatically** to [Docker Hub](https://hub.docker.com/r/defcontastic/deftastic)

---

## Code Structure

The project has been modularized to improve maintainability and clarity. The main components are:

- **Program.cs**: The entry point of the application, responsible for initializing and starting the MQTT broker.
- **MqttServerManager.cs**: Manages the lifecycle and configuration of the MQTT server, including handling client connections and subscriptions.
- **PacketHandler.cs**: Responsible for processing and validating incoming mesh packets, ensuring they meet the required criteria.
- **ClientDatabase.cs**: Manages client information and access control, providing a database-like interface for client data.

---

## Quick Start

### Docker (manual)

```bash
docker pull defcontastic/deftastic:latest
docker run --rm -p 8883:8883 defcontastic/deftastic:latest
```

You must mount a valid `certificate.pfx` if SSL is required.

### Docker Compose (recommended)

```yaml
version: "3"
services:
  meshtastic-mqtt-broker:
    image: defcontastic/deftastic:latest
    container_name: meshtastic-mqtt
    ports:
      - "8883:8883"
    volumes:
      - ./certificate.pfx:/app/certificate.pfx
    restart: unless-stopped
```

```bash
docker-compose up -d
```

---

## Configuration

- **Certificate:** Mount your `.pfx` file to `/app/certificate.pfx` inside the container.
- **Ports:** Broker listens on port 8883 (SSL/TLS).

---

## Ideas for Future Mesh Moderation

- Rate-limiting duplicate packets
- Per-node packet rate limiting
- "Zero-hopping" specific packet types
- Blocking unknown topics or undecryptable packets
- Blocking or limiting certain portnums
- Fail2ban-style IP banning
- Node ID blacklist/whitelist enforcement

---

## Troubleshooting

- Confirm Docker daemon is running.
- Ensure correct certificate formatting (`.pfx`).
- Check container logs with:

```bash
docker logs [container-id]
```

---

## Repo Links

- GitHub: [DEFtastic/mqtt](https://github.com/DEFtastic/mqtt)
- Docker Hub: [defcontastic/deftastic](https://hub.docker.com/r/defcontastic/deftastic)
