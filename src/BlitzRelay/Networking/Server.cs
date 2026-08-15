using BlitzRelay.Protocol;
using BlitzRelay.Rooms;
using LiteNetLib;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace BlitzRelay.Networking;

internal sealed class Server : IDisposable
{
	private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(15);

	private static readonly TimeSpan HostClaimTimeout = TimeSpan.FromSeconds(10);

	private const int StackallocRelayFrameThreshold = 2048;

	private readonly NetManager _netManager;

	private readonly ILogger<Server> _logger;

	private readonly Dictionary<string, Room> _roomsByCode;

	private readonly Lock _mutex = new();

	private readonly int _port;

	private readonly string _connectionKey;

	private bool _disposed;

	public Server(int port, string connectionKey, ILogger<Server> logger)
	{
		_port = port;

		_connectionKey = connectionKey;

		_logger = logger;

		_roomsByCode = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase);

		EventBasedNetListener listener = new();

		listener.ConnectionRequestEvent += HandleConnectionRequest;

		listener.PeerConnectedEvent += HandlePeerConnected;

		listener.PeerDisconnectedEvent += HandlePeerDisconnected;

		listener.NetworkReceiveEvent += HandleNetworkReceive;

		listener.NetworkErrorEvent += HandleNetworkError;

		listener.NetworkLatencyUpdateEvent += HandleNetworkLatencyUpdate;

