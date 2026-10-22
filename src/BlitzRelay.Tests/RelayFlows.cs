using BlitzRelay.Protocol;

namespace BlitzRelay.Tests;

internal static class RelayFlows
{
	public static async Task<string> RegisterHostAsync(RelayTestPeer host, int maximumClients)
	{
		host.Send(MessageCodec.CreateHostRegister(maximumClients));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string roomCode, out _));

		return roomCode;
	}

	public static async Task<int> JoinAsync(RelayTestPeer host, RelayTestPeer client, string roomCode)
	{
		client.Send(MessageCodec.CreateClientJoin(roomCode));

		Assert.IsTrue(MessageCodec.TryReadJoinSuccess(await client.ReceiveAsync()));

		Assert.IsTrue(MessageCodec.TryReadConnected(await host.ReceiveAsync(), out int virtualClientId));

		return virtualClientId;
	}

	public static async Task JoinWithoutHostAsync(RelayTestPeer client, string roomCode)
	{
		client.Send(MessageCodec.CreateClientJoin(roomCode));

		Assert.IsTrue(MessageCodec.TryReadJoinSuccess(await client.ReceiveAsync()));

		Assert.IsTrue(MessageCodec.TryReadHostUnavailable(await client.ReceiveAsync()));
	}

	public static async Task<string> JoinAsPromotedAsync(RelayTestPeer promoted, string roomCode)
	{
		promoted.Send(MessageCodec.CreateClientJoin(roomCode));

		Assert.IsTrue(MessageCodec.TryReadHostPromoted(await promoted.ReceiveAsync(), out string promotedRoomCode, out _, out string claimToken));

		Assert.AreEqual(roomCode, promotedRoomCode);

		return claimToken;
	}

	public static async Task<string> ClaimAsync(RelayTestPeer promoted, RelayTestPeer host, string roomCode, string claimToken)
	{
		promoted.Send(MessageCodec.CreateHostPromotionAck(roomCode, claimToken));

		await promoted.WaitForDisconnectAsync();

		host.Send(MessageCodec.CreateHostClaim(roomCode, claimToken));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string claimedRoomCode, out string hostToken));

		Assert.AreEqual(roomCode, claimedRoomCode);

		return hostToken;
	}
}
