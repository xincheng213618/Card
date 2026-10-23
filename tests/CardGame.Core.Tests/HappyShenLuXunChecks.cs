using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenLuXunChecks
{
    private const string Hero = "happy:shen-lu-xun", Junlue = "happy:junlue", Cuike = "happy:cuike";
    private const string Driver = "fixture:happy-luxun-driver", Quiet = "fixture:happy-luxun-quiet", Watch = "fixture:happy-luxun-watch", Deck = "fixture:happy-luxun-deck";

    public static void PopulationThresholdChosenSubsetAndNativeDamageCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry);
        Accept(g, new StartGameCommand()); Offer(g, "even-chain-discard"); Skip(g); Play(g);
        Need(V(g).Hp == 4 && V(g).MaxHp == 4 && Mark(g) == 0, "The formal Happy general begins at four HP without manufactured military marks.");
        Damage(g, "four", 2); Play(g);
        Need(Mark(g) == 4 && E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 4) == 1,
            "One native four-point damage really produces four separate Junlue points.");
        End(g); Offer(g, "even-chain-discard"); Skip(g); Play(g);
        Need(Mark(g) == 4 && E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Cuike || e.BindingId != "mass-damage"),
            "Exactly four marks among four living roles does not offer the strict greater-than-population mass branch.");
        Damage(g, "one", 2); Play(g);
        Need(Mark(g) == 5, "A further real one-point damage raises Junlue to five, still below the old eight-mark gate.");
        End(g); Offer(g, "odd-damage"); Skip(g); Offer(g, "mass-damage");
        var health = Enumerable.Range(0, 4).Select(s => V(g, s).Hp).ToArray();
        Answer(g, c => Key(c) == "activate");
        Reach(g, p => p.SkillPrompt?.SkillId == Cuike && p.Choices.Any(c => Key(c) == "select-targets"));
        var mass = Mass(g)!; var id = mass.Id;
        Need(Mark(g) == 0 && mass.WindowContext?.Facts?.LivingPlayerCount == 4 &&
            P(g)!.Choices.Count == 8 && P(g)!.Choices.All(c => c.Targets.All(s => s != 0)) &&
            P(g)!.Choices.Any(c => c.Targets.Count == 0) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1, 2, 3])) &&
            P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1, 3])) &&
            E<PlayerMarkerChangedEvent>(g).Count(e => e.ResolutionId == id && e.Marker == PlayerMarkerKind.Junlue && e.Delta == -5 && e.Count == 0) == 1,
            "Five exceeds the current four-person field: accepting pays all five marks once and exposes all eight subsets of the three genuine others, including zero, full and a proper subset.");
        Answer(g, c => c.Targets.SequenceEqual([1, 3]));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch && Mass(g) is not null);
        mass = Mass(g)!;
        Need(mass.Id == id && mass.SelectedTargetSeats.SequenceEqual([1, 3]) && mass.ReexecuteParticipantInstruction &&
            mass.NumberBindings.Any(b => b.Name == "participant-cursor-2" && b.Value == 1) &&
            V(g, 1).Hp == health[1] - 1 && V(g, 2).Hp == health[2] && V(g, 3).Hp == health[3] && Mark(g) == 0,
            "The original paid subset owns the real first-target damage and next-participant cursor while its native after-damage child pauses before granting the new Junlue point.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g).Hp == health[0] && V(g, 1).Hp == health[1] - 1 && V(g, 2).Hp == health[2] && V(g, 3).Hp == health[3] - 1 && Mark(g) == 2 &&
            E<PlayerMarkerChangedEvent>(g).Count(e => e.ResolutionId == id && e.Marker == PlayerMarkerKind.Junlue && e.Delta == -5) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.SkillId == Cuike && e.BindingId == "mass-damage" && e.Completed) == 1 &&
            g.ResolutionStack.All(f => f.Id != id),
            "Cold return damages each selected other once, leaves the omitted target and owner unchanged, pays no second cost and preserves both newly earned military marks.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The complete native journal replays strict live-population qualification, real mark payment, the proper subset and the resumed participant damage cursor.");
    }

    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "happy-lu-xun", Hero, 4, 0, 0, 0, 0, [Driver, Watch], [], [],
            [new(Peer(2)) { StartingHand = [] }, new(Peer(3)) { StartingHand = [] }], [new(Peer(1)) { StartingHand = [] }], new string('f', 64)) { DefaultGodFactionId = "wu" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 };
    }
    private static string Peer(int seat) => "fixture:happy-luxun-peer-" + seat;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static int Mark(GameEngine g) => V(g).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.Junlue)?.Count ?? 0;
    private static ProgramSkillFrame? Mass(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.SkillId == Cuike && f.TriggerId == "mass-damage");
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option-id");
    private static void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Happy Lu Xun native command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing Happy Lu Xun choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Offer(GameEngine g, string binding) => Reach(g, p => p.SkillPrompt?.SkillId == Cuike && p.Choices.Any(c => Key(c) == "activate" && c.Parameters.GetValueOrDefault("binding-id") == binding));
    private static void Skip(GameEngine g) => Answer(g, c => Key(c) == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => Option(c) == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Damage(GameEngine g, string binding, int target) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], [target], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Happy Lu Xun boundary: " + JsonSerializer.Serialize(new
                {
                    Prompt = p, Marks = Mark(g), Turn = g.State.TurnNumber, Mass = Mass(g),
                    CuikeStarts = E<ProgramBindingStartedEvent>(g).Where(e => e.SkillId == Cuike).ToArray(),
                    CuikeReturns = E<ProgramBindingResolvedEvent>(g).Where(e => e.SkillId == Cuike).ToArray(),
                    PaidMarkers = E<PlayerMarkerChangedEvent>(g).Where(e => e.Marker == PlayerMarkerKind.Junlue && e.Delta < 0).ToArray()
                }));
            }
            Need(g.State.Winner == Winner.None, "The short real-turn Happy Lu Xun fixture must retain its next boundary.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Happy Lu Xun missed the bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold restoration preserves all views, real marker cost, frozen selected subset and the native participant damage child."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-lu-xun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"four","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},
                {"id":"one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"after-real-damage","isSkillInvocation":false,"window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,"priority":100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实伤害驱动", description = "通过原生伤害结算军略。" }, [Quiet] = new { name = "安静角色", description = "跳过其他角色出牌。" }, [Watch] = new { name = "伤害后子窗", description = "暂停原生伤害后结算。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new(Peer(s), "真实同行", "supporter", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 16).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐陆逊真实人数与子集", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
