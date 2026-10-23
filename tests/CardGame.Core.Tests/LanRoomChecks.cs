using System.Text.Json;
using CardGame.Core;
using CardGame.Lan;
using CardGame.Server;

internal static class LanRoomChecks
{
    public static void HttpPlayersAiAndIsolation() => Run(requireCompletion: true).GetAwaiter().GetResult();
    public static void HttpFirstRound() => Run(requireCompletion: false).GetAwaiter().GetResult();

    private static async Task Run(bool requireCompletion)
    {
        await using var app = LanServer.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"],
            new RoomDirectory(() => 337));
        await app.StartAsync();
        try
        {
            using var host = new LanClient(app.Urls.Single());
            using var guest = new LanClient(app.Urls.Single());
            var created = await host.Create(new("回环联机检查", "房主", "identity:lan-5"));
            var joined = await guest.Join(created.Connection.RoomId, new("朋友", Seat: 2));
            var roomId = created.Connection.RoomId;
            var h = created.Connection;
            var g = joined.Connection;
            Require((await host.Rooms("identity:lan-8")).Length == 0, "Mode filtering mixed room lists.");
            Require((await host.Rooms("identity:lan-5")).Single().Humans == 2, "Joining did not add a human seat.");
            try { await host.Read(h with { Token = new string('0', 64) }); throw new Exception("Forged token accepted."); }
            catch (RoomException error) when (error.StatusCode == 401) { }
            try { await guest.Edit(g, new(joined.View.Version, "ai:fill")); throw new Exception("Guest managed host seats."); }
            catch (RoomException error) when (error.StatusCode == 403) { }
            var view = await host.Read(h);
            var ai = await host.Edit(h, new(view.Version, "ai:add", 1));
            Require(ai.Accepted && ai.View.Seats[1].Kind == RoomSeatKind.Ai, "Explicit add AI failed.");
            var removed = await host.Edit(h, new(ai.View.Version, "ai:remove", 1));
            Require(removed.Accepted && removed.View.Seats[1].Kind == RoomSeatKind.Empty, "Remove AI failed.");
            var fill = await host.Edit(h, new(removed.View.Version, "ai:fill"));
            Require(fill.View.Room.Ai == 3, "Fill replaced a human or left a gap.");
            Require(!(await host.Edit(h, new(fill.View.Version, "start"))).Accepted, "Unready guest did not block start.");
            var ready = await guest.Edit(g, new(fill.View.Version, "ready", Ready: true));
            Require(ready.Accepted, "Guest could not ready.");
            Require(!(await host.Edit(h, new(fill.View.Version, "start"))).Accepted, "Stale room revision was accepted.");
            var started = await host.Edit(h, new(ready.View.Version, "start"));
            Require(started.Accepted && started.View.Room.Phase == RoomPhase.Playing, "Room could not start.");
            var selected = new HashSet<int>();
            var acted = new HashSet<int>();
            var testedRetry = false;
            var completed = false;
            var clients = new[] { (Client: host, Connection: h), (Client: guest, Connection: g) };
            for (var step = 0; step < 600; step++)
            {
                var views = await Task.WhenAll(clients.Select(client => client.Client.Read(client.Connection)));
                if (views[0].Room.Phase == RoomPhase.Completed) { completed = true; break; }
                Require(views.All(v => v.Room.Phase == RoomPhase.Playing), "Room faulted during match.");
                foreach (var v in views)
                {
                    Require(v.Game is not null && v.Game.HumanSeat == v.ViewerSeat && v.Game.Seed is null, "Primary-view or seed leak.");
                    Require(v.Game!.Players.All(player => player.Seat == v.ViewerSeat || player.Hand.Count == 0), "Foreign hand leaked.");
                    var json = JsonSerializer.Serialize(v, LanProtocol.Json);
                    Require(!json.Contains(h.Token) && !json.Contains(g.Token) && !json.Contains("acceptedCommands", StringComparison.OrdinalIgnoreCase), "Host data leaked.");
                }
                var index = Array.FindIndex(views, v => v.Game!.PendingDecision is not null);
                Require(index >= 0 && views.Count(v => v.Game!.PendingDecision is not null) == 1, "No unique human input boundary.");
                var current = views[index];
                var prompt = current.Game!.PendingDecision!;
                if (prompt.Kind == DecisionKind.SelectGeneral) selected.Add(current.ViewerSeat);
                if (prompt.Kind == DecisionKind.PlayCard) acted.Add(current.ViewerSeat);
                var action = Choose(current);
                var input = new RoomInput(Guid.NewGuid(), current.Game.Revision, prompt.PromptId.Value, action.Id,
                    action.CardIds?.Take(action.MinCards).ToArray(), action.TargetSeats?.Take(action.MinTargets).ToArray());
                if (!testedRetry)
                {
                    var other = clients[1 - index];
                    var forged = await other.Client.Submit(other.Connection, input);
                    Require(!forged.Accepted && forged.View.Game!.Revision == current.Game.Revision, "Other connection acted as this seat.");
                }
                var client = clients[index];
                var replies = !testedRetry
                    ? await Task.WhenAll(client.Client.Submit(client.Connection, input), client.Client.Submit(client.Connection, input))
                    : [await client.Client.Submit(client.Connection, input)];
                var reply = replies[0];
                Require(replies.All(result => result.Accepted && result.View.Game!.Revision == reply.View.Game!.Revision), "Concurrent duplicate input was not committed once.");
                Require(reply.Accepted, $"Remote input {prompt.Kind}/{action.Label} rejected: {reply.Error}");
                if (!testedRetry)
                {
                    var retry = await client.Client.Submit(client.Connection, input);
                    Require(retry.Accepted && retry.View.Game!.Revision == reply.View.Game!.Revision, "Retry executed an operation twice.");
                    var changed = await client.Client.Submit(client.Connection, input with { ActionId = "forged" });
                    Require(!changed.Accepted, "Request id reuse accepted different input.");
                    using var reconnected = new LanClient(app.Urls.Single());
                    Require((await reconnected.Read(client.Connection)).Game!.Revision == reply.View.Game!.Revision, "Reconnect lost the seat.");
                    var stale = await client.Client.Submit(client.Connection, input with { RequestId = Guid.NewGuid() });
                    Require(!stale.Accepted, "Old prompt was accepted with a fresh request id.");
                    testedRetry = true;
                }
                if (!requireCompletion && selected.Count == 2 && acted.Count == 2) break;
            }
            Require(selected.Count == 2 && acted.Count == 2 && (completed || !requireCompletion),
                $"Expected both humans to select/play{(requireCompletion ? " and finish a match" : "")}: {selected.Count}/{acted.Count}/{completed}.");

            var eight = await host.Create(new("八人身份", "八人房主", "identity:lan-8"));
            var eighthSeat = await guest.Join(eight.Connection.RoomId, new("末位真人", Seat: 7));
            var eightFill = await host.Edit(eight.Connection, new(eighthSeat.View.Version, "ai:fill"));
            var eightReady = await guest.Edit(eighthSeat.Connection, new(eightFill.View.Version, "ready", Ready: true));
            var eightStart = await host.Edit(eight.Connection, new(eightReady.View.Version, "start"));
            Require(eightStart.Accepted && eightStart.View.Room.Ai == 6 && eightStart.View.Game!.Players.Count == 8,
                "Eight-seat room did not preserve both humans and six AI.");
            Require((await guest.Read(eighthSeat.Connection)).Game!.HumanSeat == 7, "Last seat did not get its own view.");
        }
        finally { await app.StopAsync(); }
    }

    private static LanAction Choose(RoomView view)
    {
        var p = view.Game!.PendingDecision!;
        if (p.Kind != DecisionKind.PlayCard) return view.Actions[0];
        // Exercise real cards while avoiding speculative multi-material skill combinations in this transport fixture.
        return view.Actions.FirstOrDefault(action => action.MaxCards == 0 && action.MaxTargets == 0 &&
            !action.Label.Contains("结束") && !action.Label.Contains("苦肉"))
            ?? view.Actions.First(action => action.Label.Contains("结束"));
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
