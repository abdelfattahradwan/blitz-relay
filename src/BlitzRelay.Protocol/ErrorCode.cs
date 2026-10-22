using JetBrains.Annotations;

namespace BlitzRelay.Protocol;

[PublicAPI]
public enum ErrorCode : byte
{
	RoomNotFound = 0x01,

	RoomFull = 0x02,

	RoomExists = 0x03,

	InvalidHostClaim = 0x04,

	RoomClosed = 0x05,

	InvalidMaximumClients = 0x06,

	HostRemoved = 0x07,

	Unknown = 0xFF,
}
