using BlitzRelay.Hosting;
using BlitzRelay.Http;
using BlitzRelay.Rooms;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlitzRelay.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CorsConfiguration))]
[JsonSerializable(typeof(RelayHttpApi.CreateRoomRequest))]
[JsonSerializable(typeof(RelayHttpApi.PatchRoomRequest))]
[JsonSerializable(typeof(IReadOnlyList<RoomSnapshot>))]
[JsonSerializable(typeof(RoomSnapshot))]
[JsonSerializable(typeof(ClientSnapshot[]))]
[JsonSerializable(typeof(ClientIpAddressSnapshot[]))]
[JsonSerializable(typeof(ClientIpEndpointSnapshot[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
