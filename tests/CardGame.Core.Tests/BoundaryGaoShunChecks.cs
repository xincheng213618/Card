using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryGaoShunChecks
{
    private const string Contest = "boundary:xianzhen-current";
    private const string Identity = "boundary:jinjiu-current";
    private const string Driver = "fixture:gs-driver";
    private const string Committed = "fixture:gs-committed";
    private const string Finalized = "fixture:gs-finalized";
    private const string Hp = "fixture:gs-hp";
    private const string Wine = "fixture:gs-wine";
    private const string Mode = "identity:classic-current-gao-shun";

    public static void OriginalTargetAdditionQuotaArmorAndCold()
    {
        var (g, registry) = Create();
        Use(g, Driver, "draw"); ReachPlay(g);
        Use(g, Driver, "boost"); ReachPlay(g);
        Use(g, Driver, "equip", [2]); ReachPlay(g);
        Require(g.CreateSnapshot(2).Players[2].Equipment.Any(c => c.Kind == CardKind.SilverLion), "A real random-deck equipment use installs the armor on the distant original target.");
        var high = Hand(g, 0).First(c => c.Kind == CardKind.Alcohol).Id;
        ContestWith(g, registry, high, 2); Cold(g, registry);
        Require(Facts<PindianResultDeterminedEvent>(g).Last().Result is { SourceRank: 13, OpponentRank: 1, SourceWon: true },
            "The printed rank-one physical Alcohol is actually K in the real Pindian, frozen before it leaves hand.");
        Require(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Contest), "The current exact Play phase cannot start a second contest.");
        for (var i = 0; i < 2; i++)
        {
            var id = Hand(g, 0).First(c => c.Kind == CardKind.Alcohol).Id;
            Play(g, id, [2], CardKind.Slash); Reach(g, p => p.SkillPrompt?.SkillId == Committed);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == id);
            Require(use.Action is { EffectiveKind: CardKind.Slash, EffectiveRank: 13 } action && action.PhysicalCards.Single().CardKind == CardKind.Alcohol &&
                use.OriginalTargetAddition?.Grants.Any(grant => grant.TargetSeat == 2 && grant.Source.SkillId == Contest) == true && use.UnlimitedUse &&
                !Facts<CardUseDebitRecordedEvent>(g).Any(debit => debit.Debit.CardActionId == action.ActionId),
                "A real accepted distant original-target Slash freezes its rank and grant, and never spends an ordinary Slash debit.");
            Frozen(use.OriginalTargetAddition!.Grants); Prepared(g); Cold(g, registry); Continue(g); ReachPlay(g);
        }
        var counted = Hand(g, 0).First(c => c.Kind == CardKind.Alcohol).Id;
        Play(g, counted, [1], CardKind.Slash);
        Reach(g, p => Action(p, "original-target-addition"));
        var offer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OriginalTargetAdditionDraft is not null);
        var owning = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == offer.OriginalTargetAdditionDraft!.CardUseFrameId);
        Require(P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.SequenceEqual([2])) &&
            owning.Action!.TargetSeats.SequenceEqual([1]) && Facts<CardUseDebitRecordedEvent>(g).Count(d => d.Debit.ActorSeat == 0) == 1,
            "The true finalized action still has its original ordinary target, and only the exact won opponent is offered for +1.");
        Prepared(g); Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => p.SkillPrompt?.SkillId == Finalized);
        owning = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == owning.Id);
        Require(owning.TargetSeats.SequenceEqual([1, 2]) && owning.Action!.EffectiveDesignatedTargetSeats.SequenceEqual([1, 2]) &&
            owning.OriginalTargetAddition is { Added: true } && Facts<OriginalTargetAdditionResolvedEvent>(g).Last() is { TargetSeat: 2, Added: true },
            "One true selection appends the same original target to the owning action without a second Slash payment or quota debit.");
        Cold(g, registry); Continue(g); ReachPlay(g);
        Require(g.CardMovements.Count(m => m.CardId == counted && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            Facts<DamageAppliedEvent>(g).Any(d => d.TargetSeat == 2 && d.Amount == 2) && !g.CreateCardZoneDiagnostics().Any(z => z.Location == CardLocation.Processing),
            "Both real Slash target cursors finish under one original physical payment and no orphan Processing material.");
        Cold(g, registry);
        var fourth = Hand(g, 0).First(c => c.Kind == CardKind.Alcohol).Id;
        Require(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == fourth && a.TargetSeats.Contains(2)) &&
            !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == fourth && a.TargetSeats.Contains(1)),
            "After one ordinary debit, original-opponent Slashes remain available while other targets respect the ordinary quota.");
        var trick = Hand(g, 0).First(c => c.Kind == CardKind.DrawTwo).Id;
        var targetHand = g.State.Players[2].HandCount;
        var ownerHand = g.State.Players[0].HandCount;
        Play(g, trick, [], CardKind.DrawTwo); Reach(g, p => Action(p, "original-target-addition"));
        Require(P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.SequenceEqual([2])), "A true ordinary trick has the same exact original-opponent +1 contract.");
        Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2])); ReachPlay(g);
        Require(g.State.Players[2].HandCount == targetHand + 2 && g.State.Players[0].HandCount == ownerHand + 1 &&
            g.CardMovements.Count(m => m.CardId == trick && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
            "The implicit self target and the extra original target each receive their real two-card effect under one original trick payment.");
        Cold(g, registry);

        var (suppressed, suppressedRegistry) = Create(); Use(suppressed, Driver, "draw"); ReachPlay(suppressed);
        Use(suppressed, Driver, "boost"); ReachPlay(suppressed); Use(suppressed, Driver, "equip", [2]); ReachPlay(suppressed);
        ContestWith(suppressed, suppressedRegistry, Hand(suppressed, 0).First(c => c.Kind == CardKind.Alcohol).Id, 2);
        var retained = Hand(suppressed, 0).First(c => c.Kind == CardKind.Alcohol).Id;
        Play(suppressed, retained, [2], CardKind.Slash); Reach(suppressed, p => p.SkillPrompt?.SkillId == Committed);
        var retainedUse = suppressed.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == retained);
        Cold(suppressed, suppressedRegistry); Answer(suppressed, c => c.Parameters.GetValueOrDefault("option-id") == "suppress"); ReachPlay(suppressed);
        Require(suppressed.State.Players[0].Hp == 1 && Facts<DamageAppliedEvent>(suppressed).Any(d => d.TargetSeat == 2 && d.Amount == 2) &&
            !Facts<CardUseDebitRecordedEvent>(suppressed).Any(d => d.Debit.CardActionId == retainedUse.Action!.ActionId) &&
            suppressed.CardMovements.Count(m => m.CardId == retained && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
            "Real post-acceptance HP suppression cannot revoke the issued original use's frozen armor bypass or cause its physical material to be paid again.");
        Cold(suppressed, suppressedRegistry);
    }

    public static void FailedContestOnlyProhibitsOriginalAndIdentityHandExemption()
    {
        var (g, registry) = Create(); Use(g, Driver, "draw"); ReachPlay(g);
        var low = Hand(g, 0).First(c => c.Kind != CardKind.Alcohol).Id;
        ContestWith(g, registry, low, 2); Cold(g, registry);
        Require(Facts<PindianResultDeterminedEvent>(g).Last().Result is { SourceRank: 1, OpponentRank: 1, SourceWon: false }, "A real tie is a not-won result.");
        var alcohol = Hand(g, 0).First(c => c.Kind == CardKind.Alcohol).Id;
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == alcohol && a.TargetSeats.Contains(2)) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == alcohol && a.TargetSeats.Contains(1)),
            "A failed contest prohibits only Slashes to that exact original opponent; another legal target remains usable.");
        var exemption = Facts<TurnHandLimitCardKindExemptionGrantedEvent>(g).Single().Policy;
        Frozen(exemption.CardKinds); Prepared(g); Cold(g, registry);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0);
        Require(P(g)!.ValidCardIds.All(id => Hand(g, 0).Single(c => c.Id == id).Kind != CardKind.Alcohol) &&
            Hand(g, 0).Count(c => c.Kind == CardKind.Alcohol) > g.State.Players[0].Hp,
            "Alcohol with the current locked Slash identity is genuinely omitted from the failed-contest hand-limit discard pool.");
        Cold(g, registry);
        var turn = g.State.TurnNumber; ReachUntil(g, () => g.State.TurnNumber > turn);
        Require(g.CreateSnapshot(0).Players[0].TurnHandLimitCardKindExemptions is null, "The original actual turn ends and expires its one failed-contest hand policy.");
        Cold(g, registry);
    }

    public static void ForeignDyingAlcoholPhysicalVirtualAndPileBoundaries()
    {
        var (g, registry) = Create(wine: true);
        Use(g, Driver, "draw"); ReachPlay(g);
        Use(g, Driver, "store"); Reach(g, p => Action(p, "select-owned-cards"));
        Answer(g, c => c.Cards.Count == 1 && Hand(g, 0).Single(card => card.Id == c.Cards[0]).Kind == CardKind.Slash); ReachPlay(g);
        var stored = g.CreateSnapshot(0).Players[0].ChunlaoCount;
        Require(stored == 1, "A genuine owned Slash movement prepares one bound-pile Wine rescue material.");
        Use(g, Driver, "hurt", [1]);
        ReachUntil(g, () => g.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == 1 &&
            f.ResponderIndex < f.ResponderSeats.Count && f.ResponderSeat == 0));
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single();
        Require(dying.VictimSeat == 1 && dying.ResponderSeat == 0 && g.State.CurrentSeat == 0 && g.State.Players[1].Hp == 0 &&
            P(g) is null &&
            !Facts<ProgramBindingStartedEvent>(g).Any(f => f.SkillId == "fixture:gs-pile-rescue") &&
            !Facts<ProgramBindingStartedEvent>(g).Any(f => f.SkillId == Wine && f.OwnerSeat == 1) &&
            !Facts<ProgramDyingRescueEvent>(g).Any(f => f.DyingFrameId == dying.Id),
            "At the original Dying frame's real human responder cursor, the policy owner's turn prohibits the foreign victim's physical/SelfDying Wine and third-party pile Wine; no usable rescue prompt or paid source is invented.");
        Cold(g, registry); ReachPlay(g);
        Require(!g.State.Players[1].IsAlive && g.CreateSnapshot(0).Players[0].ChunlaoCount == stored &&
            !g.CardMovements.Any(m => m.From == CardLocation.Chunlao(0) && m.To == CardLocation.Processing),
            "The prohibited original-victim Wine path cannot pay or drain the legitimate provider's pile.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 2 && p.SkillPrompt?.SkillId == Wine);
        var nextDying = g.ResolutionStack.OfType<DyingFrame>().Single();
        var self = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Wine);
        Require(g.State.CurrentSeat == 2 && nextDying.VictimSeat == 2 && self.DyingAlcoholPermission is { ActorSeat: 2, ResponderSeat: 2 } permission &&
            permission.DyingFrameId == nextDying.Id && permission.SkillInstanceId == self.SkillInstanceId,
            "On the next real actor's turn, its native published SelfDying producer regains an exact permission tied to this new victim and instance.");
        Prepared(g); Cold(g, registry); Advance(g);
        Reach(g, p => p.PlayerSeat == 2 && p.SkillPrompt?.SkillId == Hp);
        Require(g.State.Players[2].Hp == 1 && Facts<ProgramDyingRescueEvent>(g).Single(f => f.DyingFrameId == nextDying.Id) is { CardId: 0, VictimSeat: 2 },
            "Native choice actually consumes the zero-entity Wine once and returns through the exact current Dying/HP observer, rather than a presence-only exemption.");
        Cold(g, registry); Advance(g); ReachPlay(g);
        Require(Facts<ProgramDyingRescueEvent>(g).Count(f => f.DyingFrameId == nextDying.Id) == 1, "The native rescue never pays or recovers twice after its real child return.");
        Cold(g, registry);
    }

    public static void NativeAiActuallyIssuesOriginalTargetGrant()
    {
        var (g, registry) = Create(native: true);
        ReachUntil(g, () => Facts<OriginalTargetAdditionGrantedEvent>(g).Length > 0);
        var granted = Facts<OriginalTargetAdditionGrantedEvent>(g).First().Grant;
        Require(granted.Source.SkillId == Contest && granted.Source.OwnerSeat == g.State.CurrentSeat && granted.TargetSeat != granted.Source.OwnerSeat &&
            g.AcceptedCommands.All(command => command is not AnswerPromptCommand) &&
            Facts<PindianResultDeterminedEvent>(g).Any(f => f.Result.SourceSeat == granted.Source.OwnerSeat && f.Result.SourceRank == 13 && f.Result.SourceWon),
            "A native AI starts the real phase-limited contest, selects the K identity and issues a same-actual-turn original-target grant: " +
            JsonSerializer.Serialize(new { g.State.CurrentSeat, g.State.TurnNumber, Grant = granted, Pindian = Facts<PindianResultDeterminedEvent>(g), Prompt = P(g) }));
        Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Create(bool wine = false, bool native = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(wine, native));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand());
        // A quiet human setup boundary prevents Start from running the whole all-AI match.
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, native ? "fixture:gs-other-1" : "fixture:gs-owner", g.Revision, P(g)!.PromptId));
        if (!native) ReachPlay(g);
        return (g, registry);
    }
    private static CardSnapshot[] Hand(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].Hand.ToArray();
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static void Use(GameEngine g, string skill, string activation, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, skill, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void ContestWith(GameEngine g, ContentRegistry registry, int id, int target)
    {
        Accept(g, new UseProgramSkillCommand(0, Contest, "contest", [id], [target], g.Revision, P(g)!.PromptId));
        Require(g.ResolutionStack.Last() is PindianFrame && g.CreateSnapshot(target).PendingDecision is { IsPrivate: true } &&
            Enumerable.Range(0, 4).Where(seat => seat != target).All(seat => g.CreateSnapshot(seat).PendingDecision is null &&
                g.CreateSnapshot(seat).PublicRevealedCards.Count == 0),
            "The selected real opponent alone sees its still-uncommitted Pindian choice; no other prepared view sees either committed card.");
        Prepared(g); Cold(g, registry); ReachPlay(g);
    }
    private static void Play(GameEngine g, int id, IReadOnlyList<int> targets, CardKind kind)
    {
        var action = g.GetHumanLegalActions().Single(a => a.CardId == id && (a.PlayedCardKind ?? Hand(g, 0).Single(c => c.Id == id).Kind) == kind && a.TargetSeats.SequenceEqual(targets));
        Accept(g, new PlayCardCommand(0, id, targets, g.Revision, P(g)!.PromptId, PlayedCardKind: kind)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => ReachUntil(g, () => P(g) is { } p && predicate(p));
    private static void ReachUntil(GameEngine g, Func<bool> predicate)
    { for (var step = 0; step < 180; step++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed Gao Shun prefix missed its boundary: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Committed or Finalized or Hp) Continue(g);
        else if (p is { PlayerSeat: 0 } && Action(p, "original-target-addition")) Answer(g, c => c.Targets.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Prepared(GameEngine g)
    {
        for (var viewer = 0; viewer < 4; viewer++)
        {
            var view = g.CreateSnapshot(viewer);
            if (viewer != 0) Require(view.Players[0].Hand.Count == 0, "Other views reveal no private hand identities.");
            if (view.PendingDecision is not { } p) continue;
            Frozen(p.Choices); foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); }
        }
    }
    private static void Frozen<T>(IReadOnlyList<T> values) { if (values is not IList<T> list || list.Count == 0) return; Require(list.IsReadOnly, "Nested prepared collections are immutable."); try { list[0] = list[0]; } catch (NotSupportedException) { return; } throw new InvalidOperationException("A prepared collection accepted mutation."); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)), "A real command prefix cold-restores all four views, exact owning frames, frozen action values and payment ledger.");
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }

    private sealed class Fixture(bool wine, bool native) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-current-gao-shun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var a = typeof(StandardClassicGeneralPackage).Assembly;
            string Embedded(string suffix) { using var stream = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Embedded("boundary-gao-shun.rules.json"), Embedded("boundary-gao-shun.presentation.json"));
            foreach (var id in new[] { Contest, Identity }) b.AddSkill(new(id, actual.Presentations[id].Name, "实际当前OL规则") { Program = actual.Programs[id], ProgramPresentation = actual.Presentations[id], Tags = id == Identity ? SkillTag.Locked : SkillTag.None });
            var catalog = SkillProgramCatalog.Load(FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString()), JsonSerializer.Serialize(new { schemaVersion = 3,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实驱动", description = "固定实体、装备和失血" }, ["fixture:gs-quiet"] = new { name = "安静", description = "真实跳过出牌" },
                ["fixture:gs-hit"] = new { name = "真实自失血", description = "首个实际回合濒死" }, ["fixture:gs-pile-rescue"] = new { name = "真实醇醪型救援", description = "实体公共pile付款" },
                [Committed] = new { name = "用牌冷锚点", description = "已接受Slash", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["suppress"] = "真实失血抑制" } },
                [Finalized] = new { name = "实际目标冷锚点", description = "原增目标窗口完成后的Slash", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Wine] = new { name = "原生濒死酒", description = "真实SelfDying生产者", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Hp] = new { name = "回复冷锚点", description = "真实HP观察", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var program in catalog.Programs.Values) b.AddSkill(new(program.Id, catalog.Presentations[program.Id].Name, "机制夹具") { Program = program, ProgramPresentation = catalog.Presentations[program.Id] });
            b.AddSkill(new("fixture:gs-selection", "固定选择", "没有运行逻辑") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddSkill(new("fixture:gs-suppression", "真实抑制", "HP1抑制非锁定技能") { SuppressionRule = new(1) });
            b.AddGeneral(new("fixture:gs-owner", "高顺机制", "supporter", Contest, "qun", 4,
                native ? [Identity, Committed, Finalized] : [Identity, Driver, Committed, Finalized, "fixture:gs-pile-rescue", Hp, "fixture:gs-suppression"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:gs-other-{i}", "固定目标", "supporter", "fixture:gs-selection", "wei", wine ? 1 : 12,
                wine ? ["fixture:gs-quiet", "fixture:gs-hit", Wine, Hp] : ["fixture:gs-quiet"]));
            b.AddCard(new("fixture:gs-alcohol", "酒", "基本牌", "实体点数保持1", CardKind.Alcohol));
            b.AddCard(new("fixture:gs-lion", "白银狮子", "装备牌", "真实防具", CardKind.SilverLion));
            b.AddDeck(new("fixture:gs-deck", "固定实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(
                native ? "fixture:gs-alcohol" : wine ? (i % 4 == 0 ? "standard:slash" : "fixture:gs-alcohol") :
                    (i % 3 == 0 ? "fixture:gs-lion" : i % 3 == 1 ? "fixture:gs-alcohol" : "standard:draw_two"), Suit.Spade, 1)).ToArray() });
            b.AddMode(new(Mode, "当前普通OL高顺机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:gs-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:gs-owner", "fixture:gs-other-1", "fixture:gs-other-2", "fixture:gs-other-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:gs-driver","revision":1,"activations":[
      {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"draw","target":"owner","amount":20}]},
      {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
      {"id":"boost","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"grantTurnCardDamageModifier","target":"owner","cardKinds":["slash"],"amount":1}]},
      {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
      {"id":"store","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"cardKinds":["slash"],"zones":["hand"],"resultBind":"chun"},{"op":"moveBoundCards","target":"owner","sourceBind":"chun","destination":"ownerPersistentZone","destinationZone":"chunlao","awaitMovementTriggers":true}]}]},
     {"id":"fixture:gs-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
     {"id":"fixture:gs-hit","revision":1,"triggers":[{"id":"real-dying","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
     {"id":"fixture:gs-committed","revision":1,"triggers":[{"id":"accepted","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"},{"id":"suppress"}]},{"op":"loseHp","target":"owner","amount":4,"condition":{"kind":"choiceIs","sourceBind":"seen","optionId":"suppress"}}]}]},
     {"id":"fixture:gs-finalized","revision":1,"triggers":[{"id":"finalized","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"priority":-100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:gs-hp","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:gs-wine","revision":1,"triggers":[{"id":"wine","window":"selfDyingResponse","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"accept","options":[{"id":"continue"}]},{"op":"useVirtualDyingAlcohol","target":"owner"}]}]},
     {"id":"fixture:gs-pile-rescue","revision":1,"triggers":[{"id":"rescue","window":"dyingResponse","subject":"owner","optional":true,"condition":{"kind":"compare","left":{"kind":"currentOwnedZoneCount","zone":"chunlao"},"operator":"greaterThan","right":{"kind":"integerConstant","value":0}},"effects":[{"op":"selectSourceCard","target":"owner","cardSource":"owner","zones":["chunlao"],"resultBind":"rescue"},{"op":"useBoundCardAsDyingAlcohol","target":"owner","sourceBind":"rescue"}]}]}]}
    """;
}
