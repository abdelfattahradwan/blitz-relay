# BlitzRelay.LoadHarness

Simulates rooms of hosts and clients against a Blitz Relay server and reports delivery, throughput, latency, and
resource usage. Use it to compare relay changes and catch performance regressions.

## Running

From the repository root, against a relay started in-process:

```bash
task load
```

Arguments after `--` are passed to the harness:

```bash
task load -- --rooms 100 --clients 16 --channel UnreliableSequenced
```

To load a relay running as its own process, for example to profile it in isolation:

```bash
task load -- --relay-address 127.0.0.1 --relay-port 7770 --connection-key your-connection-key
```

## Options

| Option             | Default             | Description                                                           |
|--------------------|---------------------|-----------------------------------------------------------------------|
| `--relay-address`  | In-process relay    | Address of an external relay                                          |
| `--relay-port`     | 7770                | Port of the external relay                                            |
| `--connection-key` |                     | Connection key of the external relay. Required with `--relay-address` |
| `--rooms`          | 50                  | Number of rooms                                                       |
| `--clients`        | 8                   | Clients per room                                                      |
| `--tick-rate`      | 30                  | Messages per second sent by every host and client                     |
| `--payload-size`   | 128                 | Game payload size in bytes. At least 8                                |
| `--channel`        | Unreliable          | `Reliable`, `Unreliable`, or `UnreliableSequenced`                    |
| `--threads`        | Processor count / 4 | Harness threads driving the simulated peers, from 1 to `--rooms`      |
| `--warmup`         | 5                   | Warm-up duration in seconds                                           |
| `--duration`       | 30                  | Measured duration in seconds                                          |

Unreliable payloads must fit in a single datagram once the relay adds its header: up to 1343 bytes on `Unreliable` and
1340 bytes on `UnreliableSequenced`. The harness rejects larger payloads.

## How It Works

- Every tick, each host broadcasts a message to its clients, and each client sends a message to its host. Every
  message starts with its send timestamp.
- The run is a warm-up, a measured window, and a one-second drain for messages still in flight. Only messages sent
  during the measured window are counted.
- Simulated peers run LiteNetLib in manual mode, so a few harness threads drive every peer.

## Output

- Delivered and expected messages per direction, messages per second, and latency percentiles (p50, p90, p99, p99.9,
  and max).
- Peers disconnected during the run, for example by the relay's reliable backlog limit.
- Allocated bytes and GC collections over the measured window. When the relay runs in-process, these include the
  simulated peers.

The harness threads poll continuously to timestamp messages precisely, so the harness doesn't report CPU usage. To
measure the relay's CPU usage, run the relay as its own process and load it with `--relay-address`.
