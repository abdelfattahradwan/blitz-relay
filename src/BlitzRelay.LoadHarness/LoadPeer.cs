using BlitzRelay.Protocol;
using LiteNetLib;

namespace BlitzRelay.LoadHarness;

internal sealed class LoadPeer : IDisposable
{
	private readonly NetManager _netManager;

	private NetPeer? _peer;

	public LoadPeer(LoadSettings settings, Action<NetPacketReader> onReceive)
	{
		EventBasedNetListener listener = new();

		listener.PeerConnectedEvent += peer => _peer = peer;

		listener.PeerDisconnectedEvent += (_, _) => WasDisconnected = true;

		listener.NetworkReceiveEvent += (_, reader, _, _) => onReceive(reader);

		_netManager = new NetManager(listener)
		{
			AutoRecycle = true,

			ChannelsCount = 1,

			MtuOverride = 1350,
		};

		if (!_netManager.StartInManualMode(0)) throw new InvalidOperationException("A load peer failed to start.");

		_netManager.Connect(settings.RelayAddress, settings.RelayPort, settings.ConnectionKey);
	}

	public bool IsConnected
	{
		get => _peer is { ConnectionState: ConnectionState.Connected };
	}

	public bool WasDisconnected { get; private set; }

	public void Send(byte[] payload, DeliveryMethod deliveryMethod)
	{
		_peer?.Send(payload, MessageCodec.RelayWireChannel, deliveryMethod);
	}

	public void Poll(float elapsedMilliseconds)
	{
		_netManager.PollEvents();

		_netManager.ManualUpdate(elapsedMilliseconds);
	}

	public void Dispose()
	{
		_netManager.Stop();
	}
}
