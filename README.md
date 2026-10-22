# Blitz Relay

Blitz Relay is a relay server for multiplayer game traffic, written in C# and .NET. It handles room management and data
relay between game clients, supporting reliable, unreliable, and unreliable sequenced delivery modes.

## Features

- **Room-Based Relay**: Clients connect to a relay server and are assigned to rooms identified by 8-character codes.
- **Two Room Types**:
    - *Ephemeral*: Created by a host client and destroyed when the host disconnects.
    - *Persistent Reserved*: Created via the HTTP API, survive host disconnection, and support automatic host promotion
      to remaining clients.
- **Host Migration**: Persistent rooms automatically promote the longest-connected client to host when the host
  disconnects.
- **Dual Protocol Support**: UDP for low-latency game traffic relay, HTTP for administrative room management.
- **Configurable Delivery**: Game data can be sent reliably, unreliably, or unreliably sequenced via separate channels.
- **Simple Client Integration**: A minimal binary protocol using the `BlitzRelay.Protocol` library.
- **Native AOT Builds**: The `native` release flavour is compiled ahead-of-time for fast startup and minimal memory
  usage.

## Architecture

The server consists of two main components:

1. **UDP Relay Server** (port 7770 by default): Handles room management and game traffic relay between connected peers.
2. **HTTP Admin API** (port 7771 by default): Provides REST endpoints for managing rooms. It can be disabled with
   `--disable-http`.

### Room Lifecycle

- A host client registers with the relay and receives a room code.
- Other clients join using the room code and receive a virtual client ID.
- The host sends data to specific clients or broadcasts to all.
- When the host of a persistent room disconnects, the server promotes the next client in join order to host.
- An admin can remove the host of a persistent room through the HTTP API. The server then promotes the next client in
  the same way.

## Installation

