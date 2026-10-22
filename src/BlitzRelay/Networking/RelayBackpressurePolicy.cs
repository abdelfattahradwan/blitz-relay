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

	public static bool ShouldDisconnectReliableForBackpressure(int reliableQueuePackets, int reliablePacketCount, DeliveryMethod deliveryMethod)
	{
		return deliveryMethod == DeliveryMethod.ReliableOrdered && reliableQueuePackets + reliablePacketCount > ReliableQueuePacketsBeforeDisconnect;
	}

	public static int ReliablePacketCount(int payloadLength, int maxSinglePacketSize)
	{
		if (payloadLength <= maxSinglePacketSize) return 1;

		int fragmentPayloadLength = maxSinglePacketSize - NetConstants.FragmentHeaderSize;

		return (payloadLength + fragmentPayloadLength - 1) / fragmentPayloadLength;
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
