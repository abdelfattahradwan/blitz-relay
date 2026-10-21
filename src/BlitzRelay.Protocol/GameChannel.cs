using JetBrains.Annotations;

namespace BlitzRelay.Protocol;

[PublicAPI]
public enum GameChannel : byte
{
	Reliable = 0,

	Unreliable = 1,

	UnreliableSequenced = 2,
}
