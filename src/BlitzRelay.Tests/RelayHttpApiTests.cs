using BlitzRelay.Hosting;
using BlitzRelay.Networking;
using BlitzRelay.Protocol;
using BlitzRelay.Rooms;
using System.Net;
using System.Reflection;
using System.Text.Json;
using static BlitzRelay.Tests.RelayFlows;

namespace BlitzRelay.Tests;

[TestClass]
public sealed class RelayHttpApiTests
{
	[TestMethod]
	public async Task Health_DoesNotRequireAToken()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, "/health", null);

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
	}

	[TestMethod]
	public async Task Version_ReturnsTheRelaysVersion()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, "/version", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		string expectedVersion = typeof(Server).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

		Assert.AreEqual(expectedVersion, document.RootElement.GetProperty("version").GetString());
	}

	[TestMethod]
	[DataRow("GET", "/version")]
	[DataRow("POST", "/rooms")]
	[DataRow("GET", "/rooms")]
	[DataRow("GET", "/rooms/{roomCode}")]
	[DataRow("GET", "/rooms/{roomCode}/clients")]
	[DataRow("DELETE", "/rooms/{roomCode}/host")]
	public async Task AdminOnlyEndpoints_RejectMissingWrongAndHostTokens(string method, string path)
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using RelayTestPeer host = await api.Relay.ConnectAsync();

		host.Send(MessageCodec.CreateHostRegister(4));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string roomCode, out string hostToken));

		string roomPath = path.Replace("{roomCode}", roomCode);

		string? json = method == "POST" ? "{}" : null;

		foreach (string? token in (string?[])[null, "wrong-token", hostToken])
		{
			using HttpResponseMessage response = await api.SendAsync(new HttpMethod(method), roomPath, token, json);

			Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"{method} {path} with token '{token}'");
		}

		using HttpResponseMessage adminResponse = await api.SendAsync(new HttpMethod(method), roomPath, TestHttpApi.AdminToken, json);

		Assert.AreNotEqual(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
	}

	[TestMethod]
	public async Task RoomEditEndpoints_AcceptOnlyThatRoomsHostToken()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using RelayTestPeer host = await api.Relay.ConnectAsync();

		using RelayTestPeer otherHost = await api.Relay.ConnectAsync();

		host.Send(MessageCodec.CreateHostRegister(4));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await host.ReceiveAsync(), out string roomCode, out string hostToken));

		otherHost.Send(MessageCodec.CreateHostRegister(4));

		Assert.IsTrue(MessageCodec.TryReadRoomCreated(await otherHost.ReceiveAsync(), out _, out string otherHostToken));

		using HttpResponseMessage patchWithOtherToken = await api.SendAsync(HttpMethod.Patch, $"/rooms/{roomCode}", otherHostToken, """{"displayName":"Renamed"}""");

		Assert.AreEqual(HttpStatusCode.Unauthorized, patchWithOtherToken.StatusCode);

		using HttpResponseMessage patchWithHostToken = await api.SendAsync(HttpMethod.Patch, $"/rooms/{roomCode}", hostToken, """{"displayName":"Renamed"}""");

		Assert.AreEqual(HttpStatusCode.OK, patchWithHostToken.StatusCode);

		using HttpResponseMessage kickWithOtherToken = await api.SendAsync(HttpMethod.Delete, $"/rooms/{roomCode}/clients/99", otherHostToken);

		Assert.AreEqual(HttpStatusCode.Unauthorized, kickWithOtherToken.StatusCode);

		using HttpResponseMessage kickWithHostToken = await api.SendAsync(HttpMethod.Delete, $"/rooms/{roomCode}/clients/99", hostToken);

		Assert.AreEqual(HttpStatusCode.NotFound, kickWithHostToken.StatusCode);

		using HttpResponseMessage deleteWithOtherToken = await api.SendAsync(HttpMethod.Delete, $"/rooms/{roomCode}", otherHostToken);

		Assert.AreEqual(HttpStatusCode.Unauthorized, deleteWithOtherToken.StatusCode);

		using HttpResponseMessage deleteWithHostToken = await api.SendAsync(HttpMethod.Delete, $"/rooms/{roomCode}", hostToken);

		Assert.AreEqual(HttpStatusCode.NoContent, deleteWithHostToken.StatusCode);
	}

	[TestMethod]
	public async Task RemoveHost_ReturnsTheStatusForEachCase()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using RelayTestPeer ephemeralHost = await api.Relay.ConnectAsync();

		using RelayTestPeer promoted = await api.Relay.ConnectAsync();

		using RelayTestPeer persistentHost = await api.Relay.ConnectAsync();

		string ephemeralRoomCode = await RegisterHostAsync(ephemeralHost, 4);

		RoomSnapshot reservedRoom = api.Relay.Server.CreateReservedRoom(4, "Reserved", false, null);

		using HttpResponseMessage missingRoom = await api.SendAsync(HttpMethod.Delete, "/rooms/MISSING1/host", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.NotFound, missingRoom.StatusCode);

		using HttpResponseMessage ephemeralRoom = await api.SendAsync(HttpMethod.Delete, $"/rooms/{ephemeralRoomCode}/host", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.Conflict, ephemeralRoom.StatusCode);

		using HttpResponseMessage hostlessRoom = await api.SendAsync(HttpMethod.Delete, $"/rooms/{reservedRoom.Code}/host", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.Conflict, hostlessRoom.StatusCode);

		string claimToken = await JoinAsPromotedAsync(promoted, reservedRoom.Code);

		await ClaimAsync(promoted, persistentHost, reservedRoom.Code, claimToken);

		using HttpResponseMessage hostedRoom = await api.SendAsync(HttpMethod.Delete, $"/rooms/{reservedRoom.Code}/host", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.NoContent, hostedRoom.StatusCode);

		Assert.IsTrue(MessageCodec.TryReadError(await persistentHost.WaitForDisconnectAsync(), out ErrorCode errorCode));

		Assert.AreEqual(ErrorCode.HostRemoved, errorCode);
	}

	[TestMethod]
	[DataRow(ClientDetails.Ids, new[] { "virtualClientId" })]
	[DataRow(ClientDetails.IpAddresses, new[] { "virtualClientId", "ipAddress" })]
	[DataRow(ClientDetails.IpEndpoints, new[] { "virtualClientId", "ipAddress", "port" })]
	public async Task RoomClients_ReturnOnlyTheConfiguredFields(ClientDetails clientDetails, string[] expectedPropertyNames)
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync(clientDetails);

		using RelayTestPeer host = await api.Relay.ConnectAsync();

		using RelayTestPeer client = await api.Relay.ConnectAsync();

		string roomCode = await RegisterHostAsync(host, 4);

		await JoinAsync(host, client, roomCode);

		using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, $"/rooms/{roomCode}/clients", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

		using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		Assert.AreEqual(1, document.RootElement.GetArrayLength());

		List<string> propertyNames = [];

		foreach (JsonProperty property in document.RootElement[0].EnumerateObject())
		{
			propertyNames.Add(property.Name);
		}

		Assert.AreSequenceEqual(expectedPropertyNames, propertyNames);
	}

	[TestMethod]
	public async Task CreateRoom_ValidatesTheRequest()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using HttpResponseMessage noClients = await api.SendAsync(HttpMethod.Post, "/rooms", TestHttpApi.AdminToken, """{"maximumClients":0}""");

		Assert.AreEqual(HttpStatusCode.BadRequest, noClients.StatusCode);

		using HttpResponseMessage longDisplayName = await api.SendAsync(HttpMethod.Post, "/rooms", TestHttpApi.AdminToken, $$"""{"displayName":"{{new string('a', 256)}}"}""");

		Assert.AreEqual(HttpStatusCode.BadRequest, longDisplayName.StatusCode);

		using HttpResponseMessage nullDisplayName = await api.SendAsync(HttpMethod.Post, "/rooms", TestHttpApi.AdminToken, """{"displayName":null}""");

		Assert.AreEqual(HttpStatusCode.BadRequest, nullDisplayName.StatusCode);

		using HttpResponseMessage nullMetadataValue = await api.SendAsync(HttpMethod.Post, "/rooms", TestHttpApi.AdminToken, """{"metadata":{"key":null}}""");

		Assert.AreEqual(HttpStatusCode.BadRequest, nullMetadataValue.StatusCode);

		using HttpResponseMessage created = await api.SendAsync(HttpMethod.Post, "/rooms", TestHttpApi.AdminToken, """{"maximumClients":4,"displayName":"Room"}""");

		Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);

		using JsonDocument document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

		Assert.IsTrue(RoomCode.IsValid(document.RootElement.GetProperty("code").GetString()!));

		Assert.AreEqual(4, document.RootElement.GetProperty("maximumClients").GetInt32());

		Assert.AreEqual("Room", document.RootElement.GetProperty("displayName").GetString());
	}

	[TestMethod]
	public async Task PatchRoom_WithNullMetadata_ReturnsBadRequestAndChangesNothing()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		RoomSnapshot room = api.Relay.Server.CreateReservedRoom(4, "Room", false, null);

		using HttpResponseMessage nullValue = await api.SendAsync(HttpMethod.Patch, $"/rooms/{room.Code}", TestHttpApi.AdminToken, """{"displayName":"Renamed","metadataToAdd":{"key":null}}""");

		Assert.AreEqual(HttpStatusCode.BadRequest, nullValue.StatusCode);

		using HttpResponseMessage nullKey = await api.SendAsync(HttpMethod.Patch, $"/rooms/{room.Code}", TestHttpApi.AdminToken, """{"displayName":"Renamed","metadataToRemove":[null]}""");

		Assert.AreEqual(HttpStatusCode.BadRequest, nullKey.StatusCode);

		Assert.AreEqual("Room", api.Relay.Server.GetRoomSnapshot(room.Code)?.DisplayName);
	}

	[TestMethod]
	public async Task GetRoom_WhenRoomIsMissing_ReturnsNotFound()
	{
		await using TestHttpApi api = await TestHttpApi.StartAsync();

		using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, "/rooms/MISSING1", TestHttpApi.AdminToken);

		Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
	}
}
