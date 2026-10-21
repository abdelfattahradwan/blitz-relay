using BlitzRelay.Http;
using BlitzRelay.Networking;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlitzRelay.Hosting;

internal static class RelayHost
{
	public static async Task<int> RunAsync(RelayHostOptions options, CancellationToken cancellationToken)
	{
		IHost host = BuildHost(options);

		RelayServerService service = host.Services.GetRequiredService<RelayServerService>();

		await host.RunAsync(cancellationToken);

		return service.ExitCode;
	}

	private static IHost BuildHost(RelayHostOptions options)
	{
		if (options.HttpApiEnabled)
		{
			WebApplicationBuilder webApplicationBuilder = WebApplication.CreateSlimBuilder();

			ConfigureHost(webApplicationBuilder, options);

			return RelayHttpApi.Build(webApplicationBuilder, options);
		}

		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);

		ConfigureHost(builder, options);

		return builder.Build();
	}

	private static void ConfigureHost(IHostApplicationBuilder builder, RelayHostOptions options)
	{
		builder.Logging
			   .ClearProviders()
			   .SetMinimumLevel(options.LogLevel)
			   .AddConsole();

		builder.Services
			   .AddSingleton(serviceProvider => new Server(options.UdpPort, options.ConnectionKey, TimeProvider.System, serviceProvider.GetRequiredService<ILogger<Server>>()))
			   .AddSingleton<RelayServerService>()
			   .AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RelayServerService>());
	}

	private sealed class RelayServerService(Server server, IHostApplicationLifetime lifetime) : BackgroundService
	{
		public int ExitCode { get; private set; }

		protected async override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				ExitCode = await server.RunAsync(stoppingToken);
			}
			catch
			{
				ExitCode = 1;

				throw;
			}
			finally
			{
				StopHost();
			}
		}

		private void StopHost()
		{
			if (lifetime.ApplicationStopping.IsCancellationRequested) return;

			if (lifetime.ApplicationStarted.IsCancellationRequested)
			{
				lifetime.StopApplication();

				return;
			}

			lifetime.ApplicationStarted.Register(lifetime.StopApplication);
		}
	}
}
