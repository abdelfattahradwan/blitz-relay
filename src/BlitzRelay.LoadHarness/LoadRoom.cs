using BlitzRelay.Protocol;
using LiteNetLib;
using System.Buffers.Binary;
using System.Diagnostics;

namespace BlitzRelay.LoadHarness;

internal sealed class LoadRoom : IDisposable
{
	private readonly LoadSettings _settings;

	private readonly LoadTimeline _timeline;

	private readonly LoadStatistics _hostToClients;

	private readonly LoadStatistics _clientsToHost;

	private readonly DeliveryMethod _deliveryMethod;

	private readonly byte[] _hostFrame;

	private readonly byte[] _clientFrame;

	private readonly LoadPeer _host;

	private readonly LoadPeer[] _clients;

	private readonly long _tickInterval;

	private long _nextTick;

	private bool _hostRegisterSent;

	private bool _clientJoinsSent;

	private string? _roomCode;

	private int _connectedClients;

	private int _joinedClients;

	public LoadRoom(LoadSettings settings, LoadTimeline timeline, LoadStatistics hostToClients, LoadStatistics clientsToHost)
	{
		_settings = settings;

		_timeline = timeline;

		_hostToClients = hostToClients;

		_clientsToHost = clientsToHost;

		_deliveryMethod = settings.GameChannel switch
		{
			GameChannel.Unreliable => DeliveryMethod.Unreliable,

			GameChannel.UnreliableSequenced => DeliveryMethod.Sequenced,

			_ => DeliveryMethod.ReliableOrdered,
		};

		_hostFrame = MessageCodec.CreateHostData(MessageCodec.BroadcastVirtualClientId, (byte)settings.GameChannel, new byte[settings.PayloadSize]);

		_clientFrame = MessageCodec.CreateClientData((byte)settings.GameChannel, new byte[settings.PayloadSize]);

		_tickInterval = Stopwatch.Frequency / settings.TickRate;

		_host = new LoadPeer(settings, OnHostReceive);

		_clients = new LoadPeer[settings.ClientsPerRoom];

		for (int i = 0; i < _clients.Length; i++)
		{
			_clients[i] = new LoadPeer(settings, OnClientReceive);
		}
	}

	public bool IsReady
	{
		get => _roomCode is not null && _connectedClients == _clients.Length && _joinedClients == _clients.Length;
	}

	public int DisconnectedPeers
	{
		get => (_host.WasDisconnected ? 1 : 0) + _clients.Count(client => client.WasDisconnected);
	}

	public void Poll(float elapsedMilliseconds)
	{
		_host.Poll(elapsedMilliseconds);

		foreach (LoadPeer client in _clients)
		{
			client.Poll(elapsedMilliseconds);
		}
	}

	public void AdvanceSetUp()
	{
		if (!_hostRegisterSent && _host.IsConnected)
		{
			_host.Send(MessageCodec.CreateHostRegister(_clients.Length), DeliveryMethod.ReliableOrdered);

			_hostRegisterSent = true;
		}

		if (_clientJoinsSent || _roomCode is null || !_clients.All(client => client.IsConnected)) return;

		byte[] clientJoin = MessageCodec.CreateClientJoin(_roomCode);

		foreach (LoadPeer client in _clients)
		{
			client.Send(clientJoin, DeliveryMethod.ReliableOrdered);
		}

		_clientJoinsSent = true;
	}

	public void Tick(long now)
	{
		if (_nextTick == 0) _nextTick = now + Random.Shared.NextInt64(_tickInterval);

		if (now < _nextTick) return;

		_nextTick += _tickInterval;

		BinaryPrimitives.WriteInt64LittleEndian(_hostFrame.AsSpan(MessageCodec.HostDataHeaderSize), now);

		BinaryPrimitives.WriteInt64LittleEndian(_clientFrame.AsSpan(MessageCodec.ClientDataHeaderSize), now);

		_host.Send(_hostFrame, _deliveryMethod);

		foreach (LoadPeer client in _clients)
		{
			client.Send(_clientFrame, _deliveryMethod);
		}

		if (!_timeline.IsMeasured(now)) return;

		_hostToClients.RecordSent(_clients.Length);

		_clientsToHost.RecordSent(_clients.Length);
	}

	public void Dispose()
	{
		_host.Dispose();

		foreach (LoadPeer client in _clients)
		{
			client.Dispose();
		}
	}

	private void OnHostReceive(NetPacketReader reader)
	{
		ReadOnlySpan<byte> payload = reader.GetRemainingBytesSpan();

		if (!MessageCodec.TryReadMessageType(payload, out MessageType messageType)) return;

		switch (messageType)
		{
			case MessageType.RoomCreated:
			{
				if (MessageCodec.TryReadRoomCreated(payload, out string roomCode, out _)) _roomCode = roomCode;

				break;
			}

			case MessageType.Connected:
			{
				_connectedClients++;

				break;
			}

			case MessageType.Data:
			{
				if (MessageCodec.TryReadHostDataSpan(payload, out _, out _, out ReadOnlySpan<byte> gamePayload)) RecordReceived(_clientsToHost, gamePayload);

				break;
			}
		}
	}

	private void OnClientReceive(NetPacketReader reader)
	{
		ReadOnlySpan<byte> payload = reader.GetRemainingBytesSpan();

		if (!MessageCodec.TryReadMessageType(payload, out MessageType messageType)) return;

		switch (messageType)
		{
			case MessageType.JoinSuccess:
			{
				_joinedClients++;

				break;
			}

			case MessageType.Data:
			{
				if (MessageCodec.TryReadClientDataSpan(payload, out _, out ReadOnlySpan<byte> gamePayload)) RecordReceived(_hostToClients, gamePayload);

				break;
			}
		}
	}

	private void RecordReceived(LoadStatistics statistics, ReadOnlySpan<byte> gamePayload)
	{
		if (gamePayload.Length < _settings.PayloadSize) return;

		long sentTimestamp = BinaryPrimitives.ReadInt64LittleEndian(gamePayload);

		if (_timeline.IsMeasured(sentTimestamp)) statistics.RecordReceived(Stopwatch.GetTimestamp() - sentTimestamp);
	}
}
