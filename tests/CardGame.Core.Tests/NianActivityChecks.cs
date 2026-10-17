using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NianActivityChecks
{
    private const string Mode = "fixture:nian-activity", Driver = "fixture:nian-activity-driver", Quiet = "fixture:nian-activity-quiet";
    private const int BossSeat = 3, Limit = 30 * 60 * 1000;

    public static void RoundBagsPreserveNativeStateAcrossExtrasDeathsAndCold()
    {
        var (g, registry) = Start(clock: false); Play(g);
        Require(V(g, BossSeat).GeneralId == Form(0) && !E<NianFormChangedEvent>(g).Any() && E<RoundStartedEvent>(g).Single().RoundNumber == 1,
            "The first real round uses only the initial form, before any shuffled state bag is issued.");
        Use(g, "prepare", BossSeat); Play(g);
        var indulgence = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([BossSeat]));
        Accept(g, new PlayCardCommand(0, indulgence.CardId!.Value, indulgence.TargetSeats, g.Revision, P(g)!.PromptId, indulgence.PlayedCardKind)); Play(g);
        var original = V(g, BossSeat);
        Require(original.Hp == 10 && original.MaxHp == 12 && original.IsChained && original.Equipment.Count == 1 &&
            original.Judgment.Single().Id == indulgence.CardId, "The transition fixture prepares real wounded, chained, equipped and delayed-card state through native commands.");
        Use(g, "extra"); Play(g); var firstTurn = g.State.TurnNumber; End(g);
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > firstTurn);
        Require(E<TurnStartedEvent>(g).Take(2).Select(e => e.ActorSeat).SequenceEqual([0, 0]) && E<RoundStartedEvent>(g).Count() == 1 &&
            !E<NianFormChangedEvent>(g).Any(), "A genuine extra turn returns to the same unfinished normal round and cannot draw another state form."); Cold(g, registry);
        Use(g, "kill", 1); Play(g);
        Require(!V(g, 1).IsAlive && V(g, 2).IsAlive && V(g, BossSeat).IsAlive && g.CreateSnapshot(0).Winner == Winner.None,
            "A native allied death leaves a live match so its normal seat can be skipped.");
        for (var next = 2; next <= 9; next++)
        {
            Drive(g, () => IsRoundGuard(g));
            var oldRound = E<RoundStartedEvent>(g).Last().RoundNumber;
            var before = BossState(g);
            Require(oldRound == next - 1 && g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Any(f => f.RoundNumber == oldRound),
                "The old round's real ending parent retains the state until its optional child returns.");
            if (next is 2 or 6) Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            Drive(g, () => E<NianFormChangedEvent>(g).LastOrDefault()?.RoundNumber == next);
            var changed = E<NianFormChangedEvent>(g).Last();
            Require(changed.BossSeat == BossSeat && changed.GeneralId == V(g, BossSeat).GeneralId &&
                changed.CycleNumber == (next - 2) / 4 + 1 && changed.CyclePosition == (next - 2) % 4 && BossState(g) == before,
                "A form change replaces only the general while preserving exact health, face/chain state and all current original zone entities.");
            Require(E<RoundEndedEvent>(g).Count(e => e.RoundNumber == oldRound) == 1 && E<RoundStartedEvent>(g).Count(e => e.RoundNumber == next) == 1,
                "Only a whole normal seat loop emits one next round and one state change, regardless of a dead seat or an earlier extra turn.");
        }
        var changes = E<NianFormChangedEvent>(g).ToArray(); var bags = E<NianFormCycleStartedEvent>(g).ToArray();
        Require(changes.Select(e => e.RoundNumber).SequenceEqual(Enumerable.Range(2, 8)) && bags.Length == 2 &&
            bags.All(b => b.GeneralIds.Count == 4 && b.GeneralIds.Distinct().Count() == 4 && b.GeneralIds.All(id => id != Form(0)) &&
                b.GeneralIds is System.Collections.IList { IsReadOnly: true }) &&
            changes.Take(4).Select(e => e.GeneralId).SequenceEqual(bags[0].GeneralIds) &&
            changes.Skip(4).Select(e => e.GeneralId).SequenceEqual(bags[1].GeneralIds) &&
            E<TurnStartedEvent>(g).Where(e => e.TurnNumber > firstTurn + 1).All(e => e.ActorSeat != 1),
            "Rounds two through nine consume two independently frozen four-form permutations without repeats inside a bag, and no turn is issued to the dead ally.");
        Cold(g, registry);
    }

    public static void NativeClockExpiresPaidSlashChildWithoutRepaymentAndCold()
    {
        var (g, registry) = Start(clock: true); Play(g);
        Require(g.IsNianClockRunning && g.NianRemainingMilliseconds == Limit && !E<NianClockAdvancedEvent>(g).Any(),
            "Offline setup and ordinary commands do not read or consume wall time; the source limit remains thirty minutes.");
        var unchanged = Views(g);
        Require(!g.Submit(new TickNianClockCommand(0, g.Revision)).Accepted && Views(g) == unchanged &&
            !g.Submit(new TickNianClockCommand(Limit + 1, g.Revision)).Accepted && Views(g) == unchanged,
            "Zero and out-of-range trusted clock deltas reject before committing any state.");
        Accept(g, new TickNianClockCommand(Limit - 1, g.Revision));
        Require(g.NianRemainingMilliseconds == 1 && E<NianClockAdvancedEvent>(g).Single().TotalMilliseconds == Limit - 1,
            "The host commits an explicit elapsed duration into the accepted command journal.");
        var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([BossSeat]));
        Accept(g, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, g.Revision, P(g)!.PromptId, slash.PlayedCardKind));
        Drive(g, () => P(g)?.SkillPrompt?.SkillId == Driver && g.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any());
        var pending = P(g)!; var paid = slash.CardId.Value;
        Require(pending is { PlayerSeat: 0, IsPrivate: true } && pending.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate") &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardId == paid) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == paid).Location == CardLocation.Processing &&
            g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "A real physical Slash is already paid into Processing while its actual source waits on an optional after-damage child.");
        var frames = g.ResolutionStack.Select(f => f.Id).ToHashSet(); Cold(g, registry);
        Require(g.NianRemainingMilliseconds == 1, "Cold restore and replay do not infer extra elapsed time from the current clock.");
        Accept(g, new TickNianClockCommand(1, g.Revision));
        Require(!g.IsNianClockRunning && g.NianRemainingMilliseconds == 0 && g.ResolutionStack.Count == 0 && P(g) is null &&
            E<WinnerDeterminedEvent>(g).Last().TeamId == "team:nian" && E<NianClockExpiredEvent>(g).Count() == 1 &&
            frames.SetEquals(E<NianResolutionCancelledEvent>(g).Select(e => e.FrameId)) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == paid).Location == CardLocation.DiscardPile &&
            g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The deadline cancels the exact whole pending subtree, awards the Nian team and retires the already-paid Slash once without a child return or re-payment.");
        unchanged = Views(g);
        var stale = pending.Choices.First().Id;
        Require(!g.Submit(new AnswerPromptCommand(0, pending.PromptId, stale, g.Revision)).Accepted && Views(g) == unchanged &&
            !g.Submit(new TickNianClockCommand(1, g.Revision)).Accepted && Views(g) == unchanged,
            "An expired response and any later clock tick cannot reopen or mutate the completed challenge.");
        Cold(g, registry);
    }

    private static string Form(int i) => $"fixture:nian-cycle-form-{i}";
    private static bool IsRoundGuard(GameEngine g) => P(g)?.SkillPrompt?.SkillId == Driver &&
        g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Any();
    private static string BossState(GameEngine g)
    {
        var boss = V(g, BossSeat);
        return JsonSerializer.Serialize(new { boss.Hp, boss.MaxHp, boss.IsChained, boss.IsFaceDown, boss.IsAlive,
            Zones = g.CreateCardZoneDiagnostics().Where(c => c.Location.OwnerSeat == BossSeat).OrderBy(c => c.CardId).Select(c => new { c.CardId, c.Location }).ToArray() });
    }
    private static (GameEngine, ContentRegistry) Start(bool clock)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(clock));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:allies", ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Require(!g.IsNianClockRunning && !g.Submit(new TickNianClockCommand(1, g.Revision)).Accepted,
            "The host clock cannot run before a real started setup.");
        Accept(g, new StartGameCommand()); Drive(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:nian-activity-player-0", g.Revision, P(g)!.PromptId)); return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static string Views(GameEngine g) => JsonSerializer.Serialize(Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray());
    private static void Use(GameEngine g, string id, int? target = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 180; i++)
        {
            if (done()) return;
            if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (P(g) is { PlayerSeat: 0 } response && response.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Bounded Nian activity fixture missed its native boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(Views(g) == Views(restored) && JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements) &&
            g.NianRemainingMilliseconds == restored.NianRemainingMilliseconds &&
            JsonSerializer.Serialize(g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray()) ==
            JsonSerializer.Serialize(restored.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray()),
            "Every player view, exact owning runtime cursor, physical movement and explicit remaining clock replays from accepted commands.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Nian activity command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool clock) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("nian-activity-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2},{"op":"setChainedState","target":"selectedTarget","chained":true},{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner"}]}
              ],"triggers":[
                {"id":"old-round-guard","window":"roundEnding","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},
                {"id":"actual-paid-damage-child","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}
              ]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[
                {"id":"preserve-judgment","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["judgment"]}]},
                {"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},
                {"id":"preserve-hand","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}
              ]}]}
            """;
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "Small native Nian transition and timeout fixture." }) }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "Native Nian boundary fixture.") { Program = p, ProgramPresentation = c.Presentations[id] });
            string[] players = ["fixture:nian-activity-player-0", "fixture:nian-activity-player-1", "fixture:nian-activity-player-2"];
            for (var i = 0; i < players.Length; i++) b.AddGeneral(new(players[i], players[i], "", i == 0 ? Driver : Quiet, "qun", BaseHp: 4));
            var forms = Enumerable.Range(0, 5).Select(Form).ToArray();
            foreach (var id in forms) b.AddGeneral(new(id, id, "", Quiet, "god", BaseHp: 12));
            b.AddDeck(new("fixture:nian-activity-deck", "Real bounded fixture materials", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(i < 160 ? clock ? "standard:slash" : "standard:indulgence" : "standard:qinggang_sword", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "Small real Nian boundary", 4, 4, new Dictionary<string, int>(), "fixture:nian-activity-deck", 3,
                players.Concat(forms).ToArray(), ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:nian"] = 1 })
            { BossActivity = new(players, [new("Nian", forms, 4)], 5, [], EnemyTeamId: "team:nian"), NianActivity = new(forms) });
        }
    }
}
