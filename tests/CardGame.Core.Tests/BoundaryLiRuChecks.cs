using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLiRuChecks
{
    private const string Mieji = "boundary:mieji-current", Fencheng = "boundary:fencheng-current", Juece = "boundary:juece-current";
    private const string Driver = "fixture:lr-driver", Top = "fixture:lr-top", Cost = "fixture:lr-cost", Hp = "fixture:lr-hp", Gain = "fixture:lr-gain", Damage = "fixture:lr-damage";
    private const string Dying = "fixture:lr-dying";
    private const string Mode = "identity:classic-li-ru-fixture";
    private const string MiejiReason = "skill-program." + Mieji + ".ChooseCategoryOrSequentialDiscard";

    public static void FenchengPublishesUnpaidBatchBoundsCold()
    {
        var (g, registry) = Create(); Play(g);
        Accept(g, new UseProgramSkillCommand(0, Fencheng, "chosen-start-fire-walk", [], [1], g.Revision, P(g)!.PromptId));
        Reach(g, IsSequential);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "discard");
        var initial = P(g)!;
        Require(initial.PlayerSeat == 1 && initial.MinimumCardCount == 1 && initial.RequiredCardCount >= 2 &&
            initial.Choices.Where(c => c.Cards.Count == 1).All(c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick"),
            "Fencheng publishes its real lower bound and complete unpaid batch to the current payer.");
        var ids = initial.Choices.Where(c => c.Cards.Count == 1).Take(2).Select(c => c.Cards[0]).ToArray();
        var movements = g.CardMovements.Count;
        Answer(g, c => c.Cards.SequenceEqual([ids[0]]));
        Require(g.CardMovements.Count == movements && P(g)!.MinimumCardCount == 0 &&
            P(g)!.RequiredCardCount == initial.RequiredCardCount - 1,
            "The first selected material reduces both live bounds without paying early.");
        g = RestoreAfterCold(g, registry);
        Answer(g, c => c.Cards.SequenceEqual([ids[1]]));
        Require(g.CardMovements.Count == movements, "A larger legal batch remains unpaid until explicit finish.");
        Answer(g, c => c.Parameters.GetValueOrDefault("owned-selection-role") == "finish");
        Reach(g, p => IsSequential(p) && p.PlayerSeat != 1);
        var paid = F<ProgramSequentialDiscardPaidEvent>(g).Single();
        Require(paid.ActualCount == 2 && ids.All(id => g.CardMovements.Skip(movements).Count(m =>
            m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 1),
            "The original native finish discards exactly the two selected physical entities once.");
        Cold(g, registry);
    }

    public static void SequentialAnyDiscardKeepsTopPaymentAndChildReturns()
    {
        var (g, r) = Create(observers: true, replenish: true); Play(g); GiveTrick(g, 0); GiveTrick(g, 1);
        Use(g, "equip", [1]); Play(g); Use(g, "hurt", [1]); Play(g);
        var topCard = V(g, 0).Hand.First(c => c.Kind == CardKind.Dismantlement && c.Suit == Suit.Heart).Id;
        var firstCard = V(g, 1).Hand.First(c => c.Kind == CardKind.Dismantlement).Id;
        var lion = V(g, 1).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        StartMieji(g, topCard, 1); Reach(g, p => p.SkillPrompt?.SkillId == Top);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Mieji);
        var rootId = root.Id;
        var topReceipt = root.SequentialDiscardTopPayment!;
        var topMove = g.CardMovements.Single(m => m.Sequence > topReceipt.MovementSequenceBefore && m.CardId == topCard &&
            m.Reason.Value == "skill-program." + Mieji + ".MoveBoundCards");
        Require(root.SequentialDiscard is null && root.InstructionIndex == 2 && topReceipt.CardId == topCard &&
            topMove.From == CardLocation.Hand(0) && topMove.To == CardLocation.DrawPile &&
            !F<CardUseDeclaredEvent>(g).Any(e => e.CardId == topCard),
            "The actual red trick entity is placed on top once without being used; its original movement child precedes any forced discard.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, IsSequential);
        Private(g, 1); Reject(g); g = RestoreAfterCold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "sequential");
        Require(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([firstCard])),
            "The explicit two-card branch accepts a trick as its first real payment and does not inherit classic's non-trick complement filter.");
        Answer(g, c => c.Cards.SequenceEqual([firstCard])); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        root = SequentialRoot(g); var firstPayment = root.SequentialDiscard!.Payment!;
        Require(root.Id == rootId &&
            root.SequentialDiscard is { Remaining: 1, Stage: ProgramSequentialDiscardStage.AwaitingMovement } &&
            root.PendingMovementContinuation is { SubjectSeat: 1, BeforeCount: 0, CoverageResultBind: null } &&
            Moves(g, MiejiReason) is [var paid] && paid.CardId == firstCard &&
            V(g, 1).Equipment.Any(c => c.Id == lion) && firstPayment.ActualCount == 1,
            "Only the first real entity has paid while its own movement observer is pending; the second equipment cost remains untouched.");
        g = RestoreAfterCold(g, r); FrozenDiscardCollections(SequentialRoot(g).SequentialDiscard!);
        Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(SequentialRoot(g).SequentialDiscard!.Remaining == 1 && Moves(g, MiejiReason).Length == 1,
            "The first payment's real reward gain child completes before the second private discard is published.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, IsSequential);
        Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        root = SequentialRoot(g);
        Require(root.SequentialDiscard is { Remaining: 0, Stage: ProgramSequentialDiscardStage.AwaitingMovement } &&
            Moves(g, MiejiReason).Length == 2 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h =>
                h.ResumeFrameId == root.Id && h.Change.ParentFrameId == root.Id && h.Change.TargetSeat == 1 &&
                h.Continuation == PostEventContinuation.AwaitedProgramMovement),
            "The second Silver Lion entity pays exactly once; its actual recovery child retains the same sequential owner before completion.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var secondWindow = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
        Require(secondWindow.Batch.Movements is [var second] && second.CardId == lion && second.From == CardLocation.Equipment(1) &&
            second.To == CardLocation.DiscardPile && secondWindow.Batch.AwaitingProgramFrameId == root.Id,
            "The second movement batch is its exact already-paid equipment entity, not a rebuilt hand selection.");
        g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(F<ProgramSequentialDiscardFinishedEvent>(g).Single().RemainingUnpaid == 0 && Moves(g, MiejiReason).Length == 2 &&
            !g.GetHumanLegalActions().Any(a => a.SkillId == Mieji) && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SequentialDiscard is not null),
            "Both full child chains return once and retain the original per-play-phase use limit."); Cold(g, r);
    }

    private static (GameEngine, ContentRegistry) Create(bool observers = false, bool replenish = false, bool removeSource = false, bool fatalCost = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(observers, replenish, removeSource, fatalCost));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:lr-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void GiveTrick(GameEngine g, int target)
    {
        Use(g, "search"); Reach(g, p => Action(p, "declared-deck-criterion")); Answer(g, c => c.Parameters.GetValueOrDefault("criterion") == "red");
        Reach(g, p => Action(p, "declared-deck-recipient")); Answer(g, c => c.Targets.SequenceEqual([target])); Play(g);
    }
    private static void StartMieji(GameEngine g, int card, int target) => Accept(g,
        new UseProgramSkillCommand(0, Mieji, "topdeck-trick", [card], [target], g.Revision, P(g)!.PromptId));
    private static ProgramSkillFrame SequentialRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SequentialDiscard is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static bool IsSequential(PendingDecision p) => Action(p, "sequential-discard");
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 180; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Li Ru command boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack,
            Events = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is not null && Action(p, "select-owned-cards")) Answer(g, c => c.Cards.Count == 1);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "All four prepared views, immutable private selections, typed parents, actual ledgers and command histories cold-restore identically."); return restored;
    }
    private static void Cold(GameEngine g, ContentRegistry r) => _ = RestoreAfterCold(g, r);
    private static void FrozenDiscardCollections(ProgramSequentialDiscardDraft actual)
    {
        void Frozen<T>(IReadOnlyList<T> values)
        {
            Require(values is System.Collections.Generic.IList<T> { IsReadOnly: true }, "The recovered receipt exposes a frozen collection, including an empty selection.");
            var rejected = false;
            try { ((System.Collections.Generic.IList<T>)values).Add(default!); } catch (NotSupportedException) { rejected = true; }
            Require(rejected, "An observer cannot append an entry to the recovered receipt.");
            if (values.Count == 0) return;
            rejected = false;
            try { ((System.Collections.Generic.IList<T>)values)[0] = default!; } catch (NotSupportedException) { rejected = true; }
            Require(rejected, "An observer cannot rewrite an existing recovered receipt entry.");
        }
        void All(ProgramSequentialDiscardDraft value)
        {
            Frozen(value.Order); Frozen(value.SelectedCardIds); Frozen(value.SelectedLocations);
            Frozen(value.Payment!.CardIds); Frozen(value.Payment.SourceLocations);
        }
        All(actual);
        var restored = JsonSerializer.Deserialize<ProgramSequentialDiscardDraft>(JsonSerializer.Serialize(actual))!;
        All(restored);
        var paid = actual.Payment!; var ids = paid.CardIds.ToArray(); var places = paid.SourceLocations.ToArray();
        var order = actual.Order.ToArray(); var selected = actual.SelectedCardIds.ToArray(); var selectedPlaces = actual.SelectedLocations.ToArray();
        var constructedPayment = new ProgramSequentialDiscardPayment(paid.ChooserSeat, paid.Cursor, ids, places,
            paid.SequenceBefore, paid.SequenceAfter, paid.ActualCount);
        var constructed = new ProgramSequentialDiscardDraft(actual.Kind, actual.InstructionIndex, actual.Source, actual.GameplayHash,
            actual.ActualTurnNumber, actual.ActualTurnOwnerSeat, actual.StartSeat, order, actual.Cursor, actual.ChooserSeat,
            actual.PreviousCount, actual.Remaining, actual.PrimaryBranch, actual.Stage, selected, selectedPlaces, constructedPayment);
        var assigned = actual with { Order = order, SelectedCardIds = selected, SelectedLocations = selectedPlaces,
            Payment = paid with { CardIds = ids, SourceLocations = places } };
        ids[0] = int.MinValue; places[0] = CardLocation.DrawPile; order[0] = int.MinValue;
        if (selected.Length > 0) selected[0] = int.MinValue;
        if (selectedPlaces.Length > 0) selectedPlaces[0] = CardLocation.DrawPile;
        foreach (var value in new[] { constructed, assigned })
        {
            All(value);
            Require(value.Order.SequenceEqual(actual.Order) && value.SelectedCardIds.SequenceEqual(actual.SelectedCardIds) &&
                value.SelectedLocations.SequenceEqual(actual.SelectedLocations) && value.Payment!.CardIds.SequenceEqual(paid.CardIds) &&
                value.Payment.SourceLocations.SequenceEqual(paid.SourceLocations),
                "Constructor and init/with setters clone caller arrays without sharing private paid state.");
        }
    }
    private static void Private(GameEngine g, int chooser)
    {
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(seat).PendingDecision is null, "A foreign observer sees no private discard choices or selected IDs.");
        var p = g.CreateSnapshot(chooser).PendingDecision!;
        Require(p.Choices is System.Collections.IList { IsReadOnly: true } && p.ValidCardIds is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true }),
            "Prepared outer and nested private choices are immutable.");
    }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished discard choice cannot mutate selection or pay any cost."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool observers, bool replenish, bool removeSource, bool fatalCost) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-li-ru", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:lr-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"clear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand","equipment"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true}]},
                    {"id":"draw","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                    {"id":"search","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"declareDeckCriterionAndGiveMatchingCard","target":"owner"}]}]},
                  {"id":"fixture:lr-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:lr-top","revision":1,"triggers":[{"id":"top","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:mieji-current.MoveBoundCards"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-cost","revision":1,"triggers":[{"id":"cost","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["skill-program.boundary:mieji-current.ChooseCategoryOrSequentialDiscard","skill-program.boundary:fencheng-current.EscalatingDiscardOrDamageFromSelected"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:lr-cost.Draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:lr-damage","revision":1,"triggers":[{"id":"damage","window":"afterDamageApplied","subject":"owner","optional":false,"damageOccurrence":"perDamage","effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
                  {"id":"fixture:lr-dying","revision":1,"triggers":[{"id":"dying","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (replenish) ((JsonArray)rules["skills"]![3]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""));
            if (fatalCost) ((JsonArray)rules["skills"]![3]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":8}"""));
            if (removeSource) ((JsonArray)rules["skills"]![2]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:lr-noop"]}"""));
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "小固定夹具" }, ["fixture:lr-quiet"] = new { name = "安静回合", description = "真实跳过出牌" },
                  [Top] = Pause("置顶孩子"), [Cost] = Pause("弃牌孩子"), [Hp] = Pause("真实回复"), [Gain] = Pause("真实得牌"), [Damage] = Pause("真实火伤"), [Dying] = Pause("真实濒死入口") } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:lr-quiet", Top, Cost, Hp, Gain, Damage, Dying }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:lr-noop", "已付来源资格抑制", "置顶付款后取消未执行能力") { SuppressionRule = new(4), Tags = SkillTag.Locked });
            b.AddSkill(new("fixture:lr-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var ownerSkills = new List<string> { Fencheng, Juece, Driver }; if (observers) ownerSkills.AddRange([Top, Cost, Hp, Gain, Damage, Dying]);
            b.AddGeneral(new("fixture:lr-owner", "界李儒机制", "supporter", Mieji, "qun", 3, ownerSkills, Gender: GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:lr-target-{i}", "固定目标", "supporter", "fixture:lr-selection", "qun", 6,
                observers ? ["fixture:lr-quiet", Cost, Hp, Gain, Damage, Dying] : ["fixture:lr-quiet"], Gender: GeneralGender.Male));
            b.AddDeck(new("fixture:lr-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i =>
                new ContentDeckPhysicalCard(i % 2 == 0 ? "classic:silver-lion" : "standard:dismantlement", i % 2 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界李儒", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lr-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:lr-owner", "fixture:lr-target-1", "fixture:lr-target-2", "fixture:lr-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
