using BlitzRelay.Protocol;

namespace BlitzRelay.LoadHarness;

internal sealed record LoadSettings
(
	string RelayAddress,
	int RelayPort,
	string ConnectionKey,
	int Rooms,
	int ClientsPerRoom,
	int TickRate,
	int PayloadSize,
	GameChannel GameChannel,
	int Threads,
	TimeSpan Warmup,
	TimeSpan Duration
);
