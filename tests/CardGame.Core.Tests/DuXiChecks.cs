using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DuXiChecks
{
    private const string Quxi = "ol:quxi", Bixiong = "ol:bixiong", Driver = "fixture:dx-driver", Quiet = "fixture:dx-quiet";
    private const string Watch = "fixture:dx-watch", Boundary = "fixture:dx-boundary", Rewrite = "fixture:dx-rewrite";
    private const string Health = "fixture:dx-health";
    private const string Owner = "fixture:dx-owner", Mode = "identity:classic-du-xi-fixture";

    public static void ActualUnequalPairDrawModifiersRoundTransferAndSameHolderCold()
    {
        var (g, registry) = Start("source"); Use(g, "draw-two", [1]); Play(g);
        var counts = Enumerable.Range(0, 4).Select(s => V(g, s).HandCount).ToArray();
        End(g); Reach(g, () => Optional(P(g), Quxi)); Activate(g); Reach(g, () => Branch(P(g), "pair"));
        Need(P(g)!.Choices.All(c => c.Targets is [var less, var more] && less != 0 && more != 0 && counts[less] < counts[more]) &&
            V(g, 0).IsFaceDown && E<DuXiQuxiAcceptedEvent>(g).Count() == 1,
            "The genuine limited play-ending branch turns its owner face down before exposing only unequal-hand pairs of other living players.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2, 1])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DuXiQuxi is not null).DuXiQuxi!;
        Need(receipt is { Stage: DuXiQuxiStage.ObtainChildren, LessHandSeat: 2, MoreHandSeat: 1, ObtainedCardId: not null } &&
            E<DuXiQuxiCardObtainedEvent>(g).Count() == 1 && !E<DuXiQuxiMarkerMovedEvent>(g).Any(),
            "The lesser-hand participant really acquires an opaque native hand card before either persistent mark is assigned.");
        g = Cold(g, registry); Reach(g, () => Optional(P(g), Quxi));
        Need(E<DuXiQuxiDiscardSkippedEvent>(g).Count() == 1 && E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Boundary && e.OwnerSeat == 0) == 0 &&
            DrawCount(g, 2, 1) == 1 && DrawCount(g, 3, 2) == 3 && E<DuXiQuxiMarkerMovedEvent>(g).Count() == 2,
            "The actual initial turn skips the entire Discard boundary; the issued Qian and Feng genuinely change the next holders' normal Draw to one and three.");
        Activate(g); Reach(g, () => Branch(P(g), "transfer"));
        Answer(g, c => c.Targets.SequenceEqual([0])); g = Cold(g, registry);
        Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])), "The second independent mark can move to the owner already holding the first mark.");
        Answer(g, c => c.Targets.SequenceEqual([0]));
        var skipped = g.State.TurnNumber; Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber == skipped));
        Need(DrawCount(g, skipped, 0) == 0 && Mark(g, 0, PlayerMarkerKind.QuxiFeng) == 1 && Mark(g, 0, PlayerMarkerKind.QuxiQian) == 1,
            "The real round-start transfer permits both marks on the owner, then the existing face-down whole turn still performs no Draw.");
        g = Cold(g, registry); Reach(g, () => Optional(P(g), Quxi)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
        Need(DrawCount(g, g.State.TurnNumber, 0) == 2 && E<DuXiQuxiAcceptedEvent>(g).Count() == 1 && E<DuXiQuxiCardObtainedEvent>(g).Count() == 1,
            "The next genuine action turn applies Feng +1 and Qian -1 together as net zero, without repeating the limited branch or physical acquisition after cold restoration.");
        _ = Cold(g, registry);
    }

    public static void NativeEquipmentAcquisitionAndSilverLionReplacementChildrenCold()
    {
        var (g, registry) = Start("equipment");
        Need(V(g, 1).Skills!.Any(s => s.Id == Quxi), "The fixed public selection weights place the actual AI Du Xi source in seat one.");
        Use(g, "equip-peer", [2]); Play(g); var lion = V(g, 2).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        Use(g, "hurt", [2]); Play(g); Use(g, "hurt", [2]); Play(g); Use(g, "empty"); Play(g);
        Need(V(g, 2).Equipment.Any(c => c.Id == lion) && V(g, 2).Hp == 6 && V(g, 0).HandCount == 0,
            "A real bound native equipment use puts the physical Silver Lion on the later greater-hand participant, who is actually wounded before Quxi.");
        End(g); Reach(g, () => Optional(P(g), "ol:yizheng-cao-song")); Activate(g);
        Reach(g, () => P(g)?.Choices.Any(c => c.Targets.SequenceEqual([2])) == true); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, () => Branch(P(g), "obtain"));
        var prompt = P(g)!;
        Need(prompt is { PlayerSeat: 0, TargetSeat: 2, IsPrivate: true } && prompt.Choices.Any(c => c.Cards.SequenceEqual([lion])) &&
            prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0) && g.CreateSnapshot(3).PendingDecision is null,
            "The lesser-hand human chooses an opaque foreign hand slot or visible equipment; the AI donor does not disclose hidden physical hand identities.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DuXiQuxi is not null);
        var recovery = g.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
        Need(parent.DuXiQuxi is { Stage: DuXiQuxiStage.ObtainChildren, ObtainedCardId: var paid } && paid == lion &&
            recovery.ParentFrameId == parent.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, parent.Id) &&
            recovery.Attempt is { SourceSeat: 2, TargetSeat: 2, Amount: 1, Completion.Producer: RecoveryAttemptProducer.SilverLion } &&
            V(g, 2).Hp == 6 && V(g, 0).Hand.Any(c => c.Id == lion) && E<DuXiQuxiCardObtainedEvent>(g).Count() == 1 &&
            recovery.CaoSongRecoverySupport is { WaitingForMaximumChildren: true } && E<CaoSongYizhengAmountPaidEvent>(g).Count() == 1,
            "The exact paid equipment acquisition queues its native Silver Lion replacement before healing or assigning marks; its return belongs to the still-paid Quxi frame.");
        g = Cold(g, registry); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        Need(E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 2 && e.Amount == 2) == 1 && V(g, 2).Hp == 8,
            "The real issued Yizheng support pays its native maximum-HP child once, then the actual amplified removal recovery completes before the acquisition movement observer.");
        g = Cold(g, registry); Reach(g, () => E<DuXiQuxiMarkerMovedEvent>(g).Count() == 2);
        Need(E<DuXiQuxiCardObtainedEvent>(g).Count() == 1 && E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 2) == 1 &&
            g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(2) && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.du-xi.quxi.obtain") == 1 &&
            Mark(g, 0, PlayerMarkerKind.QuxiFeng) == 1 && Mark(g, 2, PlayerMarkerKind.QuxiQian) == 1,
            "Cold card selection, replacement and paid movement children never reacquire or reheal the equipment; only the original lesser/greater pair receives the source-owned marks.");
        _ = Cold(g, registry);
    }

    public static void ActualDiscardSuitShieldEffectiveTargetsAndFaceDownStartExpiry()
    {
        var (g, registry) = Start("bixiong"); Use(g, "stock"); Play(g);
        Use(g, "empty"); Play(g);
        Need(!E<DuXiBixiongSuitRecordedEvent>(g).Any(), "A genuine skill hand discard in Play cannot fabricate Bixiong's required actual Discard phase.");
        Use(g, "stock"); Play(g); var firstTurn = g.State.TurnNumber;
        End(g); Reach(g, () => g.State.TurnNumber > firstTurn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var facts = E<DuXiBixiongSuitRecordedEvent>(g).Where(e => e.OwnerSeat == 1).ToArray();
        Need(facts.Length > 0 && facts.All(e => e.Suit == Suit.Heart && e.Source.OwnerSeat == 1 && e.ActualDiscardPhaseToken > 0 &&
                g.CardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.HandLimitDiscard)),
            "Only the actual holder's native Discard hand payments produce Bixiong suit proofs, with their exact physical movements and actual-phase token.");
        var kinds = new[] { CardKind.Slash, CardKind.Duel, CardKind.Indulgence };
        var selected = kinds.ToDictionary(kind => kind, kind => V(g, 0).Hand.First(c => c.Kind == kind).Id);
        foreach (var kind in kinds)
        {
            Need(!g.GetHumanLegalActions().Any(a => a.CardId == selected[kind] && a.TargetSeats.Contains(1)), "Same effective-suit Slash, ordinary tricks and delayed tricks cannot publish the protected player as a target.");
            var before = State(g); var rejected = g.Submit(new PlayCardCommand(0, selected[kind], [1], g.Revision, P(g)!.PromptId));
            Need(!rejected.Accepted && State(g) == before, "A forged actual use cannot bypass the same effective-suit target shield or mutate any command boundary.");
        }
        Use(g, "hurt", [0]); Play(g); var hp = V(g, 0).Hp; var otherHp = V(g, 1).Hp;
        var garden = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.PeachGarden);
        Accept(g, new PlayCardCommand(0, garden.CardId!.Value, [], g.Revision, P(g)!.PromptId)); Play(g);
        Need(V(g, 0).Hp == hp + 1 && V(g, 1).Hp == otherHp,
            "The actual global trick retains its user's self target while excluding the other protected same-suit target at execution.");
        g = Cold(g, registry); Use(g, "rewrite"); Play(g);
        Need(kinds.All(kind => g.GetHumanLegalActions().Any(a => a.CardId == selected[kind] && a.TargetSeats.Contains(1))),
            "A real acquired suit-rewrite policy changes the same printed-heart physical cards to effective spades, allowing Slash and both trick categories through the effective-suit gate.");
        Use(g, "flip", [1]); Play(g); var currentTurn = g.State.TurnNumber;
        End(g); Reach(g, () => E<DuXiBixiongExpiredEvent>(g).Any(e => e.OwnerSeat == 1 && e.ActualTurnNumber > currentTurn));
        var expiry = E<DuXiBixiongExpiredEvent>(g).Last(e => e.OwnerSeat == 1);
        Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 1 && e.TurnNumber == expiry.ActualTurnNumber));
        Need(!V(g, 1).IsFaceDown && DrawCount(g, expiry.ActualTurnNumber, 1) == 0 &&
            E<DuXiBixiongSuitRecordedEvent>(g).All(e => e.ActualTurnNumber < expiry.ActualTurnNumber),
            "Bixiong expires at its owner's actual TurnStarted even when that whole face-down turn skips preparation and Draw; the skipped turn creates no new protection.");
        _ = Cold(g, registry);
    }

    public static void TwoIssuedSourcesAndActualHolderDeathTransferOnlyOwnedMarks()
    {
        var (g, registry) = Start("multiple"); Use(g, "draw-two", [1]); Play(g);
        End(g); Reach(g, () => Optional(P(g), Quxi)); Activate(g); Reach(g, () => Branch(P(g), "pair"));
        Answer(g, c => c.Targets.SequenceEqual([2, 1]));
        Reach(g, () => E<DuXiQuxiMarkerMovedEvent>(g).Select(e => e.Source.OwnerSeat).Distinct().Count() == 2);
        var sources = E<DuXiQuxiMarkerMovedEvent>(g).Select(e => e.Source).Distinct().OrderBy(s => s.OwnerSeat).ToArray();
        Need(sources.Select(s => s.OwnerSeat).SequenceEqual([0, 1]) && Mark(g, 2, PlayerMarkerKind.QuxiFeng) == 2,
            "Two actual limited activations from distinct living sources may place their separately attributed Feng on one recipient.");
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(DrawCount(g, 3, 2) == 4, "The shared holder's actual normal Draw stacks two genuinely issued Feng as +2.");
        Use(g, "kill", [2]); Reach(g, () => Optional(P(g), Quxi)); Activate(g); Reach(g, () => Branch(P(g), "transfer"));
        var secondBefore = E<DuXiQuxiMarkerMovedEvent>(g).Where(e => e.Source == sources[1]).ToArray();
        Answer(g, c => c.Targets.SequenceEqual([3])); g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3])); Play(g);
        Need(!V(g, 2).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 2) &&
            E<DuXiQuxiMarkerMovedEvent>(g).Where(e => e.Source == sources[1]).SequenceEqual(secondBefore) &&
            E<DuXiQuxiMarkerMovedEvent>(g).Last(e => e.Source == sources[0] && e.Marker == PlayerMarkerKind.QuxiFeng).Holder == 3 &&
            E<DuXiQuxiMarkerMovedEvent>(g).Last(e => e.Source == sources[0] && e.Marker == PlayerMarkerKind.QuxiQian).Holder == 3 &&
            Mark(g, 2, PlayerMarkerKind.QuxiFeng) == 1 && Mark(g, 3, PlayerMarkerKind.QuxiFeng) == 1 && Mark(g, 3, PlayerMarkerKind.QuxiQian) == 1,
            "The exact marked recipient's real death opens transfers; moving both of source zero's marks, cold between choices, never clears or migrates source one's overlapping mark.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Mark(GameEngine g, int seat, PlayerMarkerKind kind) => V(g, seat).Markers?.FirstOrDefault(m => m.Kind == kind)?.Count ?? 0;
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static int DrawCount(GameEngine g, int turn, int seat) => g.CardMovements.Count(m => m.TurnNumber == turn && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(seat) && m.Reason == CardMoveReasons.Draw);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 300; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Du Xi actual game completed before the required boundary: " + JsonSerializer.Serialize(new {
                g.State.TurnNumber, g.State.Winner, Players = Enumerable.Range(0, 4).Select(s => new { s, V(g, s).IsAlive, V(g, s).IsFaceDown, V(g, s).HandCount, Skills = V(g, s).Skills!.Select(x => x.Id) }),
                Starts = E<ProgramBindingStartedEvent>(g).Where(e => e.SkillId == Quxi), Accepted = E<DuXiQuxiAcceptedEvent>(g), Marks = E<DuXiQuxiMarkerMovedEvent>(g),
                Tail = g.Events.TakeLast(8).Select(e => e.Payload.ToString()) }));
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Du Xi boundary: " + JsonSerializer.Serialize(p));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Du Xi boundary: " + JsonSerializer.Serialize(new { Pending = P(g), Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Facts = g.Events.TakeLast(7).Select(e => e.Payload.ToString()) }));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Du Xi command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold Du Xi preserves exact source-attributed persistent markers, private opaque card choices, actual paid children, normal Draw and suit protections."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-du-xi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:du-xi", "ol:cao-song");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
                {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"rewrite","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Rewrite}}"]}]},
                {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1,"resultBind":"drawn"},{"op":"revealBoundCards","target":"owner","sourceBind":"drawn"},{"op":"filterBoundCards","target":"owner","sourceBind":"drawn","resultBind":"gear","categories":["equipment"]},{"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"gear","condition":{"kind":"boundCardsMatchCategories","sourceBind":"gear","cardCategories":["equipment"]} }]},
                {"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-quxi-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.du-xi.quxi.obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Boundary}}","revision":1,"triggers":[{"id":"discard-start","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"start","options":[{"id":"continue"}]}]},{"id":"discard-end","window":"discardPhaseEnded","turnOwnerScope":"own","allowOwnDiscardPhaseEnded":true,"subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"end","options":[{"id":"continue"}]}]}]},
              {"id":"{{Rewrite}}","revision":1,"cardPolicies":[{"id":"effective-spades","kind":"rewriteSuit","inputSuit":"heart","outputSuit":"spade"}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-health-child","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Watch, Boundary, Rewrite, Health };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "杜袭实际规则小夹具", optionLabels = id is Watch or Boundary or Health ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "杜袭原生行为") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:dx-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:dx-peer-pick", "其他", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            b.AddGeneral(new(Owner, "杜袭真实规则本人", "ol-du-xi", "fixture:dx-owner-pick", "wei", scenario == "equipment" ? 12 : 4,
                scenario is "source" or "multiple" ? [Quxi, Bixiong, Driver, Watch, Boundary] : scenario == "equipment" ? [Driver, Watch, Boundary, Health, "ol:yizheng-cao-song"] : [Bixiong, Driver, Watch, Boundary]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:dx-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:dx-pick-" + index;
                b.AddSkill(new(pick, "固定其他顺序", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100003d - index) });
                var skills = new List<string> { Quiet, Watch }; if (scenario is "equipment" or "multiple" && index == 0) skills.Add(Quxi); if (scenario == "bixiong" && index == 0) skills.Add(Bixiong);
                if (scenario is "equipment" or "multiple" && index == 0) skills.Remove(Quiet);
                b.AddGeneral(new(id, "真实其他", "supporter", pick, "wei", 8, skills) { InitialHp = scenario == "bixiong" && index == 0 ? 1 : 8 });
            }
            b.AddCard(new("fixture:dx-lion", "白银狮子", "装备牌", "真实装备移除回复实体", CardKind.SilverLion));
            b.AddCard(new("fixture:dx-garden", "桃园结义", "锦囊牌", "真实全体回复实体", CardKind.PeachGarden));
            var cardIds = scenario == "bixiong" ? new[] { "standard:slash", "standard:duel", "standard:indulgence", "fixture:dx-garden" } : new[] { "fixture:dx-lion" };
            b.AddDeck(new("fixture:dx-deck", "固定实体非种子搜索", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(cardIds[i % cardIds.Length], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "杜袭真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:dx-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
