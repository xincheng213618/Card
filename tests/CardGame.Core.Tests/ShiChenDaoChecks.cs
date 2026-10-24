using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiChenDaoChecks
{
    private const string Hero = "mobile:shi-chen-dao", Wanglie = "mobile:shi-wanglie", Hongyi = "mobile:shi-hongyi",
        Driver = "fixture:shi-chen-dao-driver", Quiet = "fixture:shi-chen-dao-quiet", Peer = "fixture:shi-chen-dao-peer", Deck = "fixture:shi-chen-dao-deck";

    public static void SelectedEntityDistanceNoResponseAndOnlyOtherTargetBanCold()
    {
        var s = Scene.Start(); s.Prepare("draw"); s.SelectWanglie(CardKind.Slash); s.Play();
        var selected = s.E<MobileShiWanglieSelectedEvent>().Single().CardId;
        var unselected = s.V(0).Hand.First(c => c.Kind == CardKind.Slash && c.Id != selected).Id;
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == selected && a.TargetSeats.SequenceEqual([2])) &&
            !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == unselected && a.TargetSeats.SequenceEqual([2])),
            "Only the selected real physical hand card crosses the actual distance-two target boundary.");
        s.DriverAction("drop-wanglie"); s.Play(); s.Cold();
        var dodge = s.V(2).Hand.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).ToArray(); var hp = s.V(2).Hp;
        s.Use(a => a.Kind == LegalActionKind.Slash && a.CardId == selected && a.TargetSeats.SequenceEqual([2])); s.Play();
        s.Need(s.V(2).Hp == hp - 1 && dodge.All(id => s.V(2).Hand.Any(c => c.Id == id)) &&
            s.E<MobileShiWanglieUsedEvent>().Length == 1 && s.E<MobileShiWanglieCompletedEvent>().Length == 1,
            "The issued physical-card effect survives source loss, suppresses native Dodge, and starts its ban only after this exact use finishes.");
        var actions = s.G.GetHumanLegalActions();
        s.Need(!actions.Any(a => a.Kind is LegalActionKind.Duel or LegalActionKind.Slash) &&
            actions.Any(a => a.Kind == LegalActionKind.DrawTwo) && actions.Any(a => a.Kind == LegalActionKind.Alcohol) && actions.Any(a => a.Kind == LegalActionKind.Recast),
            "After Wanglie only other-player card uses are prohibited: self DrawTwo, self Alcohol and recasting remain legal.");
        s.Cold(); s.Use(a => a.Kind == LegalActionKind.DrawTwo); s.Play(); s.Use(a => a.Kind == LegalActionKind.Alcohol); s.Play();
        s.Need(s.E<MobileShiWanglieCompletedEvent>().Length == 1, "Later self uses do not spend or repeat Wanglie."); s.Replay();
    }

    public static void PreparationFrozenXDamageOccurrenceCapAndIssuedEndingCold()
    {
        var s = Scene.Start(); s.Prepare("clear"); s.SelectWanglie(null); s.Play();
        s.Need(s.Marks == 0 && s.E<MobileShiHongyiPreparedEvent>() is [{ FrozenCount: 2, DrawFirst: false }],
            "Hongyi starts at two Yi, freezes preparation X before the choice, and immediately clears the real count.");
        s.DriverAction("damage-two", [1]); s.Play();
        s.Need(s.Marks == 1, "A two-point damage event gains one Yi, rather than one per damage point.");
        s.DriverAction("damage-two", [1]); s.Play(); s.DriverAction("damage-two", [1]); s.Play();
        s.DriverAction("damage-two", [3]); s.Play(); s.DriverAction("damage-two", [3]); s.Play();
        s.Need(s.Marks == 4, "Both ongoing actual damage flow and the four-mark cap remain live after the frozen preparation.");
        var cards = s.V(0).HandCount; s.DriverAction("drop-hongyi"); s.Play(); s.Cold(); s.End();
        s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber == 2 && e.ActorSeat == 1));
        s.Need(s.E<MobileShiHongyiEndingEvent>() is [{ FrozenCount: 2, Draw: true }] &&
            s.E<MobileShiHongyiDrawBoundEvent>().Select(e => e.Count).SequenceEqual([0, 2]) && s.V(0).HandCount == cards + 2 && s.Marks == 4,
            "The issued ending survives original skill loss and cold recovery, draws the frozen two instead of live four, and retains newly accumulated Yi."); s.Replay();

        var first = Scene.Start(); first.Prepare("draw"); first.SelectWanglie(null); first.Play(); first.DriverAction("damage-two", [1]); first.Play();
        first.Need(first.Marks == 3, "The draw-first branch retains all live marks until the ending.");
        first.End(); first.Reach(() => first.E<TurnStartedEvent>().Any(e => e.TurnNumber == 2 && e.ActorSeat == 1));
        first.Need(first.Marks == 0 && first.E<MobileShiHongyiEndingEvent>() is [{ Draw: false }],
            "Draw first means clearing all live Yi at the real ending, including damage earned later in that turn.");
    }

    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Marks => E<MobileShiHongyiMarksChangedEvent>().LastOrDefault(e => e.Source.OwnerSeat == 0)?.After ?? 0;
        public void Prepare(string branch)
        { Reach(() => P?.SkillPrompt?.SkillId == Hongyi && P.Choices.Any(c => Branch(c) == branch)); Cold(); Answer(c => Branch(c) == branch); }
        public void SelectWanglie(CardKind? card)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Wanglie && P.Choices.Any(c => Key(c) == "activate")); Answer(c => Key(c) == "activate");
            Reach(() => P?.SkillPrompt?.SkillId == Wanglie && P.Choices.Any(c => Branch(c) == "card")); Cold();
            Answer(c => card is null ? Branch(c) == "cancel" : c.Cards.Count == 1 && V(0).Hand.Any(h => h.Id == c.Cards[0] && h.Kind == card));
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void DriverAction(string activation, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(Func<LegalAction, bool> pick)
        {
            var a = G.GetHumanLegalActions().First(pick);
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Expected an actual native choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 240; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Chen Dao prompt. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded native fixture keeps the real players alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missing bounded Shi Chen Dao boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restore preserves selected entity, exact phase lease, frozen X and unpaid native choices."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "The native journal exactly reproduces physical selection, no-response payment and issued ending."); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Chen Dao command."); }
        public void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why + " " + Dump()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Owner = new { V(0).Hp, V(0).HandCount, Marks },
            Prepared = E<MobileShiHongyiPreparedEvent>(), Ending = E<MobileShiHongyiEndingEvent>(), Draws = E<MobileShiHongyiDrawBoundEvent>(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 4), new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Duel, Suit.Diamond, 6),
                new(CardKind.DrawTwo, Suit.Heart, 7), new(CardKind.Alcohol, Suit.Spade, 8), new(CardKind.IronChain, Suit.Club, 9)];
            var setup = new ShanheBattleSetup(617, 1, "shi-chen-dao-native", Hero, 4, 0, 0, 0, 0, [Driver], hand, [],
                Enumerable.Range(1, 3).Select(_ => new ShanheCombatantDefinition(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 2)] }).ToArray(), [], new string('e', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-chen-dao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],
               "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],
               "activations":[
                 {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                 {"id":"drop-wanglie","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Wanglie}}"],"sourceBind":"{{Quiet}}"}]},
                 {"id":"drop-hongyi","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Hongyi}}"],"sourceBind":"{{Quiet}}"}]}
               ]},
              {"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "真实伤害与技能变更驱动", description = "实际伤害和共享技能变化指令。" },
                [Quiet] = new { name = "安静同桌", description = "保留实际回合和原生防御。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实同桌", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势陈到实体往烈与冻结弘毅", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