Download the latest release from the [releases page](https://github.com/abdelfattahradwan/blitz-relay/releases) or build
from source.

## Usage

### Prerequisites

- .NET 10.0 runtime (or use the `standalone` or `native` pre-built binaries)

### Running the Server

Set the required environment variables before starting:

```bash
export BLITZ_RELAY_CONNECTION_KEY="your-connection-key"
export BLITZ_RELAY_HTTP_ADMIN_TOKEN="your-admin-token"
```

```powershell
$env:BLITZ_RELAY_CONNECTION_KEY = "your-connection-key"
$env:BLITZ_RELAY_HTTP_ADMIN_TOKEN = "your-admin-token"
```

`BLITZ_RELAY_HTTP_ADMIN_TOKEN` is only required when the HTTP API is enabled.

Then run the server:

```bash
BlitzRelay [--port <udp-port>] [--http-port <http-port>] [--disable-http] [--log-level <level>] [--cors <config-file>]
           [--client-details <level>]
```

**Command-Line Options**

| Option             | Short | Default     | Description                                                          |
|--------------------|-------|-------------|----------------------------------------------------------------------|
| `--port`           | `-p`  | 7770        | UDP port for relay traffic                                           |
| `--http-port`      |       | 7771        | HTTP port for admin API                                              |
| `--disable-http`   |       |             | Run the relay without the HTTP API                                   |
| `--log-level`      | `-l`  | Information | Logging level (Trace, Debug, Information, Warning, Error, Critical)  |
| `--cors`           |       |             | Optional path to a JSON file describing the HTTP API CORS policy     |
| `--client-details` |       | Ids         | Client details listed by `GET /rooms/{roomCode}/clients` (see below) |
| `--version`        |       |             | Print the relay's version                                            |
| `--help`           | `-h`  |             | Show help and usage information                                      |

### CORS

If you do not pass `--cors`, Blitz Relay uses a permissive CORS policy for the HTTP API:

- Any origin is allowed.
- Any HTTP method is allowed.
- Any request header is allowed.
- Credentials are not enabled.

This default keeps setup simple for local tools, browser dashboards, and hosted admin panels. The HTTP API still
requires the configured `Bearer` token for protected endpoints.

If you need to restrict browser access, create a JSON file and pass it with `--cors`:

```bash
BlitzRelay --cors ./cors.json
```

Example `cors.json`:

```json
{
  "allowedOrigins": [
    "https://admin.example.com"
  ],
  "allowedMethods": [
    "GET",
    "POST",
    "PATCH",
    "DELETE"
  ],
  "allowedHeaders": [
    "Authorization",
    "Content-Type"
  ],
  "allowCredentials": false
}
```

All fields are optional. If `allowedOrigins`, `allowedMethods`, or `allowedHeaders` is omitted or empty, that part of
the policy defaults to allowing any value. Set `allowCredentials` to `true` only when you list specific origins;
browsers do not allow credentials with a wildcard origin.

### Client Details

`GET /rooms/{roomCode}/clients` lists the clients connected to a room. To protect your players' privacy, choose how
much it reveals with `--client-details` or the `BLITZ_RELAY_CLIENT_DETAILS` environment variable. The option takes
precedence over the environment variable.

| Level         | Fields of each client                  |
|---------------|----------------------------------------|
| `Ids`         | `virtualClientId` (default)            |
| `IpAddresses` | `virtualClientId`, `ipAddress`         |
| `IpEndpoints` | `virtualClientId`, `ipAddress`, `port` |

Each level returns only its own fields. Fields of a more detailed level are never included, not even as `null`. The
endpoint only accepts the admin token, not a room's host token.

### HTTP Admin API

Most endpoints require a `Bearer` token in the `Authorization` header, matching the `BLITZ_RELAY_HTTP_ADMIN_TOKEN`
environment variable. `DELETE /rooms/{roomCode}`, `PATCH /rooms/{roomCode}`, and
`DELETE /rooms/{roomCode}/clients/{virtualClientId}` additionally accept the room's host token.

| Method | Endpoint                                      | Description                               |
|--------|-----------------------------------------------|-------------------------------------------|
| GET    | `/health`                                     | Health check (no authentication)          |
| GET    | `/version`                                    | Get the relay's version                   |
| POST   | `/rooms`                                      | Create a persistent room                  |
| GET    | `/rooms`                                      | List all rooms                            |
| GET    | `/rooms/{roomCode}`                           | Get details of a specific room            |
| DELETE | `/rooms/{roomCode}`                           | Delete a room                             |
| PATCH  | `/rooms/{roomCode}`                           | Update a room's display name and metadata |
| GET    | `/rooms/{roomCode}/clients`                   | List a room's clients                     |
| DELETE | `/rooms/{roomCode}/clients/{virtualClientId}` | Kick a client from a room                 |
| DELETE | `/rooms/{roomCode}/host`                      | Remove the host of a persistent room      |

`DELETE /rooms/{roomCode}/host` disconnects the host with the `HostRemoved` error code. The room then behaves as if the
host had left: the relay promotes the longest-connected client to host. Only persistent rooms accept it; to remove the
host of an ephemeral room, delete the room.

**Create Room Request**

```json
{
  "maximumClients": 8,
  "displayName": "My Game Room",
  "isPublic": true,
  "metadata": {
    "key": "value"
  }
}
```

**Patch Room Request**

```json
{
  "displayName": "Updated Room Name",
  "metadataToAdd": {
    "mode": "ranked"
  },
  "metadataToRemove": [
    "oldKey"
  ]
}
```

**Room Response**

```json
{
  "code": "ABC12345",
  "kind": 1,
  "isPublic": true,
  "displayName": "My Game Room",
  "maximumClients": 8,
  "connectedClientCount": 0,
  "hasHost": false,
  "hasPendingHostClaim": false,
  "metadata": {}
}
```

**Version Response**

```json
{
  "version": "1.4.0"
}
```

**Clients Response** (with `--client-details IpEndpoints`)

```json
[
  {
    "virtualClientId": 1,
    "ipAddress": "127.0.0.1",
    "port": 51234
  }
]
```

For the complete, interactive API reference, see the [OpenAPI specification](docs/relay-http-api/openapi.yaml) or
the [Redoc documentation](docs/relay-http-api/redoc-static.html).

### Client Integration

Clients use the `BlitzRelay.Protocol` library to communicate with the relay server:

1. Connect to the relay using the UDP port and connection key.
2. To host: send a `HostRegister` message with the maximum number of clients.
3. To join: send a `ClientJoin` message with the room code.
4. Send game data using `Data` messages, written with `WriteHostData` (from host) or `WriteClientData` (from clients).
5. Handle incoming `Connected`, `Disconnected`, and `Data` messages.

See the `BlitzRelay.Protocol` library for the full message codec API.

## Configuration Reference

| Environment Variable           | Description                                                                 |
|--------------------------------|-----------------------------------------------------------------------------|
| `BLITZ_RELAY_CONNECTION_KEY`   | Key used to authenticate UDP connections (required)                         |
| `BLITZ_RELAY_HTTP_ADMIN_TOKEN` | Token for HTTP admin API authentication (required unless `--disable-http`)  |
| `BLITZ_RELAY_CLIENT_DETAILS`   | Client details level when `--client-details` is not passed (default `Ids`)  |

## Acknowledgements

Blitz Relay uses [LiteNetLib](https://github.com/RevenantX/LiteNetLib) by RevenantX for UDP networking.

## Licence

MIT License. See [LICENSE](LICENSE) for details.

Copyright 2026 Abdelfattah Radwan
