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
		}
	}
}
