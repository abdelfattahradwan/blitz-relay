using System.Security.Cryptography;

namespace BlitzRelay.Rooms;

public static class RoomCode
{
	private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	private const int CodeLength = 8;

	public static string Generate()
	{
		return RandomNumberGenerator.GetString(Alphabet, CodeLength);
	}
}
