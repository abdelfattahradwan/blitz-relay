using BlitzRelay.Protocol;
using LiteNetLib;

namespace BlitzRelay.Networking;

internal static class RelayBackpressurePolicy
{
	public const int ReliableQueuePacketsBeforeDroppingUnreliable = 256;

	public const int ReliableQueuePacketsBeforeDisconnect = 2048;

	public static bool ShouldDropUnreliableForReliableBackpressure(int reliableQueuePackets, DeliveryMethod deliveryMethod)
	{
		return deliveryMethod is DeliveryMethod.Unreliable or DeliveryMethod.Sequenced && reliableQueuePackets >= ReliableQueuePacketsBeforeDroppingUnreliable;
	}

	public static bool ShouldDisconnectReliableForBackpressure(int reliableQueuePackets, DeliveryMethod deliveryMethod)
	{
		return deliveryMethod == DeliveryMethod.ReliableOrdered && reliableQueuePackets >= ReliableQueuePacketsBeforeDisconnect;
	}

	public static int ClientRelayPayloadLength(int gamePayloadLength)
	{
		return MessageCodec.ClientDataHeaderSize + gamePayloadLength;
	}

	public static int HostRelayPayloadLength(int gamePayloadLength)
	{
		return MessageCodec.HostDataHeaderSize + gamePayloadLength;
	}
}
