using System.Buffers;
using System.Security.Cryptography;

namespace BlitzRelay.Rooms;

public static class RoomCode
{
	private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	private const int CodeLength = 8;

	private static readonly SearchValues<char> ValidCharacters = SearchValues.Create(Alphabet + Alphabet.ToLowerInvariant());

	public static string Generate()
	{
		return RandomNumberGenerator.GetString(Alphabet, CodeLength);
	}

	public static bool IsValid(ReadOnlySpan<char> roomCode)
	{
		return roomCode is { Length: CodeLength } && !roomCode.ContainsAnyExcept(ValidCharacters);
	}
}
