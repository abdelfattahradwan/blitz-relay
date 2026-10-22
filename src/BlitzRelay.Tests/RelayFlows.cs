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
}
