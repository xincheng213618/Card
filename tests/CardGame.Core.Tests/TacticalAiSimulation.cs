using System.Diagnostics;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TacticalAiSimulation
{
    public static void Inspect(int seed, int count, string output)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = count,
            HumanSeat = -1,
            HumanRole = null,
            UseInteractiveSetup = true,
            AiPolicyVersion = 2,
            MaxTurns = 400
        }, StandardContentRegistry.Create());
        game.Submit(new StartGameCommand());
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            State = game.CreateSnapshot(-1, true),
            Thoughts = game.AiThoughts.TakeLast(30),
            Frames = game.ResolutionStack,
            Events = game.Events.TakeLast(50).Select(item => new { item.Sequence, Type = item.Payload.GetType().Name, Data = JsonSerializer.SerializeToElement(item.Payload, item.Payload.GetType()) })
        }, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    }

    // Diagnostics are reconstructed after each game. Full roles/hand movements
    // are used only to audit decisions; no diagnostic data is passed into an AI.
    public static void Run(string output)
    {
        var matches = new List<MatchMetrics>();
        foreach (var policy in new[] { 1, 2 })
            foreach (var count in new[] { 5, 8 })
            {
                for (var index = 0; index < 64; index++)
                {
                    var seed = 724000 + index * 7919;
                    var game = GameEngine.CreateStandard(new GameOptions
                    {
                        Seed = seed,
                        PlayerCount = count,
                        HumanSeat = -1,
                        HumanRole = null,
                        UseInteractiveSetup = true,
                        AiPolicyVersion = policy,
                        MaxTurns = 400
                    }, StandardContentRegistry.Create());
                    var watch = Stopwatch.StartNew();
                    CommandResult result;
                    try { result = game.Submit(new StartGameCommand()); }
                    catch (Exception exception) { throw new InvalidOperationException($"Full game failed: policy={policy}, count={count}, seed={seed}.", exception); }
                    watch.Stop();
                    if (!result.Accepted || game.State.Status != EngineStatus.Completed || game.ResolutionStack.Count != 0 || game.State.ProcessingCardCount != 0)
                        throw new InvalidOperationException($"Unfinished game: policy={policy}, count={count}, seed={seed}, status={game.State.Status}, winner={game.State.Winner}, frames={game.ResolutionStack.Count}, processing={game.State.ProcessingCardCount}.");
                    var zones = game.CreateCardZoneDiagnostics();
                    if (zones.Count != 90 || zones.Select(card => card.CardId).Distinct().Count() != 90)
                        throw new InvalidOperationException("Physical card inventory changed.");
                    matches.Add(Audit(game, seed, count, policy, watch.Elapsed.TotalMilliseconds));
                }
                Console.WriteLine($"Completed 64 full matches: policy {policy}, {count} seats.");
            }
        var summary = matches.GroupBy(match => match.Policy).Select(group => new
        {
            Policy = group.Key,
            Matches = group.Count(),
            MeanTurns = Math.Round(group.Average(match => match.Turns), 1),
            MaxTurns = group.Max(match => match.Turns),
            MeanMilliseconds = Math.Round(group.Average(match => match.Milliseconds), 1),
            Outcomes = group.GroupBy(match => match.Winner).ToDictionary(outcome => outcome.Key, outcome => outcome.Count()),
            GroupAttacks = group.Sum(match => match.GroupAttacks),
            CriticalLordGroupAttacks = group.Sum(match => match.CriticalLordGroupAttacks),
            Gardens = group.Sum(match => match.Gardens),
            OnlyEnemyLordHealed = group.Sum(match => match.OnlyEnemyLordHealed),
            WinesUsed = group.Sum(match => match.WinesUsed),
            WineExpiredUnused = group.Sum(match => match.WineExpiredUnused),
            RenegadeLordRescueOpportunities = group.Sum(match => match.RenegadeLordRescueOpportunities),
            RenegadeDeclinedLordWithPeach = group.Sum(match => match.RenegadeDeclinedLordWithPeach),
            SelfPeachWhileWineHeld = group.Sum(match => match.SelfPeachWhileWineHeld)
        }).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Method = "64 fixed seeds x 2 seat counts x 2 policies; interactive setup, all AI, 400-turn cap. Counts audit typed events and trusted diagnostics after each full game. Self-play outcomes do not establish playing strength.",
            Summary = summary,
            Matches = matches
        }, options));
        Console.WriteLine(JsonSerializer.Serialize(summary, options));
        Console.WriteLine($"Saved full per-match evidence: {Path.GetFullPath(output)}");
    }

    private static MatchMetrics Audit(GameEngine game, int seed, int count, int policy, double milliseconds)
    {
        var players = game.CreateSnapshot(-1, true).Players;
        var roles = players.ToDictionary(player => player.Seat, player => player.Role!.Value);
        var hp = players.ToDictionary(player => player.Seat, player => player.MaxHp);
        var maxHp = players.ToDictionary(player => player.Seat, player => player.MaxHp);
        var alive = players.Select(player => player.Seat).ToHashSet();
        var hands = players.ToDictionary(player => player.Seat, _ => new Dictionary<int, CardKind>());
        var dying = new Dictionary<long, int>();
        var lord = roles.Single(pair => pair.Value == Role.Lord).Key;
        var metric = new MatchMetrics(seed, count, policy, game.State.Winner.ToString(), game.State.TurnNumber, Math.Round(milliseconds, 2));
        foreach (var item in game.Events)
        {
            switch (item.Payload)
            {
                case CardMovedEvent move:
                    if (move.From.Zone == CardZoneKind.Hand) hands[move.From.OwnerSeat!.Value].Remove(move.CardId);
                    if (move.To.Zone == CardZoneKind.Hand) hands[move.To.OwnerSeat!.Value][move.CardId] = move.CardKind;
                    break;
                case DamageAppliedEvent damage: hp[damage.TargetSeat] = damage.RemainingHp; break;
                case RecoveryAppliedEvent recovery: hp[recovery.TargetSeat] = recovery.RemainingHp; break;
                case PlayerDiedEvent death: alive.Remove(death.VictimSeat); break;
                case PlayerDyingEvent request: dying[request.ResolutionId] = request.VictimSeat; break;
                case DyingResponseEvent response:
                    var victim = dying[response.ResolutionId];
                    if (victim == lord && roles[response.ResponderSeat] == Role.Renegade && alive.Count > 2 &&
                        (response.UsedPeach || hands[response.ResponderSeat].Values.Contains(CardKind.Peach)))
                    {
                        metric.RenegadeLordRescueOpportunities++;
                        if (!response.UsedPeach) metric.RenegadeDeclinedLordWithPeach++;
                    }
                    if (victim == response.ResponderSeat && response.UsedPeach && hands[response.ResponderSeat].Values.Contains(CardKind.Alcohol))
                        metric.SelfPeachWhileWineHeld++;
                    break;
                case DyingResolvedEvent resolved: dying.Remove(resolved.ResolutionId); break;
                case AlcoholExpiredEvent: metric.WineExpiredUnused++; break;
                case AlcoholAppliedEvent: metric.WinesUsed++; break;
                case CardUseDeclaredEvent use:
                    if (use.CardKind is CardKind.BarbarianAssault or CardKind.ArrowBarrage)
                    {
                        metric.GroupAttacks++;
                        if (hp[lord] <= 1 && alive.Contains(lord) &&
                            (roles[use.SourceSeat] == Role.Loyalist || roles[use.SourceSeat] == Role.Renegade && alive.Count > 2))
                            metric.CriticalLordGroupAttacks++;
                    }
                    if (use.CardKind == CardKind.PeachGarden)
                    {
                        metric.Gardens++;
                        if (roles[use.SourceSeat] == Role.Rebel && hp[lord] < maxHp[lord] && alive.Contains(lord) &&
                            alive.All(seat => seat == lord || hp[seat] == maxHp[seat])) metric.OnlyEnemyLordHealed++;
                    }
                    break;
            }
        }
        return metric;
    }

    private sealed record MatchMetrics(int Seed, int Players, int Policy, string Winner, int Turns, double Milliseconds)
    {
        public int GroupAttacks { get; set; }
        public int CriticalLordGroupAttacks { get; set; }
        public int Gardens { get; set; }
        public int OnlyEnemyLordHealed { get; set; }
        public int WinesUsed { get; set; }
        public int WineExpiredUnused { get; set; }
        public int RenegadeLordRescueOpportunities { get; set; }
        public int RenegadeDeclinedLordWithPeach { get; set; }
        public int SelfPeachWhileWineHeld { get; set; }
    }
}
