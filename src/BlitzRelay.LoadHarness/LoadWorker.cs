using System.Diagnostics;

namespace BlitzRelay.LoadHarness;

internal sealed class LoadWorker(LoadSettings settings, LoadTimeline timeline, int roomCount, TimeSpan setUpTimeout)
{
	private readonly List<LoadRoom> _rooms = [];

	private long _lastPoll = Stopwatch.GetTimestamp();

	public LoadStatistics HostToClients { get; } = new(MeasuredMessageCapacity(settings, roomCount));

	public LoadStatistics ClientsToHost { get; } = new(MeasuredMessageCapacity(settings, roomCount));

	public int DisconnectedPeers { get; private set; }

	public Exception? Error { get; private set; }

	public void Run()
	{
		bool isReady = false;

		try
		{
			SetUp();

			timeline.Ready.Signal();

			isReady = true;

			while (!timeline.Started.IsSet)
			{
				Poll();
			}

			long now;

			while ((now = Stopwatch.GetTimestamp()) < timeline.StopAt)
			{
				if (now < timeline.MeasureEnd)
				{
					foreach (LoadRoom room in _rooms)
					{
						room.Tick(now);
					}
				}

				Poll();
			}
		}
		catch (Exception exception)
		{
			Error = exception;

			if (!isReady) timeline.Ready.Signal();
		}
		finally
		{
			foreach (LoadRoom room in _rooms)
			{
				DisconnectedPeers += room.DisconnectedPeers;

				room.Dispose();
			}
		}
	}

	private void SetUp()
	{
		for (int i = 0; i < roomCount; i++)
		{
			_rooms.Add(new LoadRoom(settings, timeline, HostToClients, ClientsToHost));
		}

		long deadline = Stopwatch.GetTimestamp() + (long)(setUpTimeout.TotalSeconds * Stopwatch.Frequency);

		while (!_rooms.TrueForAll(room => room.IsReady))
		{
			if (Stopwatch.GetTimestamp() > deadline) throw new TimeoutException($"Rooms were not set up within {setUpTimeout.TotalSeconds:F0} seconds.");

			foreach (LoadRoom room in _rooms)
			{
				room.AdvanceSetUp();
			}

			Poll();
		}
	}

	private static int MeasuredMessageCapacity(LoadSettings settings, int roomCount)
	{
		double messages = (double)roomCount * settings.ClientsPerRoom * settings.TickRate * settings.Duration.TotalSeconds;

		return (int)Math.Min(messages, Array.MaxLength);
	}

	private void Poll()
	{
		long now = Stopwatch.GetTimestamp();

		float elapsedMilliseconds = (float)Stopwatch.GetElapsedTime(_lastPoll, now).TotalMilliseconds;

		_lastPoll = now;

		foreach (LoadRoom room in _rooms)
		{
			room.Poll(elapsedMilliseconds);
		}

		Thread.Yield();
	}
}
