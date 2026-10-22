using BlitzRelay.Networking;

namespace BlitzRelay.Rooms;

internal sealed record ClientIpEndpointSnapshot
(
	int VirtualClientId,
	string IpAddress,
	int Port
)
	: IClientSnapshot<ClientIpEndpointSnapshot>
{
	public static ClientIpEndpointSnapshot FromClient(PeerConnection client)
	{
		return new ClientIpEndpointSnapshot
		(
			client.VirtualClientId,
			client.Peer.Address.ToString(),
			client.Peer.Port
		);
	}
}
