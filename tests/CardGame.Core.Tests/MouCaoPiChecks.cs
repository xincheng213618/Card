using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MouCaoPiChecks
{
    private const string Mode = "identity:classic-mou-cao-pi", Owner = "fixture:mcp-owner", Driver = "fixture:mcp-driver";
    public static void SongAndXingshangCostsBenefitsAndColdReplay()
    {
        var (g, r) = Start();
        Need(Song(g) == 6, "Lord Songwei counts all three other Wei characters at Play start.");
        Use(g, "damage", [1]); Play(g); Need(Song(g) == 8, "A real damage occurrence grants two Song.");
        Use(g, "damage", [2]); Play(g); Need(Song(g) == 8, "A second damage in the same actual turn cannot grant Song again.");
        Use(g, "kill", [1]); Play(g); Need(Song(g) == 9, "Death grants separately and clamps at nine Song.");
        var count = V(g, 0).HandCount;
        Menu(g, "mou:xingshang"); g = Cold(g, r); Choose(g, "draw", 0); Play(g);
        Need(Song(g) == 7 && V(g, 0).HandCount == count + 3 && E<MouCaoPiPaidEvent>(g).Length == 1, "Draw3 consumes exactly two Song once across the real private-menu checkpoint.");
        Use(g, "abolish", []); Play(g);
        Need(V(g, 0).EquipmentSlotCapacities?.GetValueOrDefault(EquipmentSlot.Weapon) == 0, "The real weapon slot is abolished before Xingshang.");
        Use(g, "wound", [0]); Play(g); var hp = V(g, 0).Hp; var max = V(g, 0).MaxHp;
        Menu(g, "mou:xingshang"); Choose(g, "grow", 0);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Driver);
        Need(V(g, 0).Hp == hp + 1 && V(g, 0).MaxHp == max && Song(g) == 4, "The paid chain pauses in the actual recovery child before maximum-HP growth.");
        g = Cold(g, r); var prompt = P(g)!;
        Accept(g, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, g.Revision)); Play(g);
        Need(V(g, 0).Hp == hp + 1 && V(g, 0).MaxHp == max + 1 && Song(g) == 4 &&
            (V(g, 0).EquipmentSlotCapacities?.GetValueOrDefault(EquipmentSlot.Weapon, 1) ?? 1) == 1 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == "mou:xingshang"), "Recovery occurs before max-HP growth and the shared two-use Play quota is consumed.");
        _ = Cold(g, r);
    }
    public static void FangzhuDurationAndPermanentSongweiSkills()
    {
        foreach (var option in new[] { "basic", "response", "skills", "trick", "equipment" })
        {
            var (g, r) = Start(); if (option == "equipment") { Use(g, "damage", [2]); Play(g); }
            var before = Song(g); Menu(g, "mou:fangzhu"); Choose(g, option, 1); Play(g);
            var grant = E<MouCaoPiControlGrantedEvent>(g).Single().Control;
            Need(V(g, 1).MouCaoPiControls is { Count: 1 } && Song(g) == before - (option == "basic" ? 2 : option == "response" ? 4 : option == "equipment" ? 8 : 6), "Each Fangzhu branch pays its actual price and publishes the timed effect.");
            if (option is "basic" or "trick" or "equipment")
            {
                bool Forbidden(CardKind kind, IEnumerable<int> ids, bool response = false) => (bool)typeof(GameEngine).GetMethod("MouCaoPiHandUseForbidden", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [1, kind, ids, response])!;
                var hand = V(g, 1).Hand.Select(c => c.Id).Take(1).ToArray();
                Need(Forbidden(CardKind.Slash, hand) == (option != "basic") && Forbidden(CardKind.DrawTwo, hand) == (option != "trick") &&
                    Forbidden(CardKind.Crossbow, hand) == (option != "equipment") &&
                    !Forbidden(CardKind.Slash, hand, true) && !Forbidden(CardKind.DrawTwo, []) &&
                    !Forbidden(CardKind.DrawTwo, V(g, 0).Hand.Select(c => c.Id).Take(1)),
                    "The actual issued policy uses effective output category and the actor's own Hand material; pure Duel Slash plays and material-free or foreign entities remain legal.");
            }
            if (option == "skills") Need(!(V(g, 1).Skills ?? []).Any(s => s.ContentId == "fixture:mcp-quiet"), "The live binding shard suppresses even a newly queried printed general skill.");
            g = Cold(g, r); End(g);
            Reach(g, () => E<TurnStartedEvent>(g).Any(e => e.ActorSeat == 1));
            Need(V(g, 1).MouCaoPiControls is { Count: 1 }, "Fangzhu remains active at the target's next actual turn start.");
            Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 1));
            Need(V(g, 1).MouCaoPiControls is null && E<MouCaoPiControlExpiredEvent>(g).Single().FrameId == grant.FrameId, "It expires exactly at that target's next actual turn end, including a suppressed skip skill.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start(); Use(g, "damage", [2]); Play(g); Menu(g, "mou:fangzhu"); Choose(g, "turn-over", 1); Play(g);
            Need(V(g, 1).IsFaceDown && Song(g) == 0 && V(g, 1).MouCaoPiControls is null, "Eight Song turns the exact target over immediately without inventing a timed card restriction.");
            Use(g, "kill", [2]); Play(g); Menu(g, "mou:xingshang"); Choose(g, "reset", 1); Play(g);
            Need(!V(g, 1).IsFaceDown && Song(g) == 0, "The separate two-Song reset restores the real general face state."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start(); Menu(g, "mou:songwei"); Choose(g, "remove", 1); Play(g);
            Need(E<CharacterSkillsLostEvent>(g).Any(e => e.Seat == 1 && e.SkillIds.Contains("fixture:mcp-quiet")) && Song(g) == 6 &&
                !g.GetHumanLegalActions().Any(a => a.SkillId == "mou:songwei"), "Songwei permanently loses actual general grants, costs no Song, and consumes its once-game quota.");
            _ = Cold(g, r);
        }
    }
    public static void InheritanceLosesOwnThreeSkillsAndKeepsNonLordPrintedSkills()
    {
        var (g, r) = Start(); Use(g, "kill", [1]); Play(g); Menu(g, "mou:xingshang"); g = Cold(g, r); Choose(g, "inherit", 1); Play(g);
        var lost = E<CharacterSkillsLostEvent>(g).Single(e => e.Seat == 0);
        Need(lost.SkillIds.ToHashSet().SetEquals(new[] { "mou:xingshang", "mou:fangzhu", "mou:songwei" }) &&
            E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0) && E<MouCaoPiInheritedEvent>(g).Length == 1 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId is "mou:xingshang" or "mou:fangzhu" or "mou:songwei"), "Tracing a real corpse grants its printed non-Lord skills and permanently loses the original three skills in one paid chain.");
        _ = Cold(g, r);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static int Song(GameEngine g) => V(g, 0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.CaoPiSong)?.Count ?? 0;
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => g.PendingDecision;
    private static void Need(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(c); Need(result.Accepted, result.Error?.Message ?? "Rejected native command."); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 180; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Unexpected match end at " + line);
            if (P(g) is not { PlayerSeat: 0 } p) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else
            {
                var c = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") == "pass") ?? p.Choices.Last();
                Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
            }
        }
        throw new InvalidOperationException("Mou Cao Pi did not reach boundary " + line + ": " + P(g)?.Kind);
    }
    private static void Use(GameEngine g, string id, int[] targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Menu(GameEngine g, string skill) => Accept(g, new UseProgramSkillCommand(0, skill, "menu", [], [], g.Revision, P(g)!.PromptId));
    private static void Choose(GameEngine g, string option, int target)
    {
        var p = P(g)!; var c = p.Choices.Single(c => c.Parameters.GetValueOrDefault("option") == option && c.Targets.SequenceEqual([target]));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))), "Every viewer cold replays the exact owned menu and issued controls.");
        return copy;
    }
    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-cao-pi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:cao-pi");
            object Act(string id, string op, int amount) => new { id, minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op, target = "selectedTarget", amount } } };
            var programs = new object[] {
                new { id = Driver, revision = 1, activations = new object[] { Act("damage", "damage", 1), Act("kill", "damage", 20), Act("wound", "loseHp", 1),
                    new { id = "abolish", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "abolishEquipmentSlotGroup", target = "owner", equipmentSlots = new[] { "weapon" } } } } },
                    modifiers = new[] { new { id = "keep", query = "handLimit", operation = "add", value = 50, priority = 0 } },
                    triggers = new[] { new { id = "recovery-child", window = "afterHealthChanged", subject = "owner", optional = false,
                        condition = new { kind = "healthChangeKindIs", hpChangeKind = "recovery" }, effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "continue", options = new[] { new { id = "continue" } } } } } } },
                new { id = "fixture:mcp-quiet", revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "实际动作", description = "小夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续真实回复子结算" } }, ["fixture:mcp-quiet"] = new { name = "安静", description = "小夹具" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实行为夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "谋曹丕真实行为", "mou-cao-pi", "mou:xingshang", "wei", 8, ["mou:fangzhu", "mou:songwei", Driver]));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:mcp-peer-" + i).ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, id, "supporter", "fixture:mcp-quiet", "wei", 8));
            b.AddCard(new("fixture:mcp-dodge", "真实闪", "基本牌", "无救援牌的确定性小牌堆", CardKind.Dodge));
            b.AddDeck(new("fixture:mcp-deck", "谋曹丕小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("fixture:mcp-dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "谋曹丕行为", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:mcp-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, ..peers]));
        }
    }
}
