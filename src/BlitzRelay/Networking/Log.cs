using LiteNetLib;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace BlitzRelay.Networking;

internal static partial class Log
{
	[LoggerMessage(LogLevel.Error, "Missing required connection key environment variable {EnvironmentVariableName}.")]
	public static partial void MissingRequiredConnectionKey(ILogger logger, string environmentVariableName);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to start relay on UDP port {Port}.")]
	public static partial void FailedToStartRelay(ILogger logger, int port);

	[LoggerMessage(Level = LogLevel.Information, Message = "Relay server started on UDP port {Port} with ChannelsCount={ChannelsCount}, PingInterval={PingInterval}ms, DisconnectTimeout={DisconnectTimeout}ms.")]
	public static partial void RelayServerStarted(ILogger logger, int port, int channelsCount, int pingInterval, int disconnectTimeout);

	[LoggerMessage(Level = LogLevel.Information, Message = "Relay server stopping.")]
	public static partial void RelayServerStopping(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Rejected connection request from {RemoteEndPoint}.")]
	public static partial void ConnectionRequestRejected(ILogger logger, IPEndPoint remoteEndPoint, Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "Peer connected: id={PeerId}, endpoint={Endpoint}.")]
	public static partial void PeerConnected(ILogger logger, int peerId, string endpoint);

	[LoggerMessage(Level = LogLevel.Information, Message = "Peer disconnected: id={PeerId}, endpoint={Endpoint}, role={Role}, reason={Reason}.")]
	public static partial void PeerDisconnected(ILogger logger, int peerId, string endpoint, PeerRole role, DisconnectReason reason);

