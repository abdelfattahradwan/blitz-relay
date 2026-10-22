using BlitzRelay.Protocol;

namespace BlitzRelay.Tests;

[TestClass]
public sealed class MessageCodecTests
{
	[TestMethod]
	public void TryReadMessageType_WhenPayloadIsEmpty_ReturnsFalse()
	{
		Assert.IsFalse(MessageCodec.TryReadMessageType([], out MessageType messageType));

		Assert.AreEqual(default(MessageType), messageType);
	}

	[TestMethod]
	public void HostRegister_RoundTrips()
	{
		Assert.IsTrue(MessageCodec.TryReadHostRegister(MessageCodec.CreateHostRegister(ushort.MaxValue), out ushort maximumClients));

		Assert.AreEqual(ushort.MaxValue, maximumClients);
	}

	[TestMethod]
	public void TryReadHostRegister_WhenTruncated_ReturnsFalse()
	{
		Assert.IsFalse(MessageCodec.TryReadHostRegister([(byte)MessageType.HostRegister, 0x01], out _));
	}

	[TestMethod]
	public void ClientJoin_RoundTrips()
	{
		Assert.IsTrue(MessageCodec.TryReadClientJoin(MessageCodec.CreateClientJoin("ABCD2345"), out string roomCode));

		Assert.AreEqual("ABCD2345", roomCode);
	}

	[TestMethod]
	public void TryReadClientJoin_WhenLengthExceedsPayload_ReturnsFalse()
	{
		Assert.IsFalse(MessageCodec.TryReadClientJoin([(byte)MessageType.ClientJoin, 0x05, 0x00, (byte)'A', (byte)'B'], out string roomCode));

		Assert.AreEqual(string.Empty, roomCode);
	}

	[TestMethod]
	public void HostClaim_RoundTrips()
	{
		Assert.IsTrue(MessageCodec.TryReadHostClaim(MessageCodec.CreateHostClaim("ABCD2345", "TOKEN"), out string roomCode, out string claimToken));

		Assert.AreEqual("ABCD2345", roomCode);

		Assert.AreEqual("TOKEN", claimToken);
	}

	[TestMethod]
	public void HostPromotionAck_RoundTrips()
	{
		Assert.IsTrue(MessageCodec.TryReadHostPromotionAck(MessageCodec.CreateHostPromotionAck("ABCD2345", "TOKEN"), out string roomCode, out string claimToken));

		Assert.AreEqual("ABCD2345", roomCode);

		Assert.AreEqual("TOKEN", claimToken);
	}

	[TestMethod]
	public void RoomCreated_RoundTrips()
	{
		Assert.IsTrue(MessageCodec.TryReadRoomCreated(MessageCodec.CreateRoomCreated("ABCD2345", "HOSTTOKEN"), out string roomCode, out string roomHostToken));

		Assert.AreEqual("ABCD2345", roomCode);

		Assert.AreEqual("HOSTTOKEN", roomHostToken);
	}

	[TestMethod]
	public void HostPromoted_RoundTrips()
	{
		Assert.IsTrue(MessageCodec.TryReadHostPromoted(MessageCodec.CreateHostPromoted("ABCD2345", 16, "TOKEN"), out string roomCode, out int maximumClients, out string claimToken));

		Assert.AreEqual("ABCD2345", roomCode);

		Assert.AreEqual(16, maximumClients);

		Assert.AreEqual("TOKEN", claimToken);
	}

	[TestMethod]
	public void VirtualClientMessages_RoundTrip()
	{
		Assert.IsTrue(MessageCodec.TryReadConnected(MessageCodec.CreateConnected(42), out int connectedVirtualClientId));

		Assert.AreEqual(42, connectedVirtualClientId);

		Assert.IsTrue(MessageCodec.TryReadDisconnected(MessageCodec.CreateDisconnected(43), out int disconnectedVirtualClientId));

		Assert.AreEqual(43, disconnectedVirtualClientId);

		Assert.IsTrue(MessageCodec.TryReadKick(MessageCodec.CreateKick(44), out int kickedVirtualClientId));

		Assert.AreEqual(44, kickedVirtualClientId);

		Assert.IsFalse(MessageCodec.TryReadKick(MessageCodec.CreateConnected(42), out _));
	}

