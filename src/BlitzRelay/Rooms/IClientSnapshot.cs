using BlitzRelay.Networking;

namespace BlitzRelay.Rooms;

internal interface IClientSnapshot<out TSelf> where TSelf : IClientSnapshot<TSelf>
{
	static abstract TSelf FromClient(PeerConnection client);
}
