using BlitzRelay.Hosting;
using BlitzRelay.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;

namespace BlitzRelay.Tests;

internal sealed class TestHttpApi : IAsyncDisposable
{
	public const string AdminToken = "blitz-relay-tests-admin-token";

	private readonly WebApplication _application;

	private readonly HttpClient _client;

	private TestHttpApi(TestRelay relay, WebApplication application, HttpClient client)
	{
		Relay = relay;

		_application = application;

		_client = client;
	}

	public TestRelay Relay { get; }

	public static async Task<TestHttpApi> StartAsync(ClientDetails clientDetails = ClientDetails.Ids)
	{
		TestRelay relay = new();

		try
		{
			WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

			builder.Logging.ClearProviders();

			builder.Services.AddSingleton(relay.Server);

			RelayHostOptions relayHostOptions = new()
			{
				UdpPort = 0,

				HttpPort = 0,

				LogLevel = LogLevel.None,

				ConnectionKey = string.Empty,

				HttpAdminToken = AdminToken,

				Cors = null,

				ClientDetails = clientDetails,
			};

			WebApplication application = RelayHttpApi.Build(builder, relayHostOptions);

			await application.StartAsync();

			HttpClient client = new()
			{
				BaseAddress = new Uri($"http://127.0.0.1:{GetPort(application)}"),
			};

			return new TestHttpApi(relay, application, client);
		}
		catch
		{
			await relay.DisposeAsync();

			throw;
		}
	}

	public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, string? json = null)
	{
		using HttpRequestMessage request = new(method, path);

		if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

		return await _client.SendAsync(request);
	}

	public async ValueTask DisposeAsync()
	{
		_client.Dispose();

		await _application.StopAsync();

		await _application.DisposeAsync();

		await Relay.DisposeAsync();
	}

	private static int GetPort(WebApplication application)
	{
		IServerAddressesFeature serverAddresses = application.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>();

		foreach (string address in serverAddresses.Addresses)
		{
			return new Uri(address).Port;
		}

		throw new InvalidOperationException("The HTTP API has no address.");
	}
}
