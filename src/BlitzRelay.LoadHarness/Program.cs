using BlitzRelay.Networking;
using BlitzRelay.Protocol;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Diagnostics;

namespace BlitzRelay.LoadHarness;

internal static class Program
{
	private const string InProcessConnectionKey = "blitz-relay-load-harness";

	private const int Mtu = 1350;

	private static readonly TimeSpan SetUpTimeout = TimeSpan.FromSeconds(30);

	private static readonly TimeSpan DrainDuration = TimeSpan.FromSeconds(1);

	private static int Main(string[] args)
	{
		Option<string?> relayAddressOption = new("--relay-address")
		{
			Description = "Address of an external relay to load. If omitted, a relay is started in-process.",
		};

		Option<int> relayPortOption = new("--relay-port")
		{
			Description = "Port of the external relay.",

			DefaultValueFactory = _ => 7770,
		};

		Option<string> connectionKeyOption = new("--connection-key")
		{
			Description = "Connection key of the external relay.",

			DefaultValueFactory = _ => string.Empty,
		};

		Option<int> roomsOption = new("--rooms")
		{
			Description = "Number of rooms.",

			DefaultValueFactory = _ => 50,
		};

		Option<int> clientsOption = new("--clients")
		{
			Description = "Clients per room.",

			DefaultValueFactory = _ => 8,
		};

		Option<int> tickRateOption = new("--tick-rate")
		{
			Description = "Messages per second sent by every host and client.",

			DefaultValueFactory = _ => 30,
		};

		Option<int> payloadSizeOption = new("--payload-size")
		{
			Description = "Game payload size in bytes. At least 8.",

			DefaultValueFactory = _ => 128,
		};

		Option<GameChannel> channelOption = new("--channel")
		{
			Description = "Game channel of every message: Reliable, Unreliable, or UnreliableSequenced.",

			DefaultValueFactory = _ => GameChannel.Unreliable,
		};

		Option<int> threadsOption = new("--threads")
		{
			Description = "Harness threads driving the simulated peers.",

			DefaultValueFactory = _ => Math.Max(1, Environment.ProcessorCount / 4),
		};

		Option<int> warmupOption = new("--warmup")
		{
			Description = "Warm-up duration in seconds.",

			DefaultValueFactory = _ => 5,
		};

		Option<int> durationOption = new("--duration")
		{
			Description = "Measured duration in seconds.",

			DefaultValueFactory = _ => 30,
		};

		RootCommand rootCommand = new("Blitz Relay load harness.")
		{
			Options =
			{
				relayAddressOption,
				relayPortOption,
				connectionKeyOption,
				roomsOption,
				clientsOption,
				tickRateOption,
				payloadSizeOption,
				channelOption,
				threadsOption,
				warmupOption,
				durationOption,
			},
		};

		rootCommand.SetAction(RootCommandAction);

		return rootCommand.Parse(args).Invoke();

		int RootCommandAction(ParseResult parseResult)
		{
			int payloadSize = parseResult.GetRequiredValue(payloadSizeOption);

			if (payloadSize < sizeof(long))
			{
				Console.Error.WriteLine($"'--payload-size' must be at least {sizeof(long)} bytes.");

				return 1;
			}

			GameChannel gameChannel = parseResult.GetRequiredValue(channelOption);

			int maximumPayloadSize = MaximumPayloadSize(gameChannel);

			if (payloadSize > maximumPayloadSize)
			{
				Console.Error.WriteLine($"'--payload-size' must be at most {maximumPayloadSize} bytes on {gameChannel}, so that relayed messages fit in a single datagram.");

				return 1;
			}

			string? relayAddress = parseResult.GetValue(relayAddressOption);

			string connectionKey = parseResult.GetRequiredValue(connectionKeyOption);

			if (relayAddress is not null && string.IsNullOrWhiteSpace(connectionKey))
			{
				Console.Error.WriteLine("'--connection-key' is required when '--relay-address' is set.");

				return 1;
			}

			int rooms = parseResult.GetRequiredValue(roomsOption);

			LoadSettings settings = new
			(
				relayAddress ?? "127.0.0.1",
				parseResult.GetRequiredValue(relayPortOption),
				connectionKey,
				rooms,
				parseResult.GetRequiredValue(clientsOption),
				parseResult.GetRequiredValue(tickRateOption),
				payloadSize,
				gameChannel,
				Math.Clamp(parseResult.GetRequiredValue(threadsOption), 1, rooms),
				TimeSpan.FromSeconds(parseResult.GetRequiredValue(warmupOption)),
				TimeSpan.FromSeconds(parseResult.GetRequiredValue(durationOption))
			);

			return relayAddress is null ? RunInProcess(settings) : Run(settings, "external");
		}
	}

	private static int RunInProcess(LoadSettings settings)
	{
		using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

		using Server server = new(0, InProcessConnectionKey, TimeProvider.System, loggerFactory.CreateLogger<Server>());

		using CancellationTokenSource cancellation = new();

		Task<int> serverRun = server.RunAsync(cancellation.Token);

		try
		{
			LoadSettings loadSettings = settings with
			{
				RelayPort = server.LocalPort,

				ConnectionKey = InProcessConnectionKey,
			};

			return Run(loadSettings, "in-process");
		}
		finally
		{
			cancellation.Cancel();

			serverRun.GetAwaiter().GetResult();
		}
	}

