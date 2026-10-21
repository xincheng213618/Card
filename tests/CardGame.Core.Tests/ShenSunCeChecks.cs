using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenSunCeChecks
{
    private const string General = "mobile:shen-sun-ce", Driver = "fixture:shen-sun-ce-driver", Quiet = "fixture:shen-sun-ce-quiet";
    private const string Pause = "fixture:shen-sun-ce-paid", DrawPause = "fixture:shen-sun-ce-drawn", Mode = "identity:classic-shen-sun-ce-fixture";
    private const string GiftReason = "skill-program.mobile:fenghe.give";

    public static void NativeYingbaFuhaiAndDeathGrowth()
    {
        var (g, registry) = Start();
        var lost = V(g, 0).MaxHp - V(g, 0).Hp;
        Need(lost > 0 && V(g, 0).HandLimit == lost, "Fenghe's real hand-limit query replaces the HP base with lost HP: " + JsonSerializer.Serialize(new { V(g, 0).Hp, V(g, 0).MaxHp, V(g, 0).HandLimit }));
        Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "An unmarked opposite seat is outside the real range-one Slash.");
        var before = V(g, 2).MaxHp; Skill(g, "mobile:yingba", "subdue", [2]); Play(g);
        Need(V(g, 2).MaxHp == before - 1 && V(g, 0).HandLimit == lost - 1 && Mark(g, 2) == 1, "Yingba pays both actual maximum-HP losses and publishes one cumulative Pingding.");
        Need(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])), "The public marked target receives card distance exemption without changing physical seat distance.");
        var twice = g.Submit(new UseProgramSkillCommand(0, "mobile:yingba", "subdue", [], [1], g.Revision, P(g)!.PromptId));
        Need(!twice.Accepted, "Yingba remains once per actual Play phase.");
        g = Cold(g, registry);
        var multi = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain && a.TargetSeats.SequenceEqual([1, 2]));
        Use(g, multi); Play(g);
        Need(E<ShenSunCeDrawReturnedEvent>(g).Count(e => e.Operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw) == 1,
            "A multi-target native Use produces one finalized-target Fuhai draw for the whole action.");
        for (var i = 0; i < 3; i++)
        {
            Need(g.CreateSnapshot(2).Players[2].Hand.Any(c => c.Kind == CardKind.Slash), "The real Duel target actually retains a physical Slash it could otherwise play.");
            var hp = V(g, 2).Hp; var responses = E<CardActionAcceptedEvent>(g).Count(e => e.Action.Type == CardActionType.Response && e.Action.ActorSeat == 2);
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([2]));
            Use(g, action); Play(g);
            Need(V(g, 2).Hp == hp - 1 && E<CardActionAcceptedEvent>(g).Count(e => e.Action.Type == CardActionType.Response && e.Action.ActorSeat == 2) == responses,
                "A Pingding target with actual Slash cards cannot respond to the native Duel.");
        }
        var draws = E<ShenSunCeDrawReturnedEvent>(g).Where(e => e.Operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDraw).ToArray();
        Need(draws.Length == 2 && draws.Sum(e => e.Actual) == 2, "Three distinct native Uses issue exactly two actual Fuhai target draws in the turn.");
        var max = V(g, 0).MaxHp; Skill(g, Driver, "kill", [2]);
        Reach(g, p => p.SkillPrompt?.SkillId == DrawPause);
        Need(V(g, 0).MaxHp == max + 1 && E<ShenSunCeDrawReturnedEvent>(g).Single(e => e.Operation == SkillProgramEffectOp.ResolveShenSunCeFuhaiDeath) is { Requested: 1, Actual: 1 },
            "A marked actual death independently grows maximum HP and draws its frozen mark count after the two-card target cap.");
        g = Cold(g, registry); Continue(g); Play(g); Need(!V(g, 2).IsAlive, "The original native death completes after its paid Fuhai draw child returns.");
        _ = Cold(g, registry);
    }

    public static void NativeFenghePaidGiftColdRestore()
    {
        var (g, registry) = Start(); var max = V(g, 0).MaxHp; var hp = V(g, 0).Hp;
        Skill(g, Driver, "duel-owner", [1, 0]);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); Answer(g, c => c.Cards.Count == 0);
        Reach(g, p => p.SkillPrompt?.SkillId == "mobile:fenghe");
        var protectedHp = Math.Min(hp, max - 1);
        Need(V(g, 0).Hp == protectedHp && V(g, 0).MaxHp == max - 1 && E<ProgramDamagePreventedEvent>(g).Count() == 1,
            "Other-source damage is prevented once and one actual maximum HP is paid before the hand gift: " + JsonSerializer.Serialize(new { BeforeHp = hp, BeforeMax = max, AfterHp = V(g, 0).Hp, AfterMax = V(g, 0).MaxHp, Prevented = E<ProgramDamagePreventedEvent>(g).Count() }));
        Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null), "Only the owner can see the pending physical hand gift.");
        var choice = P(g)!.Choices.First(c => c.Targets.SequenceEqual([3])); var card = choice.Cards.Single();
        var wrong = g.Submit(new AnswerPromptCommand(1, P(g)!.PromptId, choice.Id, g.Revision)); Need(!wrong.Accepted, "Another role cannot pay the private gift.");
        g = Cold(g, registry); Answer(g, c => c.Id == choice.Id);
        Reach(g, p => p.SkillPrompt?.SkillId == Pause);
        Need(g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Hand(3) && Mark(g, 1) == 0,
            "The exact chosen physical hand entity moves once before its real movement child; the later source mark is still unpaid.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 0).Hp == protectedHp && V(g, 0).MaxHp == max - 1 && Mark(g, 1) == 1 && g.CardMovements.Count(m => m.CardId == card && m.Reason.Value == GiftReason) == 1,
            "Cold restore resumes the original prevented damage frame and gift receipt, then issues its source mark without repaying.");
        Skill(g, Driver, "recover", []); Play(g); var paid = E<ProgramDamagePreventedEvent>(g).Count(); max = V(g, 0).MaxHp;
        Skill(g, Driver, "self-damage", []); Play(g);
        Skill(g, Driver, "source-less", []); Play(g);
        Skill(g, Driver, "lose-hp", []); Play(g);
        Need(V(g, 0).MaxHp == max && E<ProgramDamagePreventedEvent>(g).Count() == paid, "Self-source damage, source-less damage and HP loss cannot trigger Fenghe.");
        _ = Cold(g, registry);
    }

    private static int Mark(GameEngine g, int seat) => V(g, seat).Markers?.Where(m => m.Kind == PlayerMarkerKind.Pingding).Sum(m => m.Count) ?? 0;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Skill(GameEngine g, string skill, string activation, int[] targets) => Accept(g, new UseProgramSkillCommand(0, skill, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var step = 0; step < 180; step++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                { Answer(g, c => c.Cards.Count == 0); continue; }
                if (p.SkillPrompt?.SkillId is Pause or DrawPause) { Continue(g); continue; }
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shen Sun Ce missed native boundary: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }) }));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "A native Shen Sun Ce fixture command was rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "All four private views, owning native pending frames, payments and accepted command prefix cold-restore exactly."); return restored; }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 15 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, General, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-sun-ce", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), General);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"duel-owner","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":20}]},
                {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Pause}}","revision":1,"triggers":[{"id":"gift-paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{GiftReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{DrawPause}}","revision":1,"triggers":[{"id":"draw-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile:fuhai.death-draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"drawn","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Pause, DrawPause };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Small native Shen Sun Ce fixture.", optionLabels = id is Pause or DrawPause ? new Dictionary<string, string> { ["continue"] = "继续真实子流程" } : [] }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Native Shen Sun Ce fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new($"fixture:shen-sun-ce-peer-{s}", "Quiet real participant", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:shen-sun-ce-deck", "Real Slashes, Duels and Iron Chains", 20, 2, []) { PhysicalCards = Enumerable.Range(0, 128).Select(i =>
                new ContentDeckPhysicalCard(i % 3 == 0 ? "standard:slash" : i % 3 == 1 ? "standard:duel" : "standard:iron_chain", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "Native Shen Sun Ce", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:shen-sun-ce-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [General, "fixture:shen-sun-ce-peer-1", "fixture:shen-sun-ce-peer-2", "fixture:shen-sun-ce-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == General ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds!, Driver, Pause, DrawPause] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
