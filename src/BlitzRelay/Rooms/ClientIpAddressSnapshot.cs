using BlitzRelay.Networking;

namespace BlitzRelay.Rooms;

internal sealed record ClientIpAddressSnapshot
(
	int VirtualClientId,
	string IpAddress
)
	: IClientSnapshot<ClientIpAddressSnapshot>
{
	public static ClientIpAddressSnapshot FromClient(PeerConnection client)
	{
		return new ClientIpAddressSnapshot
		(
			client.VirtualClientId,
			client.Peer.Address.ToString()
		);
	}
}