	private static int Run(LoadSettings settings, string relayDescription)
	{
		LoadTimeline timeline = new(settings.Threads);

		LoadWorker[] workers = new LoadWorker[settings.Threads];

		Thread[] threads = new Thread[settings.Threads];

		for (int i = 0; i < workers.Length; i++)
		{
			int roomCount = settings.Rooms / settings.Threads + (i < settings.Rooms % settings.Threads ? 1 : 0);

			workers[i] = new LoadWorker(settings, timeline, roomCount, SetUpTimeout);

			threads[i] = new Thread(workers[i].Run)
			{
				IsBackground = true,

				Name = $"LoadWorker-{i}",
			};

			threads[i].Start();
		}

		timeline.Ready.Wait();

		long start = Stopwatch.GetTimestamp();

		timeline.MeasureStart = start + ToTimestampTicks(settings.Warmup);

		timeline.MeasureEnd = timeline.MeasureStart + ToTimestampTicks(settings.Duration);

		timeline.StopAt = timeline.MeasureEnd + ToTimestampTicks(DrainDuration);

		timeline.Started.Set();

		SleepUntil(timeline.MeasureStart);

		ResourceUsage before = ResourceUsage.Capture();

		SleepUntil(timeline.MeasureEnd);

		ResourceUsage after = ResourceUsage.Capture();

		foreach (Thread thread in threads)
		{
			thread.Join();
		}

		Exception? error = workers.Select(worker => worker.Error).FirstOrDefault(exception => exception is not null);

		if (error is not null)
		{
			Console.Error.WriteLine($"The load run failed: {error.Message}");

			return 1;
		}

		LoadStatistics hostToClients = new();

		LoadStatistics clientsToHost = new();

		foreach (LoadWorker worker in workers)
		{
			hostToClients.Add(worker.HostToClients);

			clientsToHost.Add(worker.ClientsToHost);
		}

		Console.WriteLine($"Relay: {relayDescription} ({settings.RelayAddress}:{settings.RelayPort})");

		Console.WriteLine($"Rooms: {settings.Rooms}, clients per room: {settings.ClientsPerRoom}, tick rate: {settings.TickRate} Hz, payload: {settings.PayloadSize} bytes, channel: {settings.GameChannel}");

		Console.WriteLine($"Warm-up: {settings.Warmup.TotalSeconds:F0} s, measured: {settings.Duration.TotalSeconds:F0} s, harness threads: {settings.Threads}");

		Console.WriteLine();

		WriteDirection("Host -> clients", hostToClients, settings.Duration);

		WriteDirection("Clients -> host", clientsToHost, settings.Duration);

		Console.WriteLine();

		Console.WriteLine($"Peers disconnected during the run: {workers.Sum(worker => worker.DisconnectedPeers)}");

		Console.WriteLine($"Allocated: {(after.AllocatedBytes - before.AllocatedBytes) / 1024.0 / 1024.0:F1} MiB, GC collections (gen0/gen1/gen2): {after.Gen0 - before.Gen0}/{after.Gen1 - before.Gen1}/{after.Gen2 - before.Gen2}");

		return 0;
	}

	private static void WriteDirection(string name, LoadStatistics statistics, TimeSpan duration)
	{
		long[] latencies = [..statistics.Latencies];

		Array.Sort(latencies);

		double deliveredPercentage = statistics.Expected == 0 ? 0 : statistics.Delivered * 100.0 / statistics.Expected;

		Console.WriteLine($"{name}: delivered {statistics.Delivered:N0}/{statistics.Expected:N0} ({deliveredPercentage:F2}%), {statistics.Delivered / duration.TotalSeconds:N0} msg/s");

		Console.WriteLine($"  latency p50 {Percentile(latencies, 0.50):F2} ms | p90 {Percentile(latencies, 0.90):F2} ms | p99 {Percentile(latencies, 0.99):F2} ms | p99.9 {Percentile(latencies, 0.999):F2} ms | max {Percentile(latencies, 1.0):F2} ms");
	}

	private static double Percentile(long[] sortedLatencies, double percentile)
	{
		if (sortedLatencies.Length == 0) return double.NaN;

		int index = Math.Max(0, (int)Math.Ceiling(percentile * sortedLatencies.Length) - 1);

		return sortedLatencies[index] * 1000.0 / Stopwatch.Frequency;
	}

	private static void SleepUntil(long timestamp)
	{
		TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), timestamp);

		if (remaining > TimeSpan.Zero) Thread.Sleep(remaining);
	}

	private static long ToTimestampTicks(TimeSpan timeSpan)
	{
		return (long)(timeSpan.TotalSeconds * Stopwatch.Frequency);
	}

	private static int MaximumPayloadSize(GameChannel gameChannel)
	{
		return gameChannel switch
		{
			GameChannel.Unreliable => Mtu - NetConstants.HeaderSize - MessageCodec.MaxRelayHeaderSize,

			GameChannel.UnreliableSequenced => Mtu - NetConstants.ChanneledHeaderSize - MessageCodec.MaxRelayHeaderSize,

			_ => int.MaxValue,
		};
	}

	private readonly record struct ResourceUsage(long AllocatedBytes, int Gen0, int Gen1, int Gen2)
	{
		public static ResourceUsage Capture()
		{
			return new ResourceUsage(GC.GetTotalAllocatedBytes(precise: true), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
		}
	}
}
