using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZeroMaterialTargetAdjustmentChecks
{
    private const string Driver = "fixture:zero-target-driver", Observer = "fixture:zero-target-observer";
    private const string Mode = "identity:classic-zero-targets", Chain = "ol:xianwan", Draw = "ol:bingxin";
    private const string Tiered = "boundary:jiaozhao-round-current", Upgrade = "boundary:danxin-capped-current";
    private const string Adjustment = "boundary:qiaoshui-current", Red = "classic:fumian";
    private const string PairObserver = "fixture:zero-target-pair-observer";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void ZeroMaterialUsesPreserveAppearanceTargetsAndReplay()
    {
        foreach (var source in new[] { Chain, Draw, Tiered }) Run(source, CardKind.Slash);
        foreach (var source in new[] { Draw, Tiered })
        foreach (var kind in new[] { CardKind.Peach, CardKind.Alcohol }) Run(source, kind);
        Run(Tiered, CardKind.DrawTwo);
        SourceChanges();
    }

    public static void LegacyAndBorrowedSwordZeroMaterialContinuations()
    {
        BorrowedSwordPairs();
        foreach (var source in new[] { Chain, Draw, Tiered }) LegacySlash(source);
    }

    private static void BorrowedSwordPairs()
    {
        var (game, registry) = Create(borrowed: true); Play(game); PrepareProducer(game, Tiered);
        foreach (var seat in new[] { 1, 3 }) { Use(game, "equip", [seat]); Play(game); }
        var weapons = new[] { 1, 3 }.Select(seat => V(game, seat).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id).ToArray();
        Contest(game); Play(game);
        var targets = new[] { 1, 2, 3, 2 };
        var action = AssertQueries(game).Single(a => a.CardId == 0 && a.PlayedCardKind == CardKind.BorrowedSword && a.TargetSeats.SequenceEqual(targets));
        Require(action.ProgramActivationId == "next-actual-use-target-adjustment", "The two holder/victim pairs are the exact published winning-grant adjustment.");
        var hp = V(game, 2).Hp; var grant = F<NextActualUseTargetAdjustmentGrantedEvent>(game).Single();
        game = Cold(game, registry);
        Accept(game, new PlayCardCommand(0, 0, targets, game.Revision, P(game)!.PromptId, CardKind.BorrowedSword) { ConversionSource = action.ConversionSource });
        Reach(game, p => p.SkillPrompt?.SkillId == Observer);
        var outer = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == 0 && u.CardKind == CardKind.BorrowedSword);
        Require(outer.TargetsAdjusted && outer.TargetSeats.SequenceEqual(targets) && outer.Action is { PhysicalCards.Count: 0 } &&
            outer.Action.ConversionChain.SequenceEqual([action.ConversionSource!]) && outer.TieredRoundConversionUse is { MaterialCount: 0 },
            "The logical trick owns both exact pairs, neutral zero cost and the real tiered issuance before either forced Slash.");
        Private(game); Reject(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.SkillPrompt?.SkillId == PairObserver);
        var first = ForcedSlash(game, outer.Id, outer.Action!.ActionId, 1, 2);
        Private(game); Reject(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.SkillPrompt?.SkillId == PairObserver);
        var second = ForcedSlash(game, outer.Id, outer.Action!.ActionId, 3, 2);
        Require(V(game, 2).Hp == hp - 1 && game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == outer.Id).TargetIndex == 2 &&
            F<BorrowedSwordResolvedEvent>(game).Single(e => e.ResolutionId == outer.Id).WeaponOwnerSeat == 1 &&
            F<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == first.Id) == 1,
            "The first pair completes before the same logical owning trick resumes precisely at the second pair.");
        Private(game); Reject(game); game = Cold(game, registry); Play(game);
        var pairFacts = F<BorrowedSwordResolvedEvent>(game).Where(e => e.ResolutionId == outer.Id).ToArray();
        Require(pairFacts.Length == 2 && pairFacts.Select(e => e.WeaponOwnerSeat).SequenceEqual([1, 3]) &&
            pairFacts.All(e => e.SlashTargetSeat == 2 && e.UsedSlash && e.TransferredWeaponCardId is null) &&
            pairFacts.Select(e => e.SlashCardId).SequenceEqual(new int?[] { first.CardId, second.CardId }) && V(game, 2).Hp == hp - 2,
            "Both real holders independently pay their published actual Slash against the selected victim, with no weapon-transfer branch substituted.");
        foreach (var child in new[] { first, second })
            Require(game.CardMovements.Count(m => m.CardId == child.CardId && m.From == CardLocation.Hand(child.SourceSeat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                game.CardMovements.Count(m => m.CardId == child.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1 &&
                F<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == child.Id) == 1,
                "Each forced child pays and cleans its actual original entity once across its private cold continuation.");
        Require(F<NextActualUseTargetAdjustmentConsumedEvent>(game) is [var consumed] && consumed.GrantProgramFrameId == grant.ProgramFrameId && consumed.OriginFrameId == outer.Id &&
            F<TieredRoundConversionUseIssuedEvent>(game).Count(e => e.Receipt.OwnerFrameId == outer.Id) == 1 &&
            F<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == outer.Id) == 1 && game.CardMovements.All(m => m.CardId != 0) &&
            weapons.Select((id, i) => game.CreateCardZoneDiagnostics().Any(z => z.CardId == id && z.Location == CardLocation.Equipment(i == 0 ? 1 : 3))).All(v => v) &&
            !game.ResolutionStack.OfType<CardUseFrame>().Any(u => new[] { outer.Id, first.Id, second.Id }.Contains(u.Id)),
            "The neutral original trick consumes/finishes once, retains both real weapons and leaves no parent or pair child hanging.");
        _ = Cold(game, registry);
    }

    private static CardUseFrame ForcedSlash(GameEngine game, long outerId, long outerAction, int actor, int victim)
    {
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.SourceSeat == actor && u.CardKind == CardKind.Slash);
        Require(use.CardId > 0 && use.PhysicalCardIds is [var physical] && physical == use.CardId && use.TargetSeats.SequenceEqual([victim]) &&
            use.Action is { Type: CardActionType.Use, PhysicalCards.Count: 1 } action && action.ActorSeat == actor && action.ProviderSeat == actor &&
            action.ParentActionId == outerAction && action.PhysicalCards[0].CardId == use.CardId && action.PhysicalCards[0].From == CardLocation.Hand(actor) &&
            game.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == outerId) &&
            game.CardMovements.Count(m => m.CardId == physical && m.From == CardLocation.Hand(actor) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
            "A genuine paid forced Slash pauses under the exact logical BorrowedSword parent, retaining its original actor/provider/entity.");
        return use;
    }

    private static void LegacySlash(string source)
    {
        var (game, registry) = Create(); Play(game); PrepareProducer(game, source);
        if (source == Draw) while (V(game, 0).Hp > V(game, 0).HandCount) { Use(game, "hurt", [0]); Play(game); }
        Use(game, "legacy"); Play(game);
        var action = AssertQueries(game).Single(a => a.CardId == 0 && a.ConversionSource?.SkillId == source && a.PlayedCardKind == CardKind.Slash &&
            a.ProgramActivationId == "next-card-target-adjustment" && a.TargetSeats.SequenceEqual([1, 2]));
        var hp = new[] { V(game, 1).Hp, V(game, 2).Hp }; game = Cold(game, registry);
        Accept(game, new PlayCardCommand(0, 0, action.TargetSeats, game.Revision, P(game)!.PromptId, CardKind.Slash) { ConversionSource = action.ConversionSource });
        Reach(game, p => p.SkillPrompt?.SkillId == Observer);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == 0 && u.CardKind == CardKind.Slash);
        Require(use.TargetSeats.SequenceEqual([1, 2]) && use.Action is { PhysicalCards.Count: 0 } && use.Action.ConversionChain.SequenceEqual([action.ConversionSource!]) &&
            F<NextActualUseTargetAdjustmentConsumedEvent>(game).Length == 0 && F<RedAdditionalTargetsConsumedEvent>(game).Length == 0,
            "The old actual grant publishes and accepts the precise neutral zero-material selection without borrowing another grant's identity.");
        Private(game); Reject(game); game = Cold(game, registry); Play(game);
        Require(V(game, 1).Hp == hp[0] - 1 && V(game, 2).Hp == hp[1] - 1 &&
            F<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id) == 1 && game.CardMovements.All(m => m.CardId != 0),
            "The old-grant path resolves the same real two-target Slash and finishes once for every zero-material producer.");
        if (source == Draw) Require(F<DrawFundedDistinctBasicPaidEvent>(game).Length == 1 && F<DrawFundedDistinctBasicReturnedEvent>(game).Length == 1, "The old-grant draw-funded path draws and returns only once.");
        if (source == Chain) Require(F<ChainedStateBasicPaidEvent>(game).Length == 1 && F<ChainedStateBasicReturnedEvent>(game).Length == 1, "The old-grant chain path pays and returns only once.");
        _ = Cold(game, registry);
    }

    private static void PrepareProducer(GameEngine game, string source)
    {
        Use(game, "gain-" + source); Play(game);
        if (source == Chain) { Use(game, "chain", [0]); Play(game); }
        if (source != Tiered) return;
        for (var i = 0; i < 2; i++)
        {
            Use(game, "damage"); Reach(game, p => p.SkillPrompt?.SkillId == Upgrade && Has(p, "program-action", "activate"));
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(game);
        }
    }

    private static void Run(string source, CardKind kind)
    {
        var (game, registry) = Create(); Play(game);
        Use(game, "gain-" + source); Play(game);
        if (source == Chain) { Use(game, "chain", [0]); Play(game); }
        if (source == Tiered)
        {
            for (var i = 0; i < 2; i++)
            {
                Use(game, "damage"); Reach(game, p => p.SkillPrompt?.SkillId == Upgrade && Has(p, "program-action", "activate"));
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(game);
            }
            Require(V(game, 0).ConfiguredConversionTiers?.GetValueOrDefault("boundary-jiaozhao-round-tier") == 2,
                "Two real damage benefits establish tier2 before the material-free use.");
        }
        if (kind == CardKind.Peach) { Use(game, "hurt", [1]); Play(game); }
        Contest(game); Play(game);
        if (source == Draw) while (V(game, 0).Hp > V(game, 0).HandCount) { Use(game, "hurt", [0]); Play(game); }
        if (kind == CardKind.Peach && V(game, 0).Hp == V(game, 0).MaxHp) { Use(game, "hurt", [0]); Play(game); }
        var all = AssertQueries(game);
        var wanted = kind == CardKind.Slash ? new[] { 1, 2 } : new[] { 0, 1 };
        var baseline = all.Where(a => a.CardId == 0 && a.ConversionSource?.SkillId == source && a.PlayedCardKind == kind && a.ProgramActivationId is null).ToArray();
        Require(baseline.Length > 0 && all.Where(a => a.CardId == 0).All(a => a.ProgramActivationId != "red-additional-targets"),
            "A real red-only benefit retains the neutral material-free baseline and never recolors it or adds red-only targets.");
        var action = all.Single(a => a.CardId == 0 && a.ConversionSource?.SkillId == source && a.PlayedCardKind == kind &&
            a.ProgramActivationId == "next-actual-use-target-adjustment" && a.TargetSeats.SequenceEqual(wanted));
        Require(kind != CardKind.DrawTwo || all.Where(a => a.CardId == 0 && a.PlayedCardKind == kind).All(a => a.TargetSeats.Count <= 2),
            "The tiered ordinary-trick menu applies one extension once, without expanding an already-expanded option again.");
        var hp = Enumerable.Range(0, 4).Select(s => V(game, s).Hp).ToArray();
        var hand = Enumerable.Range(0, 4).Select(s => V(game, s).HandCount).ToArray();
        var grant = F<NextActualUseTargetAdjustmentGrantedEvent>(game).Single();
        game = Cold(game, registry);
        Accept(game, new PlayCardCommand(0, 0, action.TargetSeats, game.Revision, P(game)!.PromptId, kind, action.TargetCardId)
        { ConversionSource = action.ConversionSource });
        Reach(game, p => p.SkillPrompt?.SkillId == Observer);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == 0 && u.CardKind == kind);
        Require(use.TargetSeats.SequenceEqual(wanted) && use.Action is { Type: CardActionType.Use, PhysicalCards.Count: 0 } actual &&
            actual.ActorSeat == 0 && actual.ProviderSeat == 0 && actual.EffectiveKind == kind && actual.TargetSeats.SequenceEqual(wanted) &&
            actual.ConversionChain.SequenceEqual([action.ConversionSource!]) && actual.EffectiveSuit is null or Suit.None &&
            actual.EffectiveRank is null or 0 && actual.EffectiveIsRed != true && use.PhysicalCardIds is { Count: 0 },
            "Actual submission freezes the published ordered targets, true zero costs, provider and neutral appearance on its owning use.");
        Require(F<NextActualUseTargetAdjustmentConsumedEvent>(game) is [var consumed] && consumed.GrantProgramFrameId == grant.ProgramFrameId &&
            consumed.OriginFrameId == use.Id && consumed.CardActionId == use.Action!.ActionId && !consumed.IsNullificationUse &&
            F<RedAdditionalTargetsConsumedEvent>(game).Length == 0,
            "The actual declared use consumes its original winning grant once and leaves the unrelated red-only grant unspent.");
        Private(game); Reject(game); game = Cold(game, registry); Play(game);
        Require(F<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id && e.CardId == 0 && e.CardKind == kind) == 1 &&
            !game.ResolutionStack.Any(f => f.Id == use.Id) && game.CardMovements.All(m => m.CardId != 0) &&
            F<NextActualUseTargetAdjustmentConsumedEvent>(game).Length == 1,
            "The true multi-target use finishes and returns once, without an invented entity0 movement or a second grant payment.");
        if (kind == CardKind.Slash) Require(V(game, 1).Hp == hp[1] - 1 && V(game, 2).Hp == hp[2] - 1,
            "Both selected Slash targets really take their independent damage children, including the distant extra target.");
        if (kind == CardKind.Peach) Require(V(game, 0).Hp == hp[0] + 1 && V(game, 1).Hp == hp[1] + 1 &&
            F<RecoveryAppliedEvent>(game).Count(e => e.SourceSeat == 0 && wanted.Contains(e.TargetSeat)) == 2,
            "The existing adjusted recovery continuation actually heals both selected beneficiaries once.");
        if (kind == CardKind.Alcohol) Require(V(game, 0).HasAlcoholEffect && V(game, 1).HasAlcoholEffect &&
            F<AlcoholAppliedEvent>(game).Count(e => e.ResolutionId == use.Id && wanted.Contains(e.SourceSeat)) == 2,
            "The existing adjusted Alcohol continuation applies the effect to both selected beneficiaries once.");
        if (kind == CardKind.DrawTwo) Require(V(game, 0).HandCount == hand[0] + 2 && V(game, 1).HandCount == hand[1] + 2,
            "The tiered neutral ordinary trick really executes both published DrawTwo beneficiaries.");
        if (source == Chain) Require(!V(game, 0).IsChained && F<ChainedStateBasicPaidEvent>(game).Length == 1 &&
            F<ChainedStateBasicReturnedEvent>(game).Length == 1, "One chain release pays and returns once for the entire Slash.");
        if (source == Draw) Require(V(game, 0).HandCount == hand[0] + 1 && F<DrawFundedDistinctBasicPaidEvent>(game).Length == 1 &&
            F<DrawFundedDistinctBasicIssuedEvent>(game).Length == 1 && F<DrawFundedDistinctBasicReturnedEvent>(game).Length == 1,
            "The exact postdraw menu preserves the accepted extension while the original draw and paid return occur once.");
        _ = Cold(game, registry);
    }

    private static void SourceChanges()
    {
        var (game, registry) = Create(); Play(game); Use(game, "gain-" + Chain); Play(game); Use(game, "chain", [0]); Play(game);
        Contest(game); Play(game);
        Require(AssertQueries(game).Any(a => a.CardId == 0 && a.ProgramActivationId == "next-actual-use-target-adjustment"), "The original live source publishes the extension.");
        Use(game, "lose-" + Adjustment); Play(game);
        Require(AssertQueries(game).Any(a => a.CardId == 0 && a.ChainedStateBasicUse.HasValue) &&
            AssertQueries(game).All(a => a.CardId != 0 || a.ProgramActivationId != "next-actual-use-target-adjustment"),
            "Losing the granting skill removes only its target extension and retains the real material-free baseline.");
        Use(game, "gain-" + Adjustment); Play(game);
        Require(AssertQueries(game).All(a => a.CardId != 0 || a.ProgramActivationId != "next-actual-use-target-adjustment"),
            "A newly acquired same-name source cannot inherit the old instance's unconsumed grant.");
        Use(game, "lose-" + Chain); Play(game); Require(AssertQueries(game).All(a => a.ConversionSource?.SkillId != Chain), "Removing the producer invalidates its menu.");
        Use(game, "gain-" + Chain); Play(game); Require(AssertQueries(game).Any(a => a.CardId == 0 && a.ConversionSource?.SkillId == Chain), "A real newly granted producer returns its own baseline.");
        _ = Cold(game, registry);
    }

    private static IReadOnlyList<LegalAction> AssertQueries(GameEngine game)
    {
        var before = State(game); var all = game.GetHumanLegalActions();
        var actor = ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(game)!)[0];
        var narrow = (IReadOnlyList<LegalAction>)typeof(GameEngine).GetMethod("BuildLegalActions", Flags)!.Invoke(game, [actor, true, (int?)0])!;
        Require(JsonSerializer.Serialize(narrow) == JsonSerializer.Serialize(all.Where(a => a.CardId == 0).ToArray()) && before == State(game),
            "Wide and exact entity0 queries retain identical ordered JSON, all four private views, revision, commands, events, frames and movements.");
        Require(all.Where(a => a.CardId == 0).Select(a => (a.Kind, a.PlayedCardKind, a.ConversionSource, a.TargetCardId, Targets: string.Join(',', a.TargetSeats))).Distinct().Count() == all.Count(a => a.CardId == 0),
            "Producer and common query expansion never publish duplicate material-free tuples.");
        return all;
    }

    private static void Contest(GameEngine game)
    {
        var card = V(game, 0).Hand.First(c => c.Suit == Suit.Heart).Id;
        Accept(game, new UseProgramSkillCommand(0, Adjustment, "contest", [card], [1], game.Revision, P(game)!.PromptId)); Play(game);
        Require(F<PindianResultDeterminedEvent>(game).Single().Result.SourceWon && F<NextActualUseTargetAdjustmentGrantedEvent>(game).Length == 1,
            "The adjustment comes from a real accepted winning Pindian with actual hand payment.");
    }
    private static (GameEngine, ContentRegistry) Create(bool borrowed = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(borrowed));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:zero-target-owner", game.Revision, P(game)!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == Red && Has(p, "program-action", "activate"));
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => Has(p, "option-id", "targets")); Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "targets");
        return (game, registry);
    }
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Has(PendingDecision prompt, string key, string value) => prompt.Choices.Any(c => c.Parameters.GetValueOrDefault(key) == value);
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real zero-material command."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate) { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Use(GameEngine game, string activation, IReadOnlyList<int>? targets = null) => Accept(game, new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 160; i++)
        {
            var p = P(game); if (p is not null && predicate(p)) return;
            if (p is { PlayerSeat: 0 } && Has(p, "option-id", "continue")) Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p is { PlayerSeat: 0 } && Has(p, "program-action", "skip")) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Zero-material fixed fixture did not reach its boundary: " + JsonSerializer.Serialize(new { Prompt = P(game), Frames = game.ResolutionStack }));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { game.Revision, Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Require(State(restored) == State(game), "Independent cold command replay preserves all views and the exact neutral owning target/payment chain."); return restored; }
    private static void Private(GameEngine game) { var p = P(game)!; Require(p.IsPrivate && Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).PendingDecision is null), "The owning paused program remains private to its actor."); }
    private static void Reject(GameEngine game) { var before = State(game); var p = P(game)!; Require(!game.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished"), game.Revision)).Accepted && before == State(game), "Invalid continuation input does not mutate or pay the actual zero-material use again."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool borrowed) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:zero-material-targets", "1.0.0", "Real commands combining registered shared zero-material and target-adjustment capabilities.");
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (resource, ids) in new (string, string[])[] {
                ("boundary-jian-yong", [Adjustment]), ("classic-qin-mi", ["classic:tianbian"]), ("classic-wu-xian", [Red]),
                ("ordinary-yang-yan", [Chain]), ("ordinary-wang-xiang", [Draw]), ("boundary-guo-huang-hou", [Tiered, Upgrade]) })
            {
                using var rules = new StreamReader(typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{resource}.rules.json")!);
                using var presentation = new StreamReader(typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{resource}.presentation.json")!);
                var catalog = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
                foreach (var id in ids) builder.AddSkill(new(id, id, "Unmodified registered production program.") { Program = catalog.Programs[id] });
            }
            var root = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:zero-target-driver","revision":1,"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]}]},
              {"id":"fixture:zero-target-observer","revision":1,"triggers":[{"id":"paused","window":"cardUseCommitted","ownerRelation":"actor","optional":false,"cardKinds":["slash","peach","alcohol","drawTwo"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var activations = root["skills"]![0]!["activations"]!.AsArray();
            activations.Add(JsonNode.Parse("""{"id":"legacy","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantNextCardTargetAdjustment","target":"owner"}]}"""));
            if (borrowed)
            {
                activations.Add(JsonNode.Parse("""{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]}"""));
                root["skills"]![1]!["triggers"]![0]!["cardKinds"]!.AsArray().Add("borrowedSword");
                root["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:zero-target-pair-observer","revision":1,"triggers":[{"id":"pair","window":"cardUseCommitted","ownerRelation":"observer","optional":false,"cardKinds":["slash"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}"""));
            }
            foreach (var id in new[] { Chain, Draw, Tiered, Adjustment })
            {
                activations.Add(JsonNode.Parse(JsonSerializer.Serialize(new { id = "gain-" + id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "grantSkills", target = "owner", skillIds = id == Tiered ? new[] { Tiered, Upgrade } : new[] { id } } } })));
                activations.Add(JsonNode.Parse(JsonSerializer.Serialize(new { id = "lose-" + id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { id }, sourceBind = "fixture:zero-target-noop" } } })));
            }
            var labels = new Dictionary<string, object> {
                [Driver] = new { name = "真实小夹具", description = "通过已接受命令获得和失去技能、改变连环、伤害和体力。" },
                [Observer] = new { name = "实际提交暂停", description = "保留拥有帧用于冷回放。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [PairObserver] = new { name = "实际借刀子链暂停", description = "保留原持刀者的真实实体费用。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            if (!borrowed) labels.Remove(PairObserver);
            var local = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var id in local.Programs.Keys) builder.AddSkill(new(id, id, "Small legal shared-mechanism fixture.") { Program = local.Programs[id] });
            builder.AddSkill(new("fixture:zero-target-noop", "来源替换", "无运行能力"));
            builder.AddSkill(new("fixture:zero-target-selection", "固定其他候选", "只用公开选择权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            builder.AddGeneral(new("fixture:zero-target-owner", "共享零材料目标机制", "supporter", Driver, "shu", 7,
                borrowed ? [Adjustment, "classic:tianbian", Red, Observer, PairObserver] : [Adjustment, "classic:tianbian", Red, Observer]));
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:zero-target-peer-{i}", "固定原生目标", "supporter", "fixture:zero-target-selection", "qun", 8));
            builder.AddDeck(new("fixture:zero-target-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, 7))
                .Concat(borrowed ? Enumerable.Range(0, 20).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)) : []).ToArray() });
            builder.AddMode(new(Mode, "共享机制固定4人", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zero-target-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zero-target-owner", "fixture:zero-target-peer-1", "fixture:zero-target-peer-2", "fixture:zero-target-peer-3"]));
        }
    }
}
