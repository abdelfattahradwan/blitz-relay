using BlitzRelay.Networking;

namespace BlitzRelay.Rooms;

internal sealed record ClientSnapshot
(
	int VirtualClientId
)
	: IClientSnapshot<ClientSnapshot>
{
	public static ClientSnapshot FromClient(PeerConnection client)
	{
		return new ClientSnapshot
		(
			client.VirtualClientId
		);
	}
}