		_netManager = new NetManager(listener)
		{
			AutoRecycle = true,

			UnsyncedEvents = false,

			UnsyncedReceiveEvent = true,

			UpdateTime = 15,

			PingInterval = 2000,

			DisconnectTimeout = 30000,

			ChannelsCount = 1,
		};
	}

	public async Task<int> RunAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (!_netManager.Start(_port))
		{
			Log.FailedToStartRelay(_logger, _port);

			return 1;
		}

		Log.RelayServerStarted(_logger, _port, _netManager.ChannelsCount, _netManager.PingInterval, _netManager.DisconnectTimeout);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				_netManager.PollEvents();

				ExpirePendingHostClaims();

				await Task.Delay(PollDelay, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Operation cancelled, do nothing.
		}
		finally
		{
			Log.RelayServerStopping(_logger);

			_netManager.Stop();
		}

		return 0;
	}

	public bool TryCreateReservedRoom(ushort maximumClients, string displayName, bool isPublic, IReadOnlyDictionary<string, string>? metadata, out RoomSnapshot? snapshot, out ErrorCode errorCode)
	{
		lock (_mutex)
		{
			string roomCode;

			do
			{
				roomCode = RoomCode.Generate();
			}
			while (_roomsByCode.ContainsKey(roomCode));

			Room room = new()
			{
				Code = roomCode,

				Kind = RoomKind.PersistentReserved,

				DisplayName = displayName,

				IsPublic = isPublic,

				MaximumClients = maximumClients,
			};

			if (metadata is not null)
			{
				foreach ((string key, string value) in metadata)
				{
					room.Metadata[key] = value;
				}
			}

			_roomsByCode.Add(room.Code, room);

			snapshot = RoomSnapshot.FromRoom(room);

			errorCode = default(ErrorCode);

			return true;
		}
	}

	public IReadOnlyList<RoomSnapshot> GetRoomSnapshots()
	{
		lock (_mutex)
		{
			RoomSnapshot[] roomSnapshots = new RoomSnapshot[_roomsByCode.Count];

			int index = 0;

			foreach (Room room in _roomsByCode.Values)
			{
				roomSnapshots[index++] = RoomSnapshot.FromRoom(room);
			}

			return roomSnapshots;
		}
	}

	public RoomSnapshot? GetRoomSnapshot(string roomCode)
	{
		lock (_mutex)
		{
			return _roomsByCode.TryGetValue(roomCode, out Room? room) ? RoomSnapshot.FromRoom(room) : null;
		}
	}

	public bool DeleteRoom(string roomCode)
	{
		lock (_mutex)
		{
			if (!_roomsByCode.TryGetValue(roomCode, out Room? room)) return false;

			CloseRoomLocked(room, ErrorCode.RoomClosed);

			return true;
		}
	}

	public bool HasRoomHostToken(string roomCode, string roomHostToken)
	{
		lock (_mutex)
		{
			return _roomsByCode.TryGetValue(roomCode, out Room? room) && !string.IsNullOrWhiteSpace(room.HostToken) && string.Equals(room.HostToken, roomHostToken, StringComparison.OrdinalIgnoreCase);
		}
	}

	public RoomSnapshot? PatchRoom(string roomCode, string? displayName, IReadOnlyDictionary<string, string>? metadataToAdd, IReadOnlyList<string>? metadataToRemove)
	{
		lock (_mutex)
		{
			if (!_roomsByCode.TryGetValue(roomCode, out Room? room)) return null;

			if (displayName is not null) room.DisplayName = displayName;

			if (metadataToRemove is not null)
			{
				foreach (string key in metadataToRemove)
				{
					room.Metadata.Remove(key);
				}
			}

			if (metadataToAdd is not null)
			{
				foreach ((string key, string value) in metadataToAdd)
				{
					room.Metadata[key] = value;
				}
			}

			return RoomSnapshot.FromRoom(room);
		}
	}

	public bool TryKickClient(string roomCode, int virtualClientId)
	{
		lock (_mutex)
		{
			if (!_roomsByCode.TryGetValue(roomCode, out Room? room)) return false;

			if (!room.ClientsByVirtualId.TryGetValue(virtualClientId, out PeerConnection? clientSession)) return false;

			Log.ClientKickedByAdmin(_logger, virtualClientId, clientSession.Peer.Id, roomCode);

			return TryRemoveAndDisconnectClientLocked(room, virtualClientId);
		}
	}

	public void Dispose()
	{
		if (_disposed) return;

		_disposed = true;

		_netManager.Stop();
	}

	private void HandleConnectionRequest(ConnectionRequest request)
	{
		try
		{
			request.AcceptIfKey(_connectionKey);
		}
		catch (Exception exception)
		{
			Log.ConnectionRequestRejected(_logger, request.RemoteEndPoint, exception);

			request.Reject();
		}
	}

	private void HandlePeerConnected(NetPeer peer)
	{
		PeerConnection session = GetOrCreateSession(peer);

		Log.PeerConnected(_logger, peer.Id, session.Endpoint);
	}

	private void HandlePeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
	{
		lock (_mutex)
		{
			PeerConnection session = GetOrCreateSession(peer);

			Log.PeerDisconnected(_logger, peer.Id, session.Endpoint, session.Role, disconnectInfo.Reason);

			PeerRole role = session.Role;

			Room? room = session.Room;

			int virtualClientId = session.VirtualClientId;

			if (room is not null && ReferenceEquals(room.PendingHostPromotionPeer, session))
			{
				bool ackReceived = room.PendingHostPromotionAckReceived;

				session.Clear();

				if (ackReceived)
				{
					room.PendingHostPromotionPeer = null;

					room.PendingHostPromotionVirtualClientId = 0;

					room.PendingHostPromotionAckReceived = false;
				}
				else
				{
					room.ClearPendingHostClaim();

					NotifyClientsHostAvailabilityLocked(room, isAvailable: false);

					_ = TryPromoteNextClientToHostLocked(room);
				}

				return;
			}

			if (role == PeerRole.Host && room is not null)
			{
				session.Clear();

				if (room.Kind == RoomKind.EphemeralHostOwned)
				{
					_roomsByCode.Remove(room.Code);

					foreach ((int key, PeerConnection client) in room.ClientsByVirtualId)
					{
						Send(client, MessageCodec.CreateDisconnected(key), DeliveryMethod.ReliableOrdered);

						client.Clear();

						DisconnectPeer(client);
					}

					room.ClientsByVirtualId.Clear();

					Log.RoomTornDownBecauseHostDisconnected(_logger, room.Code);

					return;
				}

				room.Host = null;

				room.HostToken = null;

				room.ClearPendingHostClaim();

				NotifyClientsHostAvailabilityLocked(room, isAvailable: false);

				_ = TryPromoteNextClientToHostLocked(room);

				return;
			}

			if (role == PeerRole.Client && room is not null)
			{
				room.ClientsByVirtualId.Remove(virtualClientId);

				session.Clear();

				if (room is { HasActiveHost: true, Host: not null })
				{
					Send(room.Host, MessageCodec.CreateDisconnected(virtualClientId), DeliveryMethod.ReliableOrdered);
				}
				else if (room is { Kind: RoomKind.PersistentReserved, HasPendingHostClaim: false })
				{
					_ = TryPromoteNextClientToHostLocked(room);
				}

				return;
			}

			session.Clear();
		}
	}

	private void HandleNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
	{
		PeerConnection session = GetOrCreateSession(peer);

		ReadOnlySpan<byte> payload = reader.GetRemainingBytesSpan();

		Log.RelayPayloadReceived(_logger, peer.Id, payload.Length, channelNumber, deliveryMethod);

		if (payload.Length == 0) return;

		if (!MessageCodec.TryReadMessageType(payload, out MessageType messageType)) return;

		switch (messageType)
		{
			case MessageType.HostRegister:
			{
				HandleHostRegister(session, payload);

				break;
			}

			case MessageType.HostClaim:
			{
				HandleHostClaim(session, payload);

				break;
			}

			case MessageType.HostPromotionAck:
			{
				HandleHostPromotionAck(session, payload);

				break;
			}

			case MessageType.ClientJoin:
			{
				HandleClientJoin(session, payload);

				break;
			}

			case MessageType.Data:
			{
				HandleData(session, payload, deliveryMethod);

				break;
			}

			case MessageType.Kick:
			{
				HandleKick(session, payload);

				break;
			}

			default:
			{
				Log.UnknownRelayMessageType(_logger, peer.Id, payload[0]);

				break;
			}
		}
	}

	private void HandleNetworkError(IPEndPoint endPoint, SocketError socketError)
	{
		Log.LiteNetLibSocketError(_logger, endPoint, socketError);
	}

	private void HandleNetworkLatencyUpdate(NetPeer peer, int latency)
	{
		Log.PeerLatencyUpdated(_logger, peer.Id, latency);
	}

	private void HandleHostRegister(PeerConnection session, ReadOnlySpan<byte> payload)
	{
		if (!MessageCodec.TryReadHostRegister(payload, out ushort maximumClients))
		{
			Log.InvalidHostRegister(_logger, session.Peer.Id);

			Send(session, MessageCodec.GetErrorMessage(ErrorCode.RoomNotFound), DeliveryMethod.ReliableOrdered);

			return;
		}

		if (maximumClients < 1)
		{
			Log.InvalidHostRegisterMaximumClients(_logger, session.Peer.Id, maximumClients);

			Send(session, MessageCodec.GetErrorMessage(ErrorCode.InvalidMaximumClients), DeliveryMethod.ReliableOrdered);

			return;
		}

		lock (_mutex)
		{
			if (session.Role != PeerRole.None)
			{
				Log.HostRegisterWhileAlreadyAssignedRole(_logger, session.Peer.Id, session.Role);

				return;
			}

			string roomCode;

			do
			{
				roomCode = RoomCode.Generate();
			}
			while (_roomsByCode.ContainsKey(roomCode));

			Room room = new()
			{
				Code = roomCode,

				Kind = RoomKind.EphemeralHostOwned,

				Host = session,

				HostToken = GenerateRandomToken(),

				MaximumClients = maximumClients,
			};

			_roomsByCode.Add(roomCode, room);

			session.Role = PeerRole.Host;

			session.Room = room;

			Log.RoomCreated(_logger, roomCode, session.Peer.Id, room.MaximumClients);

			Send(session, MessageCodec.CreateRoomCreated(roomCode, room.HostToken), DeliveryMethod.ReliableOrdered);
		}
	}

	private void HandleHostClaim(PeerConnection session, ReadOnlySpan<byte> payload)
	{
		if (!MessageCodec.TryReadHostClaim(payload, out string roomCode, out string claimToken))
		{
			Send(session, MessageCodec.GetErrorMessage(ErrorCode.InvalidHostClaim), DeliveryMethod.ReliableOrdered);

			return;
		}

		lock (_mutex)
		{
			if (session.Role != PeerRole.None)
			{
				Log.HostRegisterWhileAlreadyAssignedRole(_logger, session.Peer.Id, session.Role);

				return;
			}

			if (!_roomsByCode.TryGetValue(roomCode, out Room? room))
			{
				Send(session, MessageCodec.GetErrorMessage(ErrorCode.RoomNotFound), DeliveryMethod.ReliableOrdered);

				return;
			}

			ExpirePendingHostClaimLocked(room, DateTimeOffset.UtcNow);

			if (room.Kind != RoomKind.PersistentReserved || room.HasActiveHost || !room.HasPendingHostClaim || !string.Equals(room.PendingHostClaimToken, claimToken, StringComparison.OrdinalIgnoreCase))
			{
				Send(session, MessageCodec.GetErrorMessage(ErrorCode.InvalidHostClaim), DeliveryMethod.ReliableOrdered);

				return;
			}

			room.ClearPendingHostClaim();

			room.Host = session;

			room.HostToken = GenerateRandomToken();

			session.Role = PeerRole.Host;

			session.Room = room;

			Log.RoomCreated(_logger, room.Code, session.Peer.Id, room.MaximumClients);

			Send(session, MessageCodec.CreateRoomCreated(room.Code, room.HostToken!), DeliveryMethod.ReliableOrdered);

			foreach (PeerConnection client in room.ClientsByVirtualId.Values)
			{
				Send(client, MessageCodec.GetHostAvailableMessage(), DeliveryMethod.ReliableOrdered);

				Send(session, MessageCodec.CreateConnected(client.VirtualClientId), DeliveryMethod.ReliableOrdered);
			}
		}
	}

	private void HandleHostPromotionAck(PeerConnection session, ReadOnlySpan<byte> payload)
	{
		if (!MessageCodec.TryReadHostPromotionAck(payload, out string roomCode, out string claimToken)) return;

		lock (_mutex)
		{
			if (session.Role != PeerRole.Client || session.Room is null) return;

			if (!_roomsByCode.TryGetValue(roomCode, out Room? room)) return;

			ExpirePendingHostClaimLocked(room, DateTimeOffset.UtcNow);

			if (room.Kind != RoomKind.PersistentReserved || !room.HasPendingHostClaim || !ReferenceEquals(room.PendingHostPromotionPeer, session) || !ReferenceEquals(session.Room, room) || !string.Equals(room.PendingHostClaimToken, claimToken, StringComparison.OrdinalIgnoreCase)) return;

			room.PendingHostPromotionAckReceived = true;

			DisconnectPeer(session);
		}
	}

	private void HandleClientJoin(PeerConnection session, ReadOnlySpan<byte> payload)
	{
		if (!MessageCodec.TryReadClientJoin(payload, out string roomCode))
		{
			Log.InvalidClientJoin(_logger, session.Peer.Id);

			Send(session, MessageCodec.GetErrorMessage(ErrorCode.RoomNotFound), DeliveryMethod.ReliableOrdered);

			return;
		}

		lock (_mutex)
		{
			if (session.Role != PeerRole.None)
			{
				Log.ClientJoinWhileAlreadyAssignedRole(_logger, session.Peer.Id, session.Role);

				return;
			}

			if (!_roomsByCode.TryGetValue(roomCode, out Room? room))
			{
				Log.JoinMissingRoom(_logger, session.Peer.Id, roomCode);

				Send(session, MessageCodec.GetErrorMessage(ErrorCode.RoomNotFound), DeliveryMethod.ReliableOrdered);

				return;
			}

			ExpirePendingHostClaimLocked(room, DateTimeOffset.UtcNow);

			if (room.ClientsByVirtualId.Count >= room.MaximumClients)
			{
				Log.JoinFullRoom(_logger, session.Peer.Id, room.Code);

				Send(session, MessageCodec.GetErrorMessage(ErrorCode.RoomFull), DeliveryMethod.ReliableOrdered);

				return;
			}

			int virtualClientId = AddClientToRoomLocked(room, session);

			Log.ClientJoinedRoom(_logger, session.Peer.Id, room.Code, virtualClientId);

			if (room is { Kind: RoomKind.PersistentReserved, HasActiveHost: false })
			{
				if (!room.HasPendingHostClaim)
				{
					_ = TryPromoteNextClientToHostLocked(room);

					return;
				}

				Send(session, MessageCodec.GetJoinSuccessMessage(), DeliveryMethod.ReliableOrdered);

				Send(session, MessageCodec.GetHostUnavailableMessage(), DeliveryMethod.ReliableOrdered);

				return;
			}

			Send(session, MessageCodec.GetJoinSuccessMessage(), DeliveryMethod.ReliableOrdered);

			if (room.Host is not null)
			{
				Send(room.Host, MessageCodec.CreateConnected(virtualClientId), DeliveryMethod.ReliableOrdered);
			}
		}
	}

	private void HandleData(PeerConnection session, ReadOnlySpan<byte> payload, DeliveryMethod deliveryMethod)
	{
		switch (session.Role)
		{
			case PeerRole.Host:
			{
				HandleDataFromHost(session, payload, deliveryMethod);

				break;
			}

			case PeerRole.Client:
			{
				HandleDataFromClient(session, payload, deliveryMethod);

				break;
			}

			default:
			{
				Log.RelayDataWithoutRoomRole(_logger, session.Peer.Id);

				break;
			}
		}
	}

	private void HandleDataFromHost(PeerConnection hostSession, ReadOnlySpan<byte> payload, DeliveryMethod deliveryMethod)
	{
		if (payload.Length < MessageCodec.HostDataHeaderSize || payload[0] != (byte)MessageType.Data)
		{
			Log.InvalidHostDataPayload(_logger, hostSession.Peer.Id);

			return;
		}

		MessageCodec.ReadHostData(payload, out int targetVirtualClientId, out byte gameChannel, out ReadOnlySpan<byte> gamePayload);

		if (gameChannel is not ((byte)GameChannel.Reliable or (byte)GameChannel.Unreliable) || deliveryMethod != MapDeliveryMethod(gameChannel)) return;

		bool isBroadcast = targetVirtualClientId == MessageCodec.BroadcastVirtualClientId;

		PeerConnection? targetClient = null;

		PeerConnection[]? broadcastClients = null;

		int broadcastRecipientCount = 0;

		lock (_mutex)
		{
			Room? room = hostSession.Room;

			if (room is null || !room.HasActiveHost || !ReferenceEquals(room.Host, hostSession))
			{
				Log.HostDataWithoutAssignedRoom(_logger, hostSession.Peer.Id);

				return;
			}

			if (isBroadcast)
			{
				broadcastRecipientCount = room.ClientsByVirtualId.Count;

				if (broadcastRecipientCount == 1)
				{
					foreach (PeerConnection client in room.ClientsByVirtualId.Values)
					{
						targetClient = client;

						break;
					}
				}
				else if (broadcastRecipientCount > 1)
				{
					broadcastClients = ArrayPool<PeerConnection>.Shared.Rent(broadcastRecipientCount);

					room.ClientsByVirtualId.Values.CopyTo(broadcastClients, 0);
				}
			}
			else if (!room.ClientsByVirtualId.TryGetValue(targetVirtualClientId, out targetClient))
			{
				Log.HostTargetedUnknownVirtualClient(_logger, hostSession.Peer.Id, targetVirtualClientId);

				return;
			}
		}

		if (isBroadcast)
		{
			if (broadcastRecipientCount == 0)
			{
				Log.HostBroadcast(_logger, hostSession.Peer.Id, gamePayload.Length, gameChannel, broadcastRecipientCount);

				return;
			}

			if (broadcastClients is null)
			{
				if (targetClient is not null) SendClientData(targetClient, gameChannel, gamePayload, deliveryMethod);
			}
			else
			{
				try
				{
					SendClientData(broadcastClients.AsSpan(0, broadcastRecipientCount), gameChannel, gamePayload, deliveryMethod);
				}
				finally
				{
					Array.Clear(broadcastClients, 0, broadcastRecipientCount);

					ArrayPool<PeerConnection>.Shared.Return(broadcastClients);
				}
			}

			Log.HostBroadcast(_logger, hostSession.Peer.Id, gamePayload.Length, gameChannel, broadcastRecipientCount);

			return;
		}

		if (targetClient is not null) SendClientData(targetClient, gameChannel, gamePayload, deliveryMethod);
	}

	private void HandleDataFromClient(PeerConnection clientSession, ReadOnlySpan<byte> payload, DeliveryMethod deliveryMethod)
	{
		if (payload.Length < MessageCodec.ClientDataHeaderSize || payload[0] != (byte)MessageType.Data)
		{
			Log.InvalidClientDataPayload(_logger, clientSession.Peer.Id);

			return;
		}

		MessageCodec.ReadClientData(payload, out byte gameChannel, out ReadOnlySpan<byte> gamePayload);

		if (gameChannel is not ((byte)GameChannel.Reliable or (byte)GameChannel.Unreliable) || deliveryMethod != MapDeliveryMethod(gameChannel)) return;

		PeerConnection host;

		int virtualClientId;

		lock (_mutex)
		{
			Room? room = clientSession.Room;

			if (room is null || !room.HasActiveHost || room.Host is not { } activeHost)
			{
				Log.ClientDataWithoutActiveHostRoom(_logger, clientSession.Peer.Id);

				return;
			}

			host = activeHost;

			virtualClientId = clientSession.VirtualClientId;
		}

		SendHostData(host, virtualClientId, gameChannel, gamePayload, deliveryMethod);
	}

	private void HandleKick(PeerConnection session, ReadOnlySpan<byte> payload)
	{
		if (session.Role != PeerRole.Host)
		{
			Log.KickWithoutHostRole(_logger, session.Peer.Id);

			return;
		}

		if (!MessageCodec.TryReadKick(payload, out int virtualClientId))
		{
			Log.InvalidKickPayload(_logger, session.Peer.Id);

			return;
		}

		lock (_mutex)
		{
			Room? room = session.Room;

			if (room is null || !room.HasActiveHost || !ReferenceEquals(room.Host, session)) return;

			if (!room.ClientsByVirtualId.TryGetValue(virtualClientId, out PeerConnection? clientSession))
			{
				Log.KickUnknownVirtualClient(_logger, session.Peer.Id, virtualClientId);

				return;
			}

			Log.ClientKickedByHost(_logger, session.Peer.Id, virtualClientId, clientSession.Peer.Id);

			_ = TryRemoveAndDisconnectClientLocked(room, virtualClientId);
		}
	}

	private bool TryRemoveAndDisconnectClientLocked(Room room, int virtualClientId)
	{
		if (!room.ClientsByVirtualId.Remove(virtualClientId, out PeerConnection? clientSession)) return false;

		Send(clientSession, MessageCodec.CreateDisconnected(virtualClientId), DeliveryMethod.ReliableOrdered);

		clientSession.Clear();

		DisconnectPeer(clientSession);

		return true;
	}

	private void ExpirePendingHostClaims()
	{
		lock (_mutex)
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;

			foreach (Room room in _roomsByCode.Values)
			{
				ExpirePendingHostClaimLocked(room, now);
			}
		}
	}

	private void ExpirePendingHostClaimLocked(Room room, DateTimeOffset now)
	{
		if (!room.HasPendingHostClaim || room.PendingHostClaimExpiresAtUtc > now) return;

		PeerConnection? pendingHostPromotionPeer = room.PendingHostPromotionPeer;

		room.ClearPendingHostClaim();

		if (pendingHostPromotionPeer is not null)
		{
			pendingHostPromotionPeer.Clear();

			DisconnectPeer(pendingHostPromotionPeer);
		}

		NotifyClientsHostAvailabilityLocked(room, isAvailable: false);

		_ = TryPromoteNextClientToHostLocked(room);
	}

	private static int AddClientToRoomLocked(Room room, PeerConnection session)
	{
		int virtualClientId = room.AllocateVirtualClientId();

		room.ClientsByVirtualId.Add(virtualClientId, session);

		session.Role = PeerRole.Client;

		session.Room = room;

		session.VirtualClientId = virtualClientId;

		session.RoomJoinOrder = room.AllocateJoinOrder();

		return virtualClientId;
	}

	private bool TryPromoteNextClientToHostLocked(Room room)
	{
		if (room.Kind != RoomKind.PersistentReserved || room.HasActiveHost || room.HasPendingHostClaim) return false;

		PeerConnection? candidate = null;

		foreach (PeerConnection client in room.ClientsByVirtualId.Values)
		{
			if (candidate is null || client.RoomJoinOrder < candidate.RoomJoinOrder) candidate = client;
		}

		if (candidate is null) return false;

		room.ClientsByVirtualId.Remove(candidate.VirtualClientId);

		string claimToken = GenerateRandomToken();

		room.PendingHostClaimToken = claimToken;

		room.PendingHostClaimExpiresAtUtc = DateTimeOffset.UtcNow.Add(HostClaimTimeout);

		room.PendingHostPromotionPeer = candidate;

		room.PendingHostPromotionVirtualClientId = candidate.VirtualClientId;

		Send(candidate, MessageCodec.CreateHostPromoted(room.Code, room.MaximumClients, claimToken), DeliveryMethod.ReliableOrdered);

		return true;
	}

	private void NotifyClientsHostAvailabilityLocked(Room room, bool isAvailable)
	{
		ReadOnlySpan<byte> payload = isAvailable ? MessageCodec.GetHostAvailableMessage() : MessageCodec.GetHostUnavailableMessage();

		foreach (PeerConnection client in room.ClientsByVirtualId.Values)
		{
			Send(client, payload, DeliveryMethod.ReliableOrdered);
		}
	}

	private void CloseRoomLocked(Room room, ErrorCode errorCode)
	{
		_roomsByCode.Remove(room.Code);

		PeerConnection? pendingHostPromotionPeer = room.PendingHostPromotionPeer;

		room.ClearPendingHostClaim();

		if (pendingHostPromotionPeer is not null)
		{
			Send(pendingHostPromotionPeer, MessageCodec.GetErrorMessage(errorCode), DeliveryMethod.ReliableOrdered);

			pendingHostPromotionPeer.Clear();

			DisconnectPeer(pendingHostPromotionPeer);
		}

		if (room.Host is not null)
		{
			Send(room.Host, MessageCodec.GetErrorMessage(errorCode), DeliveryMethod.ReliableOrdered);

			room.Host.Clear();

			DisconnectPeer(room.Host);

			room.Host = null;
		}

		foreach (PeerConnection client in room.ClientsByVirtualId.Values)
		{
			Send(client, MessageCodec.GetErrorMessage(errorCode), DeliveryMethod.ReliableOrdered);

			client.Clear();

			DisconnectPeer(client);
		}

		room.ClientsByVirtualId.Clear();
	}

	private static string GenerateRandomToken()
	{
		Span<byte> bytes = stackalloc byte[16];

		RandomNumberGenerator.Fill(bytes);

		return Convert.ToHexString(bytes);
	}

	private static DeliveryMethod MapDeliveryMethod(byte gameChannel)
	{
		return MessageCodec.IsUnreliableGameChannel(gameChannel) ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered;
	}

	private static PeerConnection GetOrCreateSession(NetPeer peer)
	{
		PeerConnection session = peer.Tag as PeerConnection ?? new PeerConnection(peer);

		peer.Tag = session;

		return session;
	}

	private void SendClientData(PeerConnection client, byte gameChannel, ReadOnlySpan<byte> gamePayload, DeliveryMethod deliveryMethod)
	{
		int payloadLength = RelayBackpressurePolicy.ClientRelayPayloadLength(gamePayload.Length);

		if (ShouldDropUnreliableRelayPayload(client, payloadLength, deliveryMethod)) return;

		if (ShouldDisconnectRecipientForReliableBacklog(client, payloadLength, deliveryMethod)) return;

		if (payloadLength <= client.Peer.GetMaxSinglePacketSize(deliveryMethod))
		{
			PooledPacket pooledPacket = client.Peer.CreatePacketFromPool(deliveryMethod, MessageCodec.RelayWireChannel);

			int written = MessageCodec.WriteClientData(pooledPacket.Data.AsSpan(pooledPacket.UserDataOffset, payloadLength), gameChannel, gamePayload);

			client.Peer.SendPooledPacket(pooledPacket, written);

			_netManager.TriggerUpdate();

			return;
		}

		if (payloadLength <= StackallocRelayFrameThreshold)
		{
			Span<byte> payload = stackalloc byte[payloadLength];

			MessageCodec.WriteClientData(payload, gameChannel, gamePayload);

			Send(client, payload, deliveryMethod);

			return;
		}

		byte[] rentedPayload = ArrayPool<byte>.Shared.Rent(payloadLength);

		try
		{
			Span<byte> payload = rentedPayload.AsSpan(0, payloadLength);

			MessageCodec.WriteClientData(payload, gameChannel, gamePayload);

			Send(client, payload, deliveryMethod);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedPayload);
		}
	}

	private void SendClientData(Span<PeerConnection> clients, byte gameChannel, ReadOnlySpan<byte> gamePayload, DeliveryMethod deliveryMethod)
	{
		if (clients.Length == 0) return;

		int payloadLength = RelayBackpressurePolicy.ClientRelayPayloadLength(gamePayload.Length);

		int clientCount = FilterRelayRecipients(clients, payloadLength, deliveryMethod);

		if (clientCount == 0) return;

		bool allRecipientsCanSendSinglePacket = true;

		for (int i = 0; i < clientCount; i++)
		{
			if (payloadLength <= clients[i].Peer.GetMaxSinglePacketSize(deliveryMethod)) continue;

			allRecipientsCanSendSinglePacket = false;

			break;
		}

		if (allRecipientsCanSendSinglePacket)
		{
			for (int i = 0; i < clientCount; i++)
			{
				PooledPacket pooledPacket = clients[i].Peer.CreatePacketFromPool(deliveryMethod, MessageCodec.RelayWireChannel);

				int written = MessageCodec.WriteClientData(pooledPacket.Data.AsSpan(pooledPacket.UserDataOffset, payloadLength), gameChannel, gamePayload);

				clients[i].Peer.SendPooledPacket(pooledPacket, written);

				_netManager.TriggerUpdate();
			}

			return;
		}

		if (payloadLength <= StackallocRelayFrameThreshold)
		{
			Span<byte> payload = stackalloc byte[payloadLength];

			MessageCodec.WriteClientData(payload, gameChannel, gamePayload);

			for (int i = 0; i < clientCount; i++)
			{
				Send(clients[i], payload, deliveryMethod);
			}

			return;
		}

		byte[] rentedPayload = ArrayPool<byte>.Shared.Rent(payloadLength);

		try
		{
			Span<byte> payload = rentedPayload.AsSpan(0, payloadLength);

			MessageCodec.WriteClientData(payload, gameChannel, gamePayload);

			for (int i = 0; i < clientCount; i++)
			{
				Send(clients[i], payload, deliveryMethod);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedPayload);
		}
	}

	private void SendHostData(PeerConnection host, int virtualClientId, byte gameChannel, ReadOnlySpan<byte> gamePayload, DeliveryMethod deliveryMethod)
	{
		int payloadLength = RelayBackpressurePolicy.HostRelayPayloadLength(gamePayload.Length);

		if (ShouldDropUnreliableRelayPayload(host, payloadLength, deliveryMethod)) return;

		if (ShouldDisconnectRecipientForReliableBacklog(host, payloadLength, deliveryMethod)) return;

		if (payloadLength <= host.Peer.GetMaxSinglePacketSize(deliveryMethod))
		{
			PooledPacket pooledPacket = host.Peer.CreatePacketFromPool(deliveryMethod, MessageCodec.RelayWireChannel);

			int written = MessageCodec.WriteHostData(pooledPacket.Data.AsSpan(pooledPacket.UserDataOffset, payloadLength), virtualClientId, gameChannel, gamePayload);

			host.Peer.SendPooledPacket(pooledPacket, written);

			_netManager.TriggerUpdate();

			return;
		}

		if (payloadLength <= StackallocRelayFrameThreshold)
		{
			Span<byte> payload = stackalloc byte[payloadLength];

			MessageCodec.WriteHostData(payload, virtualClientId, gameChannel, gamePayload);

			Send(host, payload, deliveryMethod);

			return;
		}

		byte[] rentedPayload = ArrayPool<byte>.Shared.Rent(payloadLength);

		try
		{
			Span<byte> payload = rentedPayload.AsSpan(0, payloadLength);

			MessageCodec.WriteHostData(payload, virtualClientId, gameChannel, gamePayload);

			Send(host, payload, deliveryMethod);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(rentedPayload);
		}
	}

	private int FilterRelayRecipients(Span<PeerConnection> recipients, int payloadLength, DeliveryMethod deliveryMethod)
	{
		int writeIndex = 0;

		for (int readIndex = 0; readIndex < recipients.Length; readIndex++)
		{
			PeerConnection recipient = recipients[readIndex];

			if (ShouldDropUnreliableRelayPayload(recipient, payloadLength, deliveryMethod)) continue;

			if (ShouldDisconnectRecipientForReliableBacklog(recipient, payloadLength, deliveryMethod)) continue;

			recipients[writeIndex++] = recipient;
		}

		return writeIndex;
	}

	private bool ShouldDropUnreliableRelayPayload(PeerConnection recipient, int payloadLength, DeliveryMethod deliveryMethod)
	{
		if (deliveryMethod != DeliveryMethod.Unreliable) return false;

		int maxUnreliablePayloadLength = Math.Min(NetConstants.MaxUnreliableDataSize, recipient.Peer.GetMaxSinglePacketSize(deliveryMethod));

		if (payloadLength > maxUnreliablePayloadLength)
		{
			Log.OversizedUnreliableRelayPayloadDropped(_logger, recipient.Peer.Id, payloadLength, maxUnreliablePayloadLength);

			return true;
		}

		int reliableQueuePackets = recipient.Peer.GetPacketsCountInReliableQueue(MessageCodec.RelayWireChannel, true);

		if (!RelayBackpressurePolicy.ShouldDropUnreliableForReliableBackpressure(reliableQueuePackets, deliveryMethod)) return false;

		Log.UnreliableRelayPayloadDroppedDueToBackpressure(_logger, recipient.Peer.Id, payloadLength, reliableQueuePackets, RelayBackpressurePolicy.ReliableQueuePacketsBeforeDroppingUnreliable);

		return true;
	}

	private bool ShouldDisconnectRecipientForReliableBacklog(PeerConnection recipient, int payloadLength, DeliveryMethod deliveryMethod)
	{
		if (deliveryMethod != DeliveryMethod.ReliableOrdered) return false;

		int reliableQueuePackets = recipient.Peer.GetPacketsCountInReliableQueue(MessageCodec.RelayWireChannel, true);

		if (!RelayBackpressurePolicy.ShouldDisconnectReliableForBackpressure(reliableQueuePackets, deliveryMethod)) return false;

		Log.ReliableRelayRecipientDisconnectedDueToBackpressure(_logger, recipient.Peer.Id, payloadLength, reliableQueuePackets, RelayBackpressurePolicy.ReliableQueuePacketsBeforeDisconnect);

		DisconnectPeer(recipient);

		return true;
	}

	private void Send(PeerConnection session, ReadOnlySpan<byte> payload, DeliveryMethod deliveryMethod)
	{
		try
		{
			session.Peer.Send(payload, MessageCodec.RelayWireChannel, deliveryMethod);

			_netManager.TriggerUpdate();
		}
		catch (TooBigPacketException tooBigPacketException)
		{
			Log.RelayPayloadRejectedAsTooLarge(_logger, session.Peer.Id, payload.Length, deliveryMethod, tooBigPacketException);
		}
		catch (Exception exception)
		{
			Log.FailedToSendRelayPayload(_logger, session.Peer.Id, deliveryMethod, exception);
		}
	}

	private void DisconnectPeer(PeerConnection session)
	{
		try
		{
			_netManager.DisconnectPeer(session.Peer);
		}
		catch (Exception exception)
		{
			Log.FailedToDisconnectPeer(_logger, session.Peer.Id, exception);
		}
	}
}
