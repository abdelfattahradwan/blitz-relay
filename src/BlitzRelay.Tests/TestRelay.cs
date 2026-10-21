using BlitzRelay.Networking;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BlitzRelay.Tests;

internal sealed class TestRelay : IAsyncDisposable
{
	private const string ConnectionKey = "blitz-relay-tests";

	private readonly CancellationTokenSource _cancellation = new();

	private readonly Task<int> _run;

	public TestRelay()
	{
		Server = new Server(0, ConnectionKey, TimeProvider, NullLogger<Server>.Instance);

		_run = Server.RunAsync(_cancellation.Token);

		if (_run.IsCompleted) throw new InvalidOperationException("The relay failed to start.");
	}

	public Server Server { get; }

	public FakeTimeProvider TimeProvider { get; } = new();

	public Task<RelayTestPeer> ConnectAsync()
	{
		return RelayTestPeer.ConnectAsync(Server.LocalPort, ConnectionKey);
	}

	public async ValueTask DisposeAsync()
	{
		await _cancellation.CancelAsync();

		await _run;

		Server.Dispose();

		_cancellation.Dispose();
	}
}
