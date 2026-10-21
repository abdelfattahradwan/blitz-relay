using BlitzRelay.Networking;

namespace BlitzRelay.Rooms;

internal sealed class Room
{
	public required string Code { get; init; }

	public required RoomKind Kind { get; init; }

	public bool IsPublic { get; init; }

	public string DisplayName { get; set; } = string.Empty;

	public PeerConnection? Host { get; set; }

	public string? HostToken { get; set; }

	public required ushort MaximumClients { get; init; }

	public Dictionary<int, PeerConnection> ClientsByVirtualId { get; } = new();

	public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

	public int NextVirtualClientId { get; private set; } = 1;

	public long NextJoinOrder { get; private set; } = 1;

	public string? PendingHostClaimToken { get; set; }

	public long? PendingHostClaimExpiresAt { get; set; }

	public PeerConnection? PendingHostPromotionPeer { get; set; }

	public int PendingHostPromotionVirtualClientId { get; set; }

	public bool PendingHostPromotionAckReceived { get; set; }

	public bool HasPendingHostClaim
	{
		get => !string.IsNullOrWhiteSpace(PendingHostClaimToken) && PendingHostClaimExpiresAt is not null;
	}

	public bool HasActiveHost
	{
		get => Host is { Role: PeerRole.Host, Room: not null } host && ReferenceEquals(host.Room, this);
	}

	public int AllocateVirtualClientId()
	{
		return NextVirtualClientId++;
	}

	public long AllocateJoinOrder()
	{
		return NextJoinOrder++;
	}

	public void ClearPendingHostClaim()
	{
		PendingHostClaimToken = null;

		PendingHostClaimExpiresAt = null;

		PendingHostPromotionPeer = null;

		PendingHostPromotionVirtualClientId = 0;

		PendingHostPromotionAckReceived = false;
	}
}
