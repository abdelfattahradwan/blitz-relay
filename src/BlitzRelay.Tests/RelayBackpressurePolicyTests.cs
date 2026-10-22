using BlitzRelay.Networking;
using LiteNetLib;

namespace BlitzRelay.Tests;

[TestClass]
public sealed class RelayBackpressurePolicyTests
{
	private const int MaxSinglePacketSize = 1346;

	private const int FragmentPayloadLength = MaxSinglePacketSize - NetConstants.FragmentHeaderSize;

	[TestMethod]
	[DataRow(1, 1)]
	[DataRow(MaxSinglePacketSize, 1)]
	[DataRow(MaxSinglePacketSize + 1, 2)]
	[DataRow(FragmentPayloadLength * 2, 2)]
	[DataRow(FragmentPayloadLength * 2 + 1, 3)]
	public void ReliablePacketCount_MatchesLiteNetLibFragmentation(int payloadLength, int expectedPacketCount)
	{
		Assert.AreEqual(expectedPacketCount, RelayBackpressurePolicy.ReliablePacketCount(payloadLength, MaxSinglePacketSize));
	}

	[TestMethod]
	public void ExceedsReliableBacklogAlone_OnlyAboveTheDisconnectThreshold()
	{
		Assert.IsFalse(RelayBackpressurePolicy.ExceedsReliableBacklogAlone(RelayBackpressurePolicy.ReliableQueuePacketsBeforeDisconnect));

		Assert.IsTrue(RelayBackpressurePolicy.ExceedsReliableBacklogAlone(RelayBackpressurePolicy.ReliableQueuePacketsBeforeDisconnect + 1));
	}

	[TestMethod]
	public void ShouldDisconnectReliableForBackpressure_CountsTheNewMessagesPackets()
	{
		const int threshold = RelayBackpressurePolicy.ReliableQueuePacketsBeforeDisconnect;

		Assert.IsFalse(RelayBackpressurePolicy.ShouldDisconnectReliableForBackpressure(threshold - 1, 1, DeliveryMethod.ReliableOrdered));

		Assert.IsTrue(RelayBackpressurePolicy.ShouldDisconnectReliableForBackpressure(threshold, 1, DeliveryMethod.ReliableOrdered));

		Assert.IsFalse(RelayBackpressurePolicy.ShouldDisconnectReliableForBackpressure(threshold - 10, 10, DeliveryMethod.ReliableOrdered));

		Assert.IsTrue(RelayBackpressurePolicy.ShouldDisconnectReliableForBackpressure(threshold - 10, 11, DeliveryMethod.ReliableOrdered));

		Assert.IsFalse(RelayBackpressurePolicy.ShouldDisconnectReliableForBackpressure(threshold, 1, DeliveryMethod.Unreliable));
	}
}
