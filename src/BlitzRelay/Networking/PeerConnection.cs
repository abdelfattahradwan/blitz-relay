using BlitzRelay.Rooms;
using LiteNetLib;

namespace BlitzRelay.Networking;

internal sealed class PeerConnection(NetPeer peer)
{
	public NetPeer Peer { get; } = peer;

	public PeerRole Role { get; set; }

	public Room? Room { get; set; }

	public int VirtualClientId { get; set; }

	public long RoomJoinOrder { get; set; }

	public string Endpoint
	{
		get => $"{Peer.Address}:{Peer.Port}";
	}

	public void Clear()
	{
		Role = PeerRole.None;

		Room = null;

		VirtualClientId = 0;

		RoomJoinOrder = 0;
	}
}
