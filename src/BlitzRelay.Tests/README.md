# BlitzRelay.Tests

MSTest tests for the Blitz Relay server and the `BlitzRelay.Protocol` message codec.

## Running

From the repository root:

```bash
task test
```

Arguments after `--` are passed to `dotnet test`. Without Task, run:

```bash
dotnet test src/BlitzRelay.Tests/BlitzRelay.Tests.csproj
```

## Coverage

- `MessageCodecTests`: round trips and malformed payloads for the protocol messages, and game channel classification.
- `RoomCodeTests`: generated room codes use the expected length and alphabet, and room code validation.
- `RelayBackpressurePolicyTests`: reliable packet counts and the reliable backlog thresholds.
- `RelayServerTests`: end-to-end relay behaviour, including room registration and joining, data relayed on every game
  channel, payloads larger than one packet, unicast and broadcast, kicks, disconnect reasons, host promotion and claims,
  host claim expiry, host removal, client snapshots, and room management.
- `RelayHttpApiTests`: the HTTP API, including which tokens each endpoint accepts, host removal status codes, the client
  fields for each `--client-details` level, request validation, and `/version`.

## How the Relay Tests Work

- Each test starts its own relay in-process on an ephemeral UDP port, so tests run in parallel at method level.
- Peers are real LiteNetLib clients connected over loopback, using the same 1350-byte MTU as the transports.
- Host claim expiry is driven by a `FakeTimeProvider`, so tests advance time instead of waiting for it.
- HTTP tests start the real HTTP API on an ephemeral port in front of an in-process relay and call it with `HttpClient`.
