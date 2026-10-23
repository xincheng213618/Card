using System.Text.Json.Serialization;
using CardGame.Lan;

namespace CardGame.Server;

public static class LanServer
{
    public static WebApplication Build(string[] args, RoomDirectory? directory = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton(directory ?? new RoomDirectory());
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { await next(); }
            catch (RoomException error)
            {
                context.Response.StatusCode = error.StatusCode;
                await context.Response.WriteAsJsonAsync(new LanError(error.Message));
            }
            catch (Exception error)
            {
                app.Logger.LogError(error, "LAN room request failed: {Path}", context.Request.Path);
                if (context.Response.HasStarted) throw;
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new LanError("服务器处理失败；请查看服务端日志。"));
            }
        });
        app.MapGet("/health", () => new { status = "ok", protocolVersion = LanProtocol.Version });
        app.MapGet("/api/modes", () => LanContent.Modes);
        app.MapGet("/api/rooms", (string? modeId, RoomDirectory rooms) => rooms.List(modeId));
        app.MapPost("/api/rooms", (CreateRoomRequest request, RoomDirectory rooms) => rooms.Create(request));
        app.MapPost("/api/rooms/{id}/join", (string id, JoinRoomRequest request, RoomDirectory rooms) =>
        {
            RoomDirectory.CheckProtocol(request.ProtocolVersion);
            return rooms.Get(id).Join(request.PlayerName, request.Seat);
        });
        app.MapGet("/api/rooms/{id}", (string id, HttpContext context, RoomDirectory rooms) => rooms.Get(id).Read(Token(context)));
        app.MapPost("/api/rooms/{id}/edit", (string id, RoomEditRequest request, HttpContext context, RoomDirectory rooms) =>
            rooms.Get(id).Edit(Token(context), request));
        app.MapPost("/api/rooms/{id}/input", (string id, RoomInput request, HttpContext context, RoomDirectory rooms) =>
            rooms.Get(id).Submit(Token(context), request));
        app.MapDelete("/api/rooms/{id}/member", (string id, HttpContext context, RoomDirectory rooms) =>
        { rooms.Get(id).Leave(Token(context)); return Results.NoContent(); });
        return app;
    }
    private static string Token(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.Ordinal) || value.Length != 71)
            throw new RoomException("请先加入房间。", 401);
        return value[7..];
    }
}
