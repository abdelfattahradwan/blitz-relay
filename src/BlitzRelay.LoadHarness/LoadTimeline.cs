namespace BlitzRelay.LoadHarness;

internal sealed class LoadTimeline(int workerCount)
{
	public CountdownEvent Ready { get; } = new(workerCount);

	public ManualResetEventSlim Started { get; } = new();

	public long MeasureStart { get; set; }

	public long MeasureEnd { get; set; }

	public long StopAt { get; set; }

	public bool IsMeasured(long sentTimestamp)
	{
		return sentTimestamp >= MeasureStart && sentTimestamp < MeasureEnd;
	}
}
