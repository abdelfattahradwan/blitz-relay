using BlitzRelay.Protocol;
using BlitzRelay.Rooms;
using LiteNetLib;

namespace BlitzRelay.Tests;

[TestClass]
public sealed class RelayServerTests
{
	private const int LargestUnreliableGamePayload = 1350 - 1 - MessageCodec.MaxRelayHeaderSize;

	[TestMethod]
	public async Task Connect_WhenConnectionKeyIsWrong_IsRejected()
	{
		await using TestRelay relay = new();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RelayTestPeer.ConnectAsync(relay.Server.LocalPort, "wrong-key"));
	}

	[TestMethod]
	public async Task HostRegister_CreatesEphemeralRoomWithHost()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		RoomSnapshot? snapshot = relay.Server.GetRoomSnapshot(roomCode);

		Assert.IsNotNull(snapshot);

		Assert.AreEqual(RoomKind.EphemeralHostOwned, snapshot.Kind);

		Assert.AreEqual(4, snapshot.MaximumClients);

		Assert.IsTrue(snapshot.HasHost);
	}

	[TestMethod]
	public async Task HostRegister_WhenMalformed_ReturnsInvalidMaximumClients()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		host.Send([(byte)MessageType.HostRegister]);

		Assert.IsTrue(MessageCodec.TryReadError(await host.ReceiveAsync(), out ErrorCode errorCode));

		Assert.AreEqual(ErrorCode.InvalidMaximumClients, errorCode);
	}

	[TestMethod]
	public async Task HostRegister_WhenMaximumClientsIsZero_ReturnsInvalidMaximumClients()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		host.Send(MessageCodec.CreateHostRegister(0));

		Assert.IsTrue(MessageCodec.TryReadError(await host.ReceiveAsync(), out ErrorCode errorCode));

		Assert.AreEqual(ErrorCode.InvalidMaximumClients, errorCode);
	}

	[TestMethod]
	public async Task ClientJoin_WhenRoomIsMissing_ReturnsRoomNotFound()
	{
		await using TestRelay relay = new();

		using RelayTestPeer client = await relay.ConnectAsync();

		client.Send(MessageCodec.CreateClientJoin("MISSING1"));

		Assert.IsTrue(MessageCodec.TryReadError(await client.ReceiveAsync(), out ErrorCode errorCode));

		Assert.AreEqual(ErrorCode.RoomNotFound, errorCode);
	}

	[TestMethod]
	public async Task ClientJoin_WhenRoomIsFull_ReturnsRoomFull()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer first = await relay.ConnectAsync();

		using RelayTestPeer second = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 1);

		await JoinAsync(host, first, roomCode);

		second.Send(MessageCodec.CreateClientJoin(roomCode));

		Assert.IsTrue(MessageCodec.TryReadError(await second.ReceiveAsync(), out ErrorCode errorCode));

		Assert.AreEqual(ErrorCode.RoomFull, errorCode);
	}

	[TestMethod]
	[DataRow(GameChannel.Reliable, DeliveryMethod.ReliableOrdered)]
	[DataRow(GameChannel.Unreliable, DeliveryMethod.Unreliable)]
	[DataRow(GameChannel.UnreliableSequenced, DeliveryMethod.Sequenced)]
	public async Task Data_IsRelayedInBothDirections(GameChannel gameChannel, DeliveryMethod deliveryMethod)
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		byte[] clientPayload = [1, 2, 3];

		byte[] hostPayload = [4, 5, 6, 7];

		client.Send(MessageCodec.CreateClientData((byte)gameChannel, clientPayload), deliveryMethod);

		await AssertHostDataAsync(host, virtualClientId, gameChannel, clientPayload);

		host.Send(MessageCodec.CreateHostData(virtualClientId, (byte)gameChannel, hostPayload), deliveryMethod);

		await AssertClientDataAsync(client, gameChannel, hostPayload);
	}

	[TestMethod]
	public async Task UnreliableData_UpToTheTransportMtu_IsRelayedInBothDirections()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		byte[] gamePayload = new byte[LargestUnreliableGamePayload];

		Random.Shared.NextBytes(gamePayload);

		client.Send(MessageCodec.CreateClientData((byte)GameChannel.Unreliable, gamePayload), DeliveryMethod.Unreliable);

		await AssertHostDataAsync(host, virtualClientId, GameChannel.Unreliable, gamePayload);

		host.Send(MessageCodec.CreateHostData(virtualClientId, (byte)GameChannel.Unreliable, gamePayload), DeliveryMethod.Unreliable);

		await AssertClientDataAsync(client, GameChannel.Unreliable, gamePayload);
	}

	[TestMethod]
	public async Task Data_WhenDeliveryMethodDoesNotMatchGameChannel_IsDropped()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		client.Send(MessageCodec.CreateClientData((byte)GameChannel.Reliable, [1]), DeliveryMethod.Unreliable);

		client.Send(MessageCodec.CreateClientData((byte)GameChannel.Reliable, [2]));

		await AssertHostDataAsync(host, virtualClientId, GameChannel.Reliable, [2]);

		await host.AssertNothingReceivedAsync();
	}

	[TestMethod]
	public async Task HostData_IsRelayedOnlyToTargetClient()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer target = await relay.ConnectAsync();

		using RelayTestPeer other = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int targetVirtualClientId = await JoinAsync(host, target, roomCode);

		await JoinAsync(host, other, roomCode);

		host.Send(MessageCodec.CreateHostData(targetVirtualClientId, (byte)GameChannel.Reliable, [9]));

		await AssertClientDataAsync(target, GameChannel.Reliable, [9]);

		await other.AssertNothingReceivedAsync();
	}

	[TestMethod]
	public async Task HostBroadcast_IsRelayedToEveryClient()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer first = await relay.ConnectAsync();

		using RelayTestPeer second = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		await JoinAsync(host, first, roomCode);

		await JoinAsync(host, second, roomCode);

		host.Send(MessageCodec.CreateHostData(MessageCodec.BroadcastVirtualClientId, (byte)GameChannel.Reliable, [7]));

		await AssertClientDataAsync(first, GameChannel.Reliable, [7]);

		await AssertClientDataAsync(second, GameChannel.Reliable, [7]);
	}

	[TestMethod]
	public async Task ClientDisconnect_NotifiesHost()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		client.Disconnect();

		Assert.IsTrue(MessageCodec.TryReadDisconnected(await host.ReceiveAsync(), out int disconnectedVirtualClientId));

		Assert.AreEqual(virtualClientId, disconnectedVirtualClientId);
	}

	[TestMethod]
	public async Task EphemeralHostDisconnect_ClosesRoomAndDisconnectsClients()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		host.Disconnect();

		Assert.IsTrue(MessageCodec.TryReadDisconnected(await client.WaitForDisconnectAsync(), out int disconnectedVirtualClientId));

		Assert.AreEqual(virtualClientId, disconnectedVirtualClientId);

		Assert.IsNull(relay.Server.GetRoomSnapshot(roomCode));
	}

	[TestMethod]
	public async Task HostKick_DisconnectsClientAndNotifiesHost()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		host.Send(MessageCodec.CreateKick(virtualClientId));

		await AssertKickedAsync(host, client, virtualClientId);
	}

	[TestMethod]
	public async Task AdminKick_DisconnectsClientAndNotifiesHost()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		Assert.IsTrue(relay.Server.TryKickClient(roomCode, virtualClientId));

		await AssertKickedAsync(host, client, virtualClientId);
	}

	[TestMethod]
	public async Task ReservedRoom_PromotesFirstClient_WhichClaimsTheRoomAsHost()
	{
		await using TestRelay relay = new();

		RoomSnapshot reservedRoom = relay.Server.CreateReservedRoom(4, "Reserved", false, null);

		using RelayTestPeer promoted = await relay.ConnectAsync();

		promoted.Send(MessageCodec.CreateClientJoin(reservedRoom.Code));

		Assert.IsTrue(MessageCodec.TryReadHostPromoted(await promoted.ReceiveAsync(), out string promotedRoomCode, out int maximumClients, out string claimToken));

		Assert.AreEqual(reservedRoom.Code, promotedRoomCode);

		Assert.AreEqual(4, maximumClients);

		promoted.Send(MessageCodec.CreateHostPromotionAck(promotedRoomCode, claimToken));

		await promoted.WaitForDisconnectAsync();

		using RelayTestPeer host = await relay.ConnectAsync();

		host.Send(MessageCodec.CreateHostClaim(promotedRoomCode, claimToken));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string roomCode, out _));

		Assert.AreEqual(reservedRoom.Code, roomCode);

		using RelayTestPeer client = await relay.ConnectAsync();

		await JoinAsync(host, client, roomCode);

		RoomSnapshot? snapshot = relay.Server.GetRoomSnapshot(roomCode);

		Assert.IsNotNull(snapshot);

		Assert.IsTrue(snapshot.HasHost);

		Assert.AreEqual(1, snapshot.ConnectedClientCount);
	}

	[TestMethod]
	public async Task ReservedRoom_WhenHostClaimExpires_PromotesNextClient()
	{
		await using TestRelay relay = new();

		RoomSnapshot reservedRoom = relay.Server.CreateReservedRoom(4, "Reserved", false, null);

		using RelayTestPeer first = await relay.ConnectAsync();

		using RelayTestPeer second = await relay.ConnectAsync();

		first.Send(MessageCodec.CreateClientJoin(reservedRoom.Code));

		Assert.IsTrue(MessageCodec.TryReadHostPromoted(await first.ReceiveAsync(), out _, out _, out _));

		second.Send(MessageCodec.CreateClientJoin(reservedRoom.Code));

		Assert.IsTrue(MessageCodec.TryReadJoinSuccess(await second.ReceiveAsync()));

		Assert.IsTrue(MessageCodec.TryReadHostUnavailable(await second.ReceiveAsync()));

		relay.TimeProvider.Advance(TimeSpan.FromSeconds(10));

		await first.WaitForDisconnectAsync();

		Assert.IsTrue(MessageCodec.TryReadHostUnavailable(await second.ReceiveAsync()));

		Assert.IsTrue(MessageCodec.TryReadHostPromoted(await second.ReceiveAsync(), out string roomCode, out _, out _));

		Assert.AreEqual(reservedRoom.Code, roomCode);
	}

	[TestMethod]
	public async Task HostClaim_WhenClaimTokenIsWrong_ReturnsInvalidHostClaim()
	{
		await using TestRelay relay = new();

		RoomSnapshot reservedRoom = relay.Server.CreateReservedRoom(4, "Reserved", false, null);

		using RelayTestPeer promoted = await relay.ConnectAsync();

		using RelayTestPeer impostor = await relay.ConnectAsync();

		promoted.Send(MessageCodec.CreateClientJoin(reservedRoom.Code));

		Assert.IsTrue(MessageCodec.TryReadHostPromoted(await promoted.ReceiveAsync(), out _, out _, out string claimToken));

		impostor.Send(MessageCodec.CreateHostClaim(reservedRoom.Code, claimToken.ToLowerInvariant()));

		Assert.IsTrue(MessageCodec.TryReadError(await impostor.ReceiveAsync(), out ErrorCode errorCode));

		Assert.AreEqual(ErrorCode.InvalidHostClaim, errorCode);
	}

	[TestMethod]
	public async Task HasRoomHostToken_OnlyAcceptsTheExactHostToken()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		host.Send(MessageCodec.CreateHostRegister(4));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string roomCode, out string roomHostToken));

		Assert.IsTrue(relay.Server.HasRoomHostToken(roomCode, roomHostToken));

		Assert.IsFalse(relay.Server.HasRoomHostToken(roomCode, roomHostToken.ToLowerInvariant()));

		Assert.IsFalse(relay.Server.HasRoomHostToken(roomCode, roomHostToken[..^1]));

		Assert.IsFalse(relay.Server.HasRoomHostToken("MISSING1", roomHostToken));
	}

	[TestMethod]
	public async Task PatchRoom_UpdatesDisplayNameAndMetadata()
	{
		await using TestRelay relay = new();

		RoomSnapshot reservedRoom = relay.Server.CreateReservedRoom(4, "Before", true, new Dictionary<string, string>
		{
			["mode"] = "casual",
			["map"] = "dunes",
		});

		RoomSnapshot? patched = relay.Server.PatchRoom(reservedRoom.Code, "After", new Dictionary<string, string>
		{
			["mode"] = "ranked",
		}, ["map"]);

		Assert.IsNotNull(patched);

		Assert.AreEqual("After", patched.DisplayName);

		Assert.HasCount(1, patched.Metadata);

		Assert.AreEqual("ranked", patched.Metadata["mode"]);

		Assert.IsNull(relay.Server.PatchRoom("MISSING1", "After", null, null));
	}

	[TestMethod]
	public async Task DeleteRoom_ClosesRoomForParticipants()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		await JoinAsync(host, client, roomCode);

		Assert.IsTrue(relay.Server.DeleteRoom(roomCode));

		Assert.IsTrue(MessageCodec.TryReadError(await host.WaitForDisconnectAsync(), out ErrorCode hostErrorCode));

		Assert.AreEqual(ErrorCode.RoomClosed, hostErrorCode);

		Assert.IsTrue(MessageCodec.TryReadError(await client.WaitForDisconnectAsync(), out ErrorCode clientErrorCode));

		Assert.AreEqual(ErrorCode.RoomClosed, clientErrorCode);

		Assert.IsNull(relay.Server.GetRoomSnapshot(roomCode));

		Assert.IsFalse(relay.Server.DeleteRoom(roomCode));
	}

	[TestMethod]
	public async Task GetRoomClientSnapshots_WhenRoomDoesNotExist_ReturnsNull()
	{
		await using TestRelay relay = new();

		Assert.IsNull(relay.Server.GetRoomClientSnapshots<ClientSnapshot>("MISSING"));
	}

	[TestMethod]
	public async Task GetRoomClientSnapshots_WithIds_ReturnsVirtualClientIds()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		ClientSnapshot[]? clientSnapshots = relay.Server.GetRoomClientSnapshots<ClientSnapshot>(roomCode);

		Assert.IsNotNull(clientSnapshots);

		Assert.AreSequenceEqual([new ClientSnapshot(virtualClientId)], clientSnapshots);
	}

	[TestMethod]
	public async Task GetRoomClientSnapshots_WithIpAddresses_ReturnsVirtualClientIdsAndIpAddresses()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		ClientIpAddressSnapshot[]? clientSnapshots = relay.Server.GetRoomClientSnapshots<ClientIpAddressSnapshot>(roomCode);

		Assert.IsNotNull(clientSnapshots);

		Assert.AreSequenceEqual([new ClientIpAddressSnapshot(virtualClientId, "127.0.0.1")], clientSnapshots);
	}

	[TestMethod]
	public async Task GetRoomClientSnapshots_WithIpEndpoints_ReturnsVirtualClientIdsIpAddressesAndPorts()
	{
		await using TestRelay relay = new();

		using RelayTestPeer host = await relay.ConnectAsync();

		using RelayTestPeer client = await relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		int virtualClientId = await JoinAsync(host, client, roomCode);

		ClientIpEndpointSnapshot[]? clientSnapshots = relay.Server.GetRoomClientSnapshots<ClientIpEndpointSnapshot>(roomCode);

		Assert.IsNotNull(clientSnapshots);

		Assert.AreSequenceEqual([new ClientIpEndpointSnapshot(virtualClientId, "127.0.0.1", client.LocalPort)], clientSnapshots);
	}

	private static async Task<string> RegisterHostAsync(RelayTestPeer host, int maximumClients)
	{
		host.Send(MessageCodec.CreateHostRegister(maximumClients));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string roomCode, out _));

		return roomCode;
	}

	private static async Task<int> JoinAsync(RelayTestPeer host, RelayTestPeer client, string roomCode)
	{
		client.Send(MessageCodec.CreateClientJoin(roomCode));

		Assert.IsTrue(MessageCodec.TryReadJoinSuccess(await client.ReceiveAsync()));

		Assert.IsTrue(MessageCodec.TryReadConnected(await host.ReceiveAsync(), out int virtualClientId));

		return virtualClientId;
	}

	private static async Task AssertHostDataAsync(RelayTestPeer host, int virtualClientId, GameChannel gameChannel, byte[] gamePayload)
	{
		Assert.IsTrue(MessageCodec.TryReadHostData(await host.ReceiveAsync(), out int sourceVirtualClientId, out byte receivedGameChannel, out byte[] receivedGamePayload));

		Assert.AreEqual(virtualClientId, sourceVirtualClientId);

		Assert.AreEqual((byte)gameChannel, receivedGameChannel);

		Assert.AreSequenceEqual(gamePayload, receivedGamePayload);
	}

	private static async Task AssertClientDataAsync(RelayTestPeer client, GameChannel gameChannel, byte[] gamePayload)
	{
		Assert.IsTrue(MessageCodec.TryReadClientData(await client.ReceiveAsync(), out byte receivedGameChannel, out byte[] receivedGamePayload));

		Assert.AreEqual((byte)gameChannel, receivedGameChannel);

		Assert.AreSequenceEqual(gamePayload, receivedGamePayload);
	}

	private static async Task AssertKickedAsync(RelayTestPeer host, RelayTestPeer client, int virtualClientId)
	{
		Assert.IsTrue(MessageCodec.TryReadDisconnected(await client.WaitForDisconnectAsync(), out int clientVirtualClientId));

		Assert.AreEqual(virtualClientId, clientVirtualClientId);

		Assert.IsTrue(MessageCodec.TryReadDisconnected(await host.ReceiveAsync(), out int hostVirtualClientId));

		Assert.AreEqual(virtualClientId, hostVirtualClientId);
	}
}
