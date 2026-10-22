namespace BlitzRelay.LoadHarness;

internal sealed class LoadStatistics(int latencyCapacity = 0)
{
	private readonly List<long> _latencies = new(latencyCapacity);

	public long Expected { get; private set; }

	public long Delivered { get; private set; }

	public IReadOnlyList<long> Latencies
	{
		get => _latencies;
	}

	public void RecordSent(int recipients)
	{
		Expected += recipients;
	}

	public void RecordReceived(long latency)
	{
		Delivered++;

		_latencies.Add(latency);
	}

	public void Add(LoadStatistics other)
	{
		Expected += other.Expected;

		Delivered += other.Delivered;

		_latencies.AddRange(other._latencies);
	}
}
