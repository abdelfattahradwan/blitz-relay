using BlitzRelay.Protocol;
using LiteNetLib;
using System.Threading.Channels;

namespace BlitzRelay.Tests;

internal sealed class RelayTestPeer : IDisposable
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(200);

	private readonly NetManager _netManager;

	private readonly Channel<byte[]> _received = Channel.CreateUnbounded<byte[]>();

	private readonly TaskCompletionSource<NetPeer> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly TaskCompletionSource<byte[]> _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private RelayTestPeer()
	{
		EventBasedNetListener listener = new();

		listener.PeerConnectedEvent += peer => _connected.TrySetResult(peer);

		listener.PeerDisconnectedEvent += (_, disconnectInfo) => _disconnected.TrySetResult(disconnectInfo.AdditionalData.GetRemainingBytesSpan().ToArray());

		listener.NetworkReceiveEvent += (_, reader, _, _) => _received.Writer.TryWrite(reader.GetRemainingBytes());

		_netManager = new NetManager(listener)
		{
			AutoRecycle = true,

			UnsyncedEvents = true,

			ChannelsCount = 1,

			MtuOverride = 1350,
		};
	}

	private NetPeer Peer
	{
		get => _connected.Task.IsCompletedSuccessfully ? _connected.Task.Result : throw new InvalidOperationException("The test peer is not connected.");
	}

	public static async Task<RelayTestPeer> ConnectAsync(int port, string connectionKey)
	{
		RelayTestPeer testPeer = new();

		try
		{
			if (!testPeer._netManager.Start()) throw new InvalidOperationException("The test peer failed to start.");

			testPeer._netManager.Connect("127.0.0.1", port, connectionKey);

			Task completed = await Task.WhenAny(testPeer._connected.Task, testPeer._disconnected.Task).WaitAsync(Timeout);

			if (completed != testPeer._connected.Task) throw new InvalidOperationException("The relay rejected the connection.");

			return testPeer;
		}
		catch
		{
			testPeer.Dispose();

			throw;
		}
	}

	public void Send(byte[] payload, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
	{
		Peer.Send(payload, MessageCodec.RelayWireChannel, deliveryMethod);

		_netManager.TriggerUpdate();
	}

	public async Task<byte[]> ReceiveAsync()
	{
		using CancellationTokenSource cancellation = new(Timeout);

		return await _received.Reader.ReadAsync(cancellation.Token);
	}

	public async Task AssertNothingReceivedAsync()
	{
		await Task.Delay(QuietPeriod);

		Assert.IsFalse(_received.Reader.TryRead(out byte[]? payload), $"Unexpectedly received a {(payload is { Length: > 0 } ? (MessageType)payload[0] : default(MessageType))} message.");
	}

	public void Disconnect()
	{
		Peer.Disconnect();
	}

	public Task<byte[]> WaitForDisconnectAsync()
	{
		return _disconnected.Task.WaitAsync(Timeout);
	}

	public void Dispose()
	{
		_netManager.Stop();
	}
}