	[LoggerMessage(Level = LogLevel.Information, Message = "Room {RoomCode} was torn down because the host disconnected.")]
	public static partial void RoomTornDownBecauseHostDisconnected(ILogger logger, string roomCode);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Received relay payload from peer {PeerId}: bytes={Length}, channel={ChannelNumber}, delivery={DeliveryMethod}.")]
	public static partial void RelayPayloadReceived(ILogger logger, int peerId, int length, byte channelNumber, DeliveryMethod deliveryMethod);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} sent unknown relay message type 0x{MessageType:X2}.")]
	public static partial void UnknownRelayMessageType(ILogger logger, int peerId, byte messageType);

	[LoggerMessage(Level = LogLevel.Warning, Message = "LiteNetLib socket error from {EndPoint}: {SocketError}.")]
	public static partial void LiteNetLibSocketError(ILogger logger, IPEndPoint endPoint, SocketError socketError);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Latency update for peer {PeerId}: {Latency}ms.")]
	public static partial void PeerLatencyUpdated(ILogger logger, int peerId, int latency);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Invalid HostRegister from peer {PeerId}.")]
	public static partial void InvalidHostRegister(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Invalid HostRegister from peer {PeerId}: maximumClients={MaximumClients} must be at least 1.")]
	public static partial void InvalidHostRegisterMaximumClients(ILogger logger, int peerId, ushort maximumClients);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} attempted HostRegister while already assigned role {Role}.")]
	public static partial void HostRegisterWhileAlreadyAssignedRole(ILogger logger, int peerId, PeerRole role);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} attempted HostClaim while already assigned role {Role}.")]
	public static partial void HostClaimWhileAlreadyAssignedRole(ILogger logger, int peerId, PeerRole role);

	[LoggerMessage(Level = LogLevel.Information, Message = "Room created: roomCode={RoomCode}, hostPeerId={PeerId}, maximumClients={MaximumClients}.")]
	public static partial void RoomCreated(ILogger logger, string roomCode, int peerId, int maximumClients);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Invalid ClientJoin from peer {PeerId}.")]
	public static partial void InvalidClientJoin(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} attempted ClientJoin while already assigned role {Role}.")]
	public static partial void ClientJoinWhileAlreadyAssignedRole(ILogger logger, int peerId, PeerRole role);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} attempted to join missing room {RoomCode}.")]
	public static partial void JoinMissingRoom(ILogger logger, int peerId, string roomCode);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} attempted to join full room {RoomCode}.")]
	public static partial void JoinFullRoom(ILogger logger, int peerId, string roomCode);

	[LoggerMessage(Level = LogLevel.Information, Message = "Peer {PeerId} joined room {RoomCode} as virtual client {VirtualClientId}.")]
	public static partial void ClientJoinedRoom(ILogger logger, int peerId, string roomCode, int virtualClientId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} sent relay data without a room role.")]
	public static partial void RelayDataWithoutRoomRole(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Invalid host data payload from peer {PeerId}.")]
	public static partial void InvalidHostDataPayload(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Host peer {PeerId} sent data without an assigned room.")]
	public static partial void HostDataWithoutAssignedRoom(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Host peer {PeerId} broadcast {PayloadLength} bytes on game channel {GameChannel} to {RecipientCount} clients.")]
	public static partial void HostBroadcast(ILogger logger, int peerId, int payloadLength, byte gameChannel, int recipientCount);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Host peer {PeerId} targeted unknown virtual client {VirtualClientId}.")]
	public static partial void HostTargetedUnknownVirtualClient(ILogger logger, int peerId, int virtualClientId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Invalid client data payload from peer {PeerId}.")]
	public static partial void InvalidClientDataPayload(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Client peer {PeerId} sent data without an active host room.")]
	public static partial void ClientDataWithoutActiveHostRoom(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Peer {PeerId} attempted kick without host role.")]
	public static partial void KickWithoutHostRole(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Invalid kick payload from peer {PeerId}.")]
	public static partial void InvalidKickPayload(ILogger logger, int peerId);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Host peer {PeerId} attempted to kick unknown virtual client {VirtualClientId}.")]
	public static partial void KickUnknownVirtualClient(ILogger logger, int peerId, int virtualClientId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Host peer {PeerId} kicked virtual client {VirtualClientId} (peer {ClientPeerId}).")]
	public static partial void ClientKickedByHost(ILogger logger, int peerId, int virtualClientId, int clientPeerId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Admin kicked virtual client {VirtualClientId} (peer {ClientPeerId}) from room {RoomCode}.")]
	public static partial void ClientKickedByAdmin(ILogger logger, int virtualClientId, int clientPeerId, string roomCode);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to send relay payload to peer {PeerId} with delivery {DeliveryMethod}.")]
	public static partial void FailedToSendRelayPayload(ILogger logger, int peerId, DeliveryMethod deliveryMethod, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Dropped oversized unreliable relay payload to peer {PeerId}: bytes={PayloadLength}, maxUnreliableBytes={MaxUnreliablePayloadLength}.")]
	public static partial void OversizedUnreliableRelayPayloadDropped(ILogger logger, int peerId, int payloadLength, int maxUnreliablePayloadLength);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Dropped unreliable relay payload to peer {PeerId} because reliable queue is backed up: bytes={PayloadLength}, reliableQueuePackets={ReliableQueuePackets}, threshold={Threshold}.")]
	public static partial void UnreliableRelayPayloadDroppedDueToBackpressure(ILogger logger, int peerId, int payloadLength, int reliableQueuePackets, int threshold);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Disconnecting peer {PeerId} because reliable relay queue is backed up: bytes={PayloadLength}, reliableQueuePackets={ReliableQueuePackets}, threshold={Threshold}.")]
	public static partial void ReliableRelayRecipientDisconnectedDueToBackpressure(ILogger logger, int peerId, int payloadLength, int reliableQueuePackets, int threshold);

	[LoggerMessage(Level = LogLevel.Warning, Message = "LiteNetLib rejected relay payload to peer {PeerId}: bytes={PayloadLength}, delivery={DeliveryMethod}.")]
	public static partial void RelayPayloadRejectedAsTooLarge(ILogger logger, int peerId, int payloadLength, DeliveryMethod deliveryMethod, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to disconnect peer {PeerId}.")]
	public static partial void FailedToDisconnectPeer(ILogger logger, int peerId, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in {Handler}.")]
	public static partial void UnhandledHandlerException(ILogger logger, string handler, Exception exception);

	[LoggerMessage(Message = "LiteNetLib: {Message}")]
	public static partial void LiteNetLibMessage(ILogger logger, LogLevel level, string message);
}
