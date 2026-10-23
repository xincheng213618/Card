using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HappyShenCaoCaoChecks
{
    private const string Hero = "happy:shen-cao-cao", Guixin = "happy:guixin";
    private const string Driver = "fixture:happy-caocao-driver", Watch = "fixture:happy-caocao-watch", Quiet = "fixture:happy-caocao-quiet";
    private const string Mode = "identity:classic-happy-caocao-fixture", Reason = "skill-program.happy:guixin.obtain";

    public static void RandomOnePerCharacterActualThresholdAndFaceDownCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Hero, g.Revision, P(g)!.PromptId)); Play(g);
        Use(g, "empty-target", [5]); Play(g);
        Need(V(g, 5).HandCount == 0 && Enumerable.Range(1, 4).All(s => V(g, s).HandCount == 4) &&
            g.CardMovements.Count(m => m.From == CardLocation.Hand(5) && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == "program.participants.discard-hand") == 4,
            "The six-player native fixture really discards one other character's hand, leaving exactly four obtainable characters.");
        var initialHand = V(g).HandCount;
        var initialHp = V(g).Hp;
        Use(g, "one-damage"); Offer(g); Activate(g); Paid(g);
        var root = Root(g)!; var firstId = root.Id; var receipt = root.HappyGuixin!;
        Need(receipt is { Stage: HappyGuixinStage.MovementChildren, Cursor: 0, Payments.Count: 1 } &&
            receipt.OtherSeats.SequenceEqual([1, 2, 3, 4, 5]) && receipt.Payments[0].Candidates.Count == 4 &&
            receipt.Payments[0].Movement is { TargetSeat: 1, Movements.Count: 1 } payment &&
            payment.Movements[0].From == CardLocation.Hand(1) && payment.Movements[0].To == CardLocation.Hand(0) &&
            V(g).HandCount == initialHand + 1 && !V(g).IsFaceDown,
            "Happy Guixin randomly obtains one actual hand entity from the whole legal pool and suspends its original paid movement before the next character.");
        g = Cold(g, registry); Continue(g);
        for (var n = 1; n < 4; n++) { Paid(g); Continue(g); }
        Play(g);
        Need(V(g).HandCount == initialHand + 4 && !V(g).IsFaceDown && V(g).Hp == initialHp - 1 &&
            E<HappyGuixinCompletedEvent>(g) is [{ PaidCharacters: 4, ActualObtained: 4, TurnoverIssued: false, Cancelled: false }] &&
            E<HappyGuixinSeatSkippedEvent>(g) is [{ TargetSeat: 5 }] && E<HappyGuixinTurnoverIssuedEvent>(g).Length == 0 &&
            E<HappyGuixinMovementPaidEvent>(g).Count(e => e.FrameId == firstId) == 4,
            "Five other seats with only four actual gains do not satisfy greater-than-four: the empty character is skipped and exactly four cards settle once after cold restore.");
        Use(g, "refill-target", [5]); Play(g);
        Need(V(g, 5).HandCount == 4, "The previously empty target receives four real deck cards before the next actual damage event.");
        Use(g, "two-damage"); Offer(g); Activate(g);
        for (var n = 0; n < 5; n++) { Paid(g); Continue(g); }
        Offer(g);
        var completed = E<HappyGuixinCompletedEvent>(g);
        var secondId = completed.Last().FrameId;
        Need(completed.Length == 2 && completed.Last() is { PaidCharacters: 5, ActualObtained: 5, TurnoverIssued: true, Cancelled: false } &&
            V(g).IsFaceDown && V(g).HandCount == initialHand + 9 &&
            E<HappyGuixinTurnoverIssuedEvent>(g) is [{ ActualObtained: 5, Before: false, After: true }],
            "The first point of one native two-damage event obtains five cards and flips a face-up Cao Cao only after the entire sweep returns.");
        Activate(g);
        for (var n = 0; n < 5; n++) { Paid(g); Continue(g); }
        Play(g);
        var started = E<HappyGuixinStartedEvent>(g);
        Need(E<HappyGuixinCompletedEvent>(g).Last() is { PaidCharacters: 5, ActualObtained: 5, TurnoverIssued: false, Cancelled: false } &&
            V(g).IsFaceDown && V(g).HandCount == initialHand + 14 && V(g).Hp == initialHp - 3 &&
            started.Select(e => e.OccurrenceIndex).SequenceEqual([0, 0, 1]) &&
            started[1].DamageFrameId == started[2].DamageFrameId && started[1].FrameId == secondId &&
            started[1].DamageAmount == 2 && started[2].DamageAmount == 2 &&
            E<HappyGuixinTurnoverIssuedEvent>(g).Length == 1,
            "The second real damage point has an independent sweep and optional activation; five more gains do not turn an already face-down Cao Cao back up.");
        Need(g.CardMovements.Count(m => m.Reason.Value == Reason) == 14 &&
            E<HappyGuixinMovementPaidEvent>(g).Length == 14 &&
            g.ResolutionStack.All(f => f is not ProgramSkillFrame { HappyGuixin: not null }) &&
            Enumerable.Range(1, 5).All(s => g.CardMovements.Count(m => m.Reason.Value == Reason && m.From.OwnerSeat == s) == (s == 5 ? 2 : 3)),
            "Every eligible other character contributes exactly one physical card per sweep, with no re-randomization or duplicated paid gain.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)),
            "The accepted command journal replays the private random choices, actual-gain threshold, damage-point ownership, child return and sole conditional turnover.");
    }

    private static GameOptions Options() => new()
    {
        Seed = 7, PlayerCount = 6, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
        UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
    };
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 6).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HappyGuixin is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected real Happy Guixin command.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "Missing Happy Guixin published choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Use(GameEngine g, string activation, int[]? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Offer(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Guixin && p.Choices.Any(c => Action(c) == "activate"));
    private static void Activate(GameEngine g) => Answer(g, c => Action(c) == "activate");
    private static void Paid(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Watch);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 180; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat == 5 && p.SkillPrompt?.SkillId == Driver && p.Choices.Count > 0 &&
                    p.Choices.All(c => Action(c) == "private-reserve-choice" && c.Parameters.GetValueOrDefault("reserve-mode") == "discard-hand"))
                {
                    // This native discard commits automatically when the fourth published hand choice is selected.
                    Answer(g, c => c.Cards.Count == 1); continue;
                }
                throw new InvalidOperationException("Unexpected Happy Guixin boundary: " + JsonSerializer.Serialize(new
                { Prompt = p, Receipt = Root(g)?.HappyGuixin, Completed = E<HappyGuixinCompletedEvent>(g), Hp = V(g).Hp, Hand = V(g).HandCount }));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Happy Guixin missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold restore preserves the original damage point, private random pool and payment, native gain child and all six player views.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 6).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-caocao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"empty-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":64}]},
                {"id":"refill-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":4}]},
                {"id":"one-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"two-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Reason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实归心驱动", description = "真实清空、补牌与伤害。" },
                [Watch] = new { name = "真实获得子窗", description = "暂停已支付的原生归心获得。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "安静角色", description = "跳过其他角色出牌阶段。" }
            };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "Native Happy Guixin fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 6; seat++) b.AddGeneral(new(Peer(seat), "真实目标", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:happy-caocao-deck", "固定小型实体牌堆", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:slash", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "欢乐归心实际获得及条件翻面", 6, 6,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 3, [nameof(Role.Renegade)] = 1 },
                "fixture:happy-caocao-deck", GeneralCandidateCount: 6, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3), Peer(4), Peer(5)]));
        }
    }
    private static string Peer(int seat) => "fixture:happy-caocao-peer-" + seat;
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [..d.AdditionalSkillIds!, Driver, Watch] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
