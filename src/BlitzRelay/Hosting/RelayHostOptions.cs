using Microsoft.Extensions.Logging;
using System.Text;

namespace BlitzRelay.Hosting;

public sealed class RelayHostOptions
{
	public required int UdpPort { get; init; }

	public required int HttpPort { get; init; }

	public bool HttpApiEnabled { get; init; } = true;

	public required LogLevel LogLevel { get; init; }

	public required string ConnectionKey { get; init; }

	public required string HttpAdminToken { get; init; }

	public byte[] HttpAdminTokenBytes
	{
		get => field ??= Encoding.UTF8.GetBytes(HttpAdminToken);
	}

	public required CorsConfiguration? Cors { get; init; }

	public required ClientDetails ClientDetails { get; init; }
}
