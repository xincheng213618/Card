using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CardGame.Lan;

/// <summary>Transport only. Local matches continue to use their existing engine and save store.</summary>
public sealed class LanClient : IDisposable
{
    private readonly HttpClient _http;
    public LanClient(string address)
    {
        if (!Uri.TryCreate(address.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("请输入完整服务器地址，例如 http://192.168.1.10:5088。");
        _http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(12) };
    }
    public Task<LanMode[]> Modes(CancellationToken ct = default) => Send<LanMode[]>(HttpMethod.Get, "api/modes", null, null, ct);
    public Task<RoomSummary[]> Rooms(string modeId, CancellationToken ct = default) =>
        Send<RoomSummary[]>(HttpMethod.Get, "api/rooms?modeId=" + Uri.EscapeDataString(modeId), null, null, ct);
    public Task<JoinRoomResult> Create(CreateRoomRequest request, CancellationToken ct = default) =>
        Send<JoinRoomResult>(HttpMethod.Post, "api/rooms", request, null, ct);
    public Task<JoinRoomResult> Join(string roomId, JoinRoomRequest request, CancellationToken ct = default) =>
        Send<JoinRoomResult>(HttpMethod.Post, "api/rooms/" + Uri.EscapeDataString(roomId) + "/join", request, null, ct);
    public Task<RoomView> Read(RoomConnection connection, CancellationToken ct = default) =>
        Send<RoomView>(HttpMethod.Get, Path(connection), null, connection.Token, ct);
    public Task<RoomReply> Edit(RoomConnection connection, RoomEditRequest request, CancellationToken ct = default) =>
        Send<RoomReply>(HttpMethod.Post, Path(connection) + "/edit", request, connection.Token, ct);
    public Task<RoomReply> Submit(RoomConnection connection, RoomInput request, CancellationToken ct = default) =>
        Send<RoomReply>(HttpMethod.Post, Path(connection) + "/input", request, connection.Token, ct);
    public async Task Leave(RoomConnection connection, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Path(connection) + "/member");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await Check(response, ct).ConfigureAwait(false);
    }
    private static string Path(RoomConnection connection) => "api/rooms/" + Uri.EscapeDataString(connection.RoomId);
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: LanProtocol.Json);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await Check(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(LanProtocol.Json, ct).ConfigureAwait(false)
            ?? throw new RoomException("服务器返回了空结果。");
    }
    private static async Task Check(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        LanError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<LanError>(LanProtocol.Json, ct).ConfigureAwait(false); }
        catch (System.Text.Json.JsonException) { }
        throw new RoomException(error?.Error ?? $"服务器返回 {(int)response.StatusCode}。", (int)response.StatusCode);
    }
    public void Dispose() => _http.Dispose();
}