	[TestMethod]
	public void EmptyMessages_RoundTrip()
	{
		Assert.IsTrue(MessageCodec.TryReadJoinSuccess(MessageCodec.CreateJoinSuccess()));

		Assert.IsTrue(MessageCodec.TryReadHostAvailable(MessageCodec.CreateHostAvailable()));

		Assert.IsTrue(MessageCodec.TryReadHostUnavailable(MessageCodec.CreateHostUnavailable()));

		Assert.IsFalse(MessageCodec.TryReadHostAvailable(MessageCodec.CreateHostUnavailable()));
	}

	[TestMethod]
	public void HostData_RoundTrips()
	{
		byte[] gamePayload = [1, 2, 3, 4];

		byte[] payload = MessageCodec.CreateHostData(MessageCodec.BroadcastVirtualClientId, (byte)GameChannel.UnreliableSequenced, gamePayload);

		Assert.HasCount(MessageCodec.HostDataHeaderSize + gamePayload.Length, payload);

		Assert.IsTrue(MessageCodec.TryReadHostData(payload, out int virtualClientId, out byte gameChannel, out byte[] readGamePayload));

		Assert.AreEqual(MessageCodec.BroadcastVirtualClientId, virtualClientId);

		Assert.AreEqual((byte)GameChannel.UnreliableSequenced, gameChannel);

		Assert.AreSequenceEqual(gamePayload, readGamePayload);
	}

	[TestMethod]
	public void ClientData_RoundTrips()
	{
		byte[] gamePayload = [5, 6, 7];

		byte[] payload = MessageCodec.CreateClientData((byte)GameChannel.Unreliable, gamePayload);

		Assert.HasCount(MessageCodec.ClientDataHeaderSize + gamePayload.Length, payload);

		Assert.IsTrue(MessageCodec.TryReadClientData(payload, out byte gameChannel, out byte[] readGamePayload));

		Assert.AreEqual((byte)GameChannel.Unreliable, gameChannel);

		Assert.AreSequenceEqual(gamePayload, readGamePayload);
	}

	[TestMethod]
	public void WriteHostData_WhenDestinationIsTooSmall_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => MessageCodec.WriteHostData(new byte[MessageCodec.HostDataHeaderSize], 1, (byte)GameChannel.Reliable, [1]));
	}

	[TestMethod]
	[DataRow(ErrorCode.RoomNotFound)]
	[DataRow(ErrorCode.RoomFull)]
	[DataRow(ErrorCode.RoomExists)]
	[DataRow(ErrorCode.InvalidHostClaim)]
	[DataRow(ErrorCode.RoomClosed)]
	[DataRow(ErrorCode.InvalidMaximumClients)]
	[DataRow(ErrorCode.HostRemoved)]
	[DataRow(ErrorCode.Unknown)]
	public void ErrorMessage_RoundTrips(ErrorCode errorCode)
	{
		Assert.IsTrue(MessageCodec.TryReadError(MessageCodec.GetErrorMessage(errorCode), out ErrorCode readErrorCode));

		Assert.AreEqual(errorCode, readErrorCode);

		Assert.AreSequenceEqual(MessageCodec.CreateError(errorCode), MessageCodec.GetErrorMessage(errorCode).ToArray());
	}

	[TestMethod]
	[DataRow(GameChannel.Reliable, false)]
	[DataRow(GameChannel.Unreliable, true)]
	[DataRow(GameChannel.UnreliableSequenced, true)]
	public void IsUnreliableGameChannel_MatchesGameChannel(GameChannel gameChannel, bool isUnreliable)
	{
		Assert.AreEqual(isUnreliable, MessageCodec.IsUnreliableGameChannel((byte)gameChannel));
	}
}
