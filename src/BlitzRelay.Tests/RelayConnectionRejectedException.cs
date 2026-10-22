using LiteNetLib;

namespace BlitzRelay.Tests;

internal sealed class RelayConnectionRejectedException(DisconnectReason reason) : Exception($"The relay rejected the connection: {reason}.")
{
	public DisconnectReason Reason { get; } = reason;
}
