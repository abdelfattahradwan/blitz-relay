using BlitzRelay.Rooms;

namespace BlitzRelay.Tests;

[TestClass]
public sealed class RoomCodeTests
{
	private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	[TestMethod]
	public void Generate_ReturnsEightCharactersFromTheAlphabet()
	{
		for (int i = 0; i < 1000; i++)
		{
			string roomCode = RoomCode.Generate();

			Assert.AreEqual(8, roomCode.Length);

			Assert.IsTrue(roomCode.All(Alphabet.Contains), $"Room code '{roomCode}' contains a character outside the alphabet.");

			Assert.IsTrue(RoomCode.IsValid(roomCode));
		}
	}

	[TestMethod]
	[DataRow("ABCDEFGH")]
	[DataRow("abcdefgh")]
	[DataRow("23456789")]
	public void IsValid_AcceptsCodesFromTheAlphabetInAnyCase(string roomCode)
	{
		Assert.IsTrue(RoomCode.IsValid(roomCode));
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("ABCDEFG")]
	[DataRow("ABCDEFGHJ")]
	[DataRow("ABCDEFGI")]
	[DataRow("ABCDEFG0")]
	[DataRow("ABCDEFG1")]
	[DataRow("ABCDEFG\n")]
	public void IsValid_RejectsCodesWithTheWrongLengthOrCharacters(string roomCode)
	{
		Assert.IsFalse(RoomCode.IsValid(roomCode));
	}
}
