using JetBrains.Annotations;
using System.Buffers.Binary;
using System.Text;

namespace BlitzRelay.Protocol;

[PublicAPI]
public static class MessageCodec
{
	private const int HostRegisterHeaderSize = 3;

	private const int ClientJoinHeaderSize = 3;

	private const int RoomCreatedHeaderSize = 4;

	private const int HostClaimHeaderSize = 5;

	private const int HostPromotedHeaderSize = 6;

	private const int HostPromotionAckHeaderSize = 5;

	private const int EmptyMessageSize = 1;

	private const int ErrorHeaderSize = 2;

	private const int VirtualClientIdSize = 4;

	private static readonly byte[] JoinSuccessMessage = [(byte)MessageType.JoinSuccess];

	private static readonly byte[] HostUnavailableMessage = [(byte)MessageType.HostUnavailable];

	private static readonly byte[] HostAvailableMessage = [(byte)MessageType.HostAvailable];

	private static readonly byte[] RoomNotFoundErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.RoomNotFound];

	private static readonly byte[] RoomFullErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.RoomFull];

	private static readonly byte[] RoomExistsErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.RoomExists];

	private static readonly byte[] InvalidHostClaimErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.InvalidHostClaim];

	private static readonly byte[] RoomClosedErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.RoomClosed];

	private static readonly byte[] InvalidMaximumClientsErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.InvalidMaximumClients];

	private static readonly byte[] UnknownErrorMessage = [(byte)MessageType.Error, (byte)ErrorCode.Unknown];

	public const int HostDataHeaderSize = 6;

	public const int ClientDataHeaderSize = 2;

	public const int MaxRelayHeaderSize = HostDataHeaderSize;

	public const int BroadcastVirtualClientId = -1;

	public const byte RelayWireChannel = 0;

	public static bool TryReadMessageType(ReadOnlySpan<byte> payload, out MessageType messageType)
	{
		messageType = default(MessageType);

		if (payload.Length == 0) return false;

		messageType = ReadMessageType(payload);

		return true;
	}

	public static MessageType ReadMessageType(ReadOnlySpan<byte> payload)
	{
		return (MessageType)payload[0];
	}

	public static int WriteHostRegister(Span<byte> destination, int maximumClients)
	{
		EnsureDestinationSize(destination, HostRegisterHeaderSize);

		destination[0] = (byte)MessageType.HostRegister;

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), checked((ushort)maximumClients));

		return HostRegisterHeaderSize;
	}

	public static byte[] CreateHostRegister(int maximumClients)
	{
		byte[] payload = new byte[HostRegisterHeaderSize];

		WriteHostRegister(payload, maximumClients);

		return payload;
	}

	public static bool TryReadHostRegister(ReadOnlySpan<byte> payload, out ushort maximumClients)
	{
		maximumClients = 0;

		if (payload.Length < HostRegisterHeaderSize || payload[0] != (byte)MessageType.HostRegister) return false;

		maximumClients = ReadHostRegister(payload);

		return true;
	}

	public static ushort ReadHostRegister(ReadOnlySpan<byte> payload)
	{
		return BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);
	}

	public static int WriteClientJoin(Span<byte> destination, string roomCode)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		return WriteClientJoin(destination, roomCode, roomCodeByteCount);
	}

	public static byte[] CreateClientJoin(string roomCode)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		byte[] payload = new byte[ClientJoinHeaderSize + roomCodeByteCount];

		WriteClientJoin(payload, roomCode, roomCodeByteCount);

		return payload;
	}

	public static bool TryReadClientJoin(ReadOnlySpan<byte> payload, out string roomCode)
	{
		roomCode = string.Empty;

		if (payload.Length < ClientJoinHeaderSize || payload[0] != (byte)MessageType.ClientJoin) return false;

		ushort roomCodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);

		if (payload.Length < ClientJoinHeaderSize + roomCodeLength) return false;

		roomCode = ReadString(payload, ClientJoinHeaderSize, roomCodeLength);

		return true;
	}

	public static string ReadClientJoin(ReadOnlySpan<byte> payload)
	{
		ushort roomCodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);

		return ReadString(payload, ClientJoinHeaderSize, roomCodeLength);
	}

	public static int WriteHostClaim(Span<byte> destination, string roomCode, string claimToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int claimTokenByteCount = Encoding.UTF8.GetByteCount(claimToken);

		return WriteHostClaim(destination, roomCode, roomCodeByteCount, claimToken, claimTokenByteCount);
	}

	public static byte[] CreateHostClaim(string roomCode, string claimToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int claimTokenByteCount = Encoding.UTF8.GetByteCount(claimToken);

		byte[] payload = new byte[HostClaimHeaderSize + roomCodeByteCount + claimTokenByteCount];

		WriteHostClaim(payload, roomCode, roomCodeByteCount, claimToken, claimTokenByteCount);

		return payload;
	}

	public static bool TryReadHostClaim(ReadOnlySpan<byte> payload, out string roomCode, out string claimToken)
	{
		roomCode = string.Empty;

		claimToken = string.Empty;

		if (payload.Length < HostClaimHeaderSize || payload[0] != (byte)MessageType.HostClaim) return false;

		ushort roomCodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);

		ushort claimTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[3..5]);

		if (payload.Length < HostClaimHeaderSize + roomCodeLength + claimTokenLength) return false;

		roomCode = ReadString(payload, HostClaimHeaderSize, roomCodeLength);

		claimToken = ReadString(payload, HostClaimHeaderSize + roomCodeLength, claimTokenLength);

		return true;
	}

	public static void ReadHostClaim(ReadOnlySpan<byte> payload, out string roomCode, out string claimToken)
	{
		ushort roomCodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);

		ushort claimTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[3..5]);

		roomCode = ReadString(payload, HostClaimHeaderSize, roomCodeLength);

		claimToken = ReadString(payload, HostClaimHeaderSize + roomCodeLength, claimTokenLength);
	}

	public static int WriteHostPromotionAck(Span<byte> destination, string roomCode, string claimToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int claimTokenByteCount = Encoding.UTF8.GetByteCount(claimToken);

		return WriteHostPromotionAck(destination, roomCode, roomCodeByteCount, claimToken, claimTokenByteCount);
	}

	public static byte[] CreateHostPromotionAck(string roomCode, string claimToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int claimTokenByteCount = Encoding.UTF8.GetByteCount(claimToken);

		byte[] payload = new byte[HostPromotionAckHeaderSize + roomCodeByteCount + claimTokenByteCount];

		WriteHostPromotionAck(payload, roomCode, roomCodeByteCount, claimToken, claimTokenByteCount);

		return payload;
	}

	public static bool TryReadHostPromotionAck(ReadOnlySpan<byte> payload, out string roomCode, out string claimToken)
	{
		roomCode = string.Empty;

		claimToken = string.Empty;

		if (payload.Length < HostPromotionAckHeaderSize || payload[0] != (byte)MessageType.HostPromotionAck) return false;

		ushort roomCodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);

		ushort claimTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[3..5]);

		if (payload.Length < HostPromotionAckHeaderSize + roomCodeLength + claimTokenLength) return false;

		roomCode = ReadString(payload, HostPromotionAckHeaderSize, roomCodeLength);

		claimToken = ReadString(payload, HostPromotionAckHeaderSize + roomCodeLength, claimTokenLength);

		return true;
	}

	public static void ReadHostPromotionAck(ReadOnlySpan<byte> payload, out string roomCode, out string claimToken)
	{
		ushort roomCodeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..3]);

		ushort claimTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[3..5]);

		roomCode = ReadString(payload, HostPromotionAckHeaderSize, roomCodeLength);

		claimToken = ReadString(payload, HostPromotionAckHeaderSize + roomCodeLength, claimTokenLength);
	}

	public static int WriteConnected(Span<byte> destination, int virtualClientId)
	{
		return WriteVirtualClientMessage(destination, MessageType.Connected, virtualClientId);
	}

	public static byte[] CreateConnected(int virtualClientId)
	{
		return CreateVirtualClientMessage(MessageType.Connected, virtualClientId);
	}

	public static bool TryReadConnected(ReadOnlySpan<byte> payload, out int virtualClientId)
	{
		return TryReadVirtualClientMessage(payload, MessageType.Connected, out virtualClientId);
	}

	public static int ReadConnected(ReadOnlySpan<byte> payload)
	{
		return ReadVirtualClientId(payload);
	}

	public static int WriteDisconnected(Span<byte> destination, int virtualClientId)
	{
		return WriteVirtualClientMessage(destination, MessageType.Disconnected, virtualClientId);
	}

	public static byte[] CreateDisconnected(int virtualClientId)
	{
		return CreateVirtualClientMessage(MessageType.Disconnected, virtualClientId);
	}

	public static bool TryReadDisconnected(ReadOnlySpan<byte> payload, out int virtualClientId)
	{
		return TryReadVirtualClientMessage(payload, MessageType.Disconnected, out virtualClientId);
	}

	public static int ReadDisconnected(ReadOnlySpan<byte> payload)
	{
		return ReadVirtualClientId(payload);
	}

	public static int WriteHostData(Span<byte> destination, int virtualClientId, byte gameChannel, ReadOnlySpan<byte> gamePayload)
	{
		int payloadLength = HostDataHeaderSize + gamePayload.Length;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.Data;

		BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(1, VirtualClientIdSize), virtualClientId);

		destination[5] = gameChannel;

		gamePayload.CopyTo(destination.Slice(HostDataHeaderSize, gamePayload.Length));

		return payloadLength;
	}

	public static byte[] CreateHostData(int virtualClientId, byte gameChannel, ReadOnlySpan<byte> gamePayload)
	{
		byte[] payload = new byte[HostDataHeaderSize + gamePayload.Length];

		payload[0] = (byte)MessageType.Data;

		BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, VirtualClientIdSize), virtualClientId);

		payload[5] = gameChannel;

		gamePayload.CopyTo(payload.AsSpan(HostDataHeaderSize, gamePayload.Length));

		return payload;
	}

	public static bool TryReadHostData(ReadOnlySpan<byte> payload, out int targetVirtualClientId, out byte gameChannel, out byte[] gamePayload)
	{
		if (!TryReadHostDataSpan(payload, out targetVirtualClientId, out gameChannel, out ReadOnlySpan<byte> gamePayloadSpan))
		{
			gamePayload = [];

			return false;
		}

		gamePayload = gamePayloadSpan.ToArray();

		return true;
	}

	public static bool TryReadHostDataSpan(ReadOnlySpan<byte> payload, out int targetVirtualClientId, out byte gameChannel, out ReadOnlySpan<byte> gamePayload)
	{
		if (payload.Length < HostDataHeaderSize || payload[0] != (byte)MessageType.Data)
		{
			targetVirtualClientId = 0;

			gameChannel = 0;

			gamePayload = ReadOnlySpan<byte>.Empty;

			return false;
		}

		targetVirtualClientId = ReadVirtualClientId(payload);

		gameChannel = payload[5];

		gamePayload = payload[HostDataHeaderSize..];

		return true;
	}

	public static void ReadHostData(ReadOnlySpan<byte> payload, out int targetVirtualClientId, out byte gameChannel, out ReadOnlySpan<byte> gamePayload)
	{
		targetVirtualClientId = ReadVirtualClientId(payload);

		gameChannel = payload[5];

		gamePayload = payload[HostDataHeaderSize..];
	}

	public static void ReadHostData(byte[] data, int totalLength, out int virtualClientId, out byte gameChannel, out int payloadOffset, out int payloadLength)
	{
		virtualClientId = ReadVirtualClientId(data);

		gameChannel = data[5];

		payloadOffset = HostDataHeaderSize;

		payloadLength = totalLength - HostDataHeaderSize;
	}

	public static int WriteClientData(Span<byte> destination, byte gameChannel, ReadOnlySpan<byte> gamePayload)
	{
		int payloadLength = ClientDataHeaderSize + gamePayload.Length;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.Data;

		destination[1] = gameChannel;

		gamePayload.CopyTo(destination.Slice(ClientDataHeaderSize, gamePayload.Length));

		return payloadLength;
	}

	public static byte[] CreateClientData(byte gameChannel, ReadOnlySpan<byte> gamePayload)
	{
		byte[] payload = new byte[ClientDataHeaderSize + gamePayload.Length];

		payload[0] = (byte)MessageType.Data;

		payload[1] = gameChannel;

		gamePayload.CopyTo(payload.AsSpan(ClientDataHeaderSize, gamePayload.Length));

		return payload;
	}

	public static bool TryReadClientData(ReadOnlySpan<byte> payload, out byte gameChannel, out byte[] gamePayload)
	{
		if (!TryReadClientDataSpan(payload, out gameChannel, out ReadOnlySpan<byte> gamePayloadSpan))
		{
			gamePayload = [];

			return false;
		}

		gamePayload = gamePayloadSpan.ToArray();

		return true;
	}

	public static bool TryReadClientDataSpan(ReadOnlySpan<byte> payload, out byte gameChannel, out ReadOnlySpan<byte> gamePayload)
	{
		if (payload.Length < ClientDataHeaderSize || payload[0] != (byte)MessageType.Data)
		{
			gameChannel = 0;

			gamePayload = ReadOnlySpan<byte>.Empty;

			return false;
		}

		gameChannel = payload[1];

		gamePayload = payload[ClientDataHeaderSize..];

		return true;
	}

	public static void ReadClientData(ReadOnlySpan<byte> payload, out byte gameChannel, out ReadOnlySpan<byte> gamePayload)
	{
		gameChannel = payload[1];

		gamePayload = payload[ClientDataHeaderSize..];
	}

	public static void ReadClientData(byte[] data, int totalLength, out byte gameChannel, out int payloadOffset, out int payloadLength)
	{
		gameChannel = data[1];

		payloadOffset = ClientDataHeaderSize;

		payloadLength = totalLength - ClientDataHeaderSize;
	}

	public static int WriteKick(Span<byte> destination, int virtualClientId)
	{
		return WriteVirtualClientMessage(destination, MessageType.Kick, virtualClientId);
	}

	public static byte[] CreateKick(int virtualClientId)
	{
		return CreateVirtualClientMessage(MessageType.Kick, virtualClientId);
	}

	public static bool TryReadKick(ReadOnlySpan<byte> payload, out int virtualClientId)
	{
		return TryReadVirtualClientMessage(payload, MessageType.Kick, out virtualClientId);
	}

	public static int ReadKick(ReadOnlySpan<byte> payload)
	{
		return ReadVirtualClientId(payload);
	}

	public static int WriteRoomCreated(Span<byte> destination, string roomCode, string roomHostToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int roomHostTokenByteCount = Encoding.UTF8.GetByteCount(roomHostToken);

		return WriteRoomCreated(destination, roomCode, roomCodeByteCount, roomHostToken, roomHostTokenByteCount);
	}

	public static byte[] CreateRoomCreated(string roomCode, string roomHostToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int roomHostTokenByteCount = Encoding.UTF8.GetByteCount(roomHostToken);

		byte[] payload = new byte[RoomCreatedHeaderSize + roomCodeByteCount + roomHostTokenByteCount];

		WriteRoomCreated(payload, roomCode, roomCodeByteCount, roomHostToken, roomHostTokenByteCount);

		return payload;
	}

	public static bool TryReadRoomCreated(ReadOnlySpan<byte> payload, out string roomCode, out string roomHostToken)
	{
		roomCode = string.Empty;

		roomHostToken = string.Empty;

		if (payload.Length < RoomCreatedHeaderSize || payload[0] != (byte)MessageType.RoomCreated) return false;

		byte roomCodeLength = payload[1];

		ushort roomHostTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);

		if (payload.Length < RoomCreatedHeaderSize + roomCodeLength + roomHostTokenLength) return false;

		roomCode = ReadString(payload, RoomCreatedHeaderSize, roomCodeLength);

		roomHostToken = ReadString(payload, RoomCreatedHeaderSize + roomCodeLength, roomHostTokenLength);

		return true;
	}

	public static void ReadRoomCreated(ReadOnlySpan<byte> payload, out string roomCode, out string roomHostToken)
	{
		byte roomCodeLength = payload[1];

		ushort roomHostTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);

		roomCode = ReadString(payload, RoomCreatedHeaderSize, roomCodeLength);

		roomHostToken = ReadString(payload, RoomCreatedHeaderSize + roomCodeLength, roomHostTokenLength);
	}

	public static int WriteJoinSuccess(Span<byte> destination)
	{
		EnsureDestinationSize(destination, 1);

		destination[0] = (byte)MessageType.JoinSuccess;

		return 1;
	}

	public static byte[] CreateJoinSuccess()
	{
		byte[] payload = new byte[1];

		WriteJoinSuccess(payload);

		return payload;
	}

	internal static ReadOnlySpan<byte> GetJoinSuccessMessage()
	{
		return JoinSuccessMessage;
	}

	public static bool TryReadJoinSuccess(ReadOnlySpan<byte> payload)
	{
		return payload.Length == 1 && payload[0] == (byte)MessageType.JoinSuccess;
	}

	public static void ReadJoinSuccess(ReadOnlySpan<byte> payload)
	{
		_ = payload[0];
	}

	public static int WriteHostPromoted(Span<byte> destination, string roomCode, int maximumClients, string claimToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int claimTokenByteCount = Encoding.UTF8.GetByteCount(claimToken);

		return WriteHostPromoted(destination, roomCode, roomCodeByteCount, maximumClients, claimToken, claimTokenByteCount);
	}

	public static byte[] CreateHostPromoted(string roomCode, int maximumClients, string claimToken)
	{
		int roomCodeByteCount = Encoding.UTF8.GetByteCount(roomCode);

		int claimTokenByteCount = Encoding.UTF8.GetByteCount(claimToken);

		byte[] payload = new byte[HostPromotedHeaderSize + roomCodeByteCount + claimTokenByteCount];

		WriteHostPromoted(payload, roomCode, roomCodeByteCount, maximumClients, claimToken, claimTokenByteCount);

		return payload;
	}

	public static bool TryReadHostPromoted(ReadOnlySpan<byte> payload, out string roomCode, out int maximumClients, out string claimToken)
	{
		roomCode = string.Empty;

		maximumClients = 0;

		claimToken = string.Empty;

		if (payload.Length < HostPromotedHeaderSize || payload[0] != (byte)MessageType.HostPromoted) return false;

		byte roomCodeLength = payload[1];

		maximumClients = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);

		ushort claimTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..6]);

		if (payload.Length < HostPromotedHeaderSize + roomCodeLength + claimTokenLength) return false;

		roomCode = ReadString(payload, HostPromotedHeaderSize, roomCodeLength);

		claimToken = ReadString(payload, HostPromotedHeaderSize + roomCodeLength, claimTokenLength);

		return true;
	}

	public static void ReadHostPromoted(ReadOnlySpan<byte> payload, out string roomCode, out int maximumClients, out string claimToken)
	{
		byte roomCodeLength = payload[1];

		maximumClients = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);

		ushort claimTokenLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..6]);

		roomCode = ReadString(payload, HostPromotedHeaderSize, roomCodeLength);

		claimToken = ReadString(payload, HostPromotedHeaderSize + roomCodeLength, claimTokenLength);
	}

	public static int WriteHostUnavailable(Span<byte> destination)
	{
		return WriteEmptyMessage(destination, MessageType.HostUnavailable);
	}

	public static byte[] CreateHostUnavailable()
	{
		return CreateEmptyMessage(MessageType.HostUnavailable);
	}

	internal static ReadOnlySpan<byte> GetHostUnavailableMessage()
	{
		return HostUnavailableMessage;
	}

	public static bool TryReadHostUnavailable(ReadOnlySpan<byte> payload)
	{
		return TryReadEmptyMessage(payload, MessageType.HostUnavailable);
	}

	public static void ReadHostUnavailable(ReadOnlySpan<byte> payload)
	{
		_ = payload[0];
	}

	public static int WriteHostAvailable(Span<byte> destination)
	{
		return WriteEmptyMessage(destination, MessageType.HostAvailable);
	}

	public static byte[] CreateHostAvailable()
	{
		return CreateEmptyMessage(MessageType.HostAvailable);
	}

	internal static ReadOnlySpan<byte> GetHostAvailableMessage()
	{
		return HostAvailableMessage;
	}

	public static bool TryReadHostAvailable(ReadOnlySpan<byte> payload)
	{
		return TryReadEmptyMessage(payload, MessageType.HostAvailable);
	}

	public static void ReadHostAvailable(ReadOnlySpan<byte> payload)
	{
		_ = payload[0];
	}

	public static int WriteError(Span<byte> destination, ErrorCode errorCode)
	{
		EnsureDestinationSize(destination, ErrorHeaderSize);

		destination[0] = (byte)MessageType.Error;

		destination[1] = (byte)errorCode;

		return ErrorHeaderSize;
	}

	public static byte[] CreateError(ErrorCode errorCode)
	{
		byte[] payload = new byte[ErrorHeaderSize];

		WriteError(payload, errorCode);

		return payload;
	}

	internal static ReadOnlySpan<byte> GetErrorMessage(ErrorCode errorCode)
	{
		byte[] payload = errorCode switch
		{
			ErrorCode.RoomNotFound => RoomNotFoundErrorMessage,

			ErrorCode.RoomFull => RoomFullErrorMessage,

			ErrorCode.RoomExists => RoomExistsErrorMessage,

			ErrorCode.InvalidHostClaim => InvalidHostClaimErrorMessage,

			ErrorCode.RoomClosed => RoomClosedErrorMessage,

			ErrorCode.InvalidMaximumClients => InvalidMaximumClientsErrorMessage,

			ErrorCode.Unknown => UnknownErrorMessage,

			_ => CreateError(errorCode),
		};

		return payload;
	}

	public static bool TryReadError(ReadOnlySpan<byte> payload, out ErrorCode errorCode)
	{
		errorCode = default(ErrorCode);

		if (payload.Length < ErrorHeaderSize || payload[0] != (byte)MessageType.Error) return false;

		errorCode = ReadError(payload);

		return true;
	}

	public static ErrorCode ReadError(ReadOnlySpan<byte> payload)
	{
		return (ErrorCode)payload[1];
	}

	public static bool IsUnreliableGameChannel(byte gameChannel)
	{
		return gameChannel is (byte)GameChannel.Unreliable or (byte)GameChannel.UnreliableSequenced;
	}

	private static byte[] CreateVirtualClientMessage(MessageType messageType, int virtualClientId)
	{
		byte[] payload = new byte[1 + VirtualClientIdSize];

		WriteVirtualClientMessage(payload, messageType, virtualClientId);

		return payload;
	}

	private static int WriteClientJoin(Span<byte> destination, string roomCode, int roomCodeByteCount)
	{
		int payloadLength = ClientJoinHeaderSize + roomCodeByteCount;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.ClientJoin;

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), checked((ushort)roomCodeByteCount));

		Encoding.UTF8.GetBytes(roomCode.AsSpan(), destination.Slice(ClientJoinHeaderSize, roomCodeByteCount));

		return payloadLength;
	}

	private static int WriteHostClaim(Span<byte> destination, string roomCode, int roomCodeByteCount, string claimToken, int claimTokenByteCount)
	{
		int payloadLength = HostClaimHeaderSize + roomCodeByteCount + claimTokenByteCount;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.HostClaim;

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), checked((ushort)roomCodeByteCount));

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(3, 2), checked((ushort)claimTokenByteCount));

		Encoding.UTF8.GetBytes(roomCode.AsSpan(), destination.Slice(HostClaimHeaderSize, roomCodeByteCount));

		Encoding.UTF8.GetBytes(claimToken.AsSpan(), destination.Slice(HostClaimHeaderSize + roomCodeByteCount, claimTokenByteCount));

		return payloadLength;
	}

	private static int WriteHostPromotionAck(Span<byte> destination, string roomCode, int roomCodeByteCount, string claimToken, int claimTokenByteCount)
	{
		int payloadLength = HostPromotionAckHeaderSize + roomCodeByteCount + claimTokenByteCount;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.HostPromotionAck;

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), checked((ushort)roomCodeByteCount));

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(3, 2), checked((ushort)claimTokenByteCount));

		Encoding.UTF8.GetBytes(roomCode.AsSpan(), destination.Slice(HostPromotionAckHeaderSize, roomCodeByteCount));

		Encoding.UTF8.GetBytes(claimToken.AsSpan(), destination.Slice(HostPromotionAckHeaderSize + roomCodeByteCount, claimTokenByteCount));

		return payloadLength;
	}

	private static int WriteRoomCreated(Span<byte> destination, string roomCode, int roomCodeByteCount, string roomHostToken, int roomHostTokenByteCount)
	{
		int payloadLength = RoomCreatedHeaderSize + roomCodeByteCount + roomHostTokenByteCount;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.RoomCreated;

		destination[1] = checked((byte)roomCodeByteCount);

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), checked((ushort)roomHostTokenByteCount));

		Encoding.UTF8.GetBytes(roomCode.AsSpan(), destination.Slice(RoomCreatedHeaderSize, roomCodeByteCount));

		Encoding.UTF8.GetBytes(roomHostToken.AsSpan(), destination.Slice(RoomCreatedHeaderSize + roomCodeByteCount, roomHostTokenByteCount));

		return payloadLength;
	}

	private static int WriteHostPromoted(Span<byte> destination, string roomCode, int roomCodeByteCount, int maximumClients, string claimToken, int claimTokenByteCount)
	{
		int payloadLength = HostPromotedHeaderSize + roomCodeByteCount + claimTokenByteCount;

		EnsureDestinationSize(destination, payloadLength);

		destination[0] = (byte)MessageType.HostPromoted;

		destination[1] = checked((byte)roomCodeByteCount);

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), checked((ushort)maximumClients));

		BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), checked((ushort)claimTokenByteCount));

		Encoding.UTF8.GetBytes(roomCode.AsSpan(), destination.Slice(HostPromotedHeaderSize, roomCodeByteCount));

		Encoding.UTF8.GetBytes(claimToken.AsSpan(), destination.Slice(HostPromotedHeaderSize + roomCodeByteCount, claimTokenByteCount));

		return payloadLength;
	}

	private static int WriteVirtualClientMessage(Span<byte> destination, MessageType messageType, int virtualClientId)
	{
		EnsureDestinationSize(destination, 1 + VirtualClientIdSize);

		destination[0] = (byte)messageType;

		BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(1, VirtualClientIdSize), virtualClientId);

		return 1 + VirtualClientIdSize;
	}

	private static bool TryReadVirtualClientMessage(ReadOnlySpan<byte> payload, MessageType messageType, out int virtualClientId)
	{
		virtualClientId = 0;

		if (payload.Length < 1 + VirtualClientIdSize || payload[0] != (byte)messageType) return false;

		virtualClientId = ReadVirtualClientId(payload);

		return true;
	}

	private static int WriteEmptyMessage(Span<byte> destination, MessageType messageType)
	{
		EnsureDestinationSize(destination, EmptyMessageSize);

		destination[0] = (byte)messageType;

		return EmptyMessageSize;
	}

	private static byte[] CreateEmptyMessage(MessageType messageType)
	{
		byte[] payload = new byte[EmptyMessageSize];

		WriteEmptyMessage(payload, messageType);

		return payload;
	}

	private static bool TryReadEmptyMessage(ReadOnlySpan<byte> payload, MessageType messageType)
	{
		return payload.Length == EmptyMessageSize && payload[0] == (byte)messageType;
	}

	private static int ReadVirtualClientId(ReadOnlySpan<byte> payload)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(payload[1..5]);
	}

	private static string ReadString(ReadOnlySpan<byte> payload, int headerSize, int valueLength)
	{
		return Encoding.UTF8.GetString(payload.Slice(headerSize, valueLength));
	}

	private static void EnsureDestinationSize(Span<byte> destination, int requiredLength)
	{
		if (destination.Length < requiredLength)
		{
			throw new ArgumentException("Destination buffer is too small.", nameof(destination));
		}
	}
}
