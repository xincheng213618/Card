using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryJianYongChecks
{
    private const string Skill = "boundary:qiaoshui-current";
    private const string Driver = "fixture:jy-driver";
    private const string Multi = "fixture:jy-multi";
    private const string Observer = "fixture:jy-committed";
    private const string Mode = "identity:classic-boundary-jian-yong-fixture";

    public static void NextActualUsePaidWinsRefreshSingleTargetTokenAndClaimOriginalCards()
    {
        var (game, registry) = Create(); ReachPlay(game);
        var first = Contest(game, registry, 1, take: true);
        var second = Contest(game, registry, 3, take: true);
        var grants = Grants(game);
        Require(first.SourceWon && second.SourceWon && grants.Length == 2 && grants[0].ProgramFrameId != grants[1].ProgramFrameId,
            "Two real paid wins issue distinct latest receipts without stacking target allowance.");
        var actions = game.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Slash).ToArray();
        Require(actions.Any(a => a.TargetSeats.SequenceEqual(new[] { 1, 3 })) && actions.All(a => a.TargetSeats.Count <= 2),
            "Repeated unconsumed successes permit exactly one extra legal target.");
        var hp = game.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        var action = actions.First(a => a.TargetSeats.SequenceEqual(new[] { 1, 3 }));
        var entity = action.CardId!.Value;
        Play(game, action); ReachObserver(game); RejectUnpublished(game); Cold(game, registry); Continue(game); ReachPlay(game);
        var consumed = Consumed(game).Single();
        Require(consumed.GrantProgramFrameId == grants[1].ProgramFrameId && !consumed.IsNullificationUse && consumed.EffectiveKind == CardKind.Slash &&
            game.CreateSnapshot(0).Players[1].Hp == hp[1] - 1 && game.CreateSnapshot(0).Players[3].Hp == hp[3] - 1 &&
            PayCount(game, entity) == 1 && CleanupCount(game, entity) == 1 &&
            !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The latest token is consumed by one real use, each target resolves, the original entity pays and cleans once, and Slash quota remains one.");
        Contest(game, registry, 1, take: false);
        EndPlay(game);
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard && game.CreateSnapshot(0).TurnNumber > 1);
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramActivationId == "next-actual-use-target-adjustment"),
            "An unused prior-turn receipt cannot create adjusted actions on the owner's next actual turn.");
        Cold(game, registry);
    }

    public static void NextActualUseFailureKeepsZongshiAndBlocksTricksBeforePayment()
    {
        var (game, registry) = Create(win: false); ReachPlay(game);
        var beforeHand = game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToHashSet();
        var result = Contest(game, registry, 1, take: true);
        Require(!result.SourceWon && result.SourceRank == result.OpponentRank &&
            beforeHand.Contains(result.SourceCardId) && game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == result.SourceCardId) &&
            game.CardMovements.Count(m => m.CardId == result.SourceCardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.pindian.claim") == 1,
            "A tied contest disables only Qiaoshui and Zongshi still claims the owner's original paid contest entity.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill) &&
            !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:qice") &&
            game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Failure rejects both another contest and all-hand ordinary Trick conversion before payment while basic uses remain available.");
        var before = State(game);
        var cost = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        Require(!game.Submit(new UseProgramSkillCommand(0, Skill, "contest", [cost], [1], game.Revision, P(game)!.PromptId)).Accepted && before == State(game),
            "An unavailable repeat contest cannot pay a card or alter any private view.");
        Use(game, "opponent-duel", [1]); Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        var slash = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "slash");
        Answer(game, c => c.Id == slash.Id); ReachPlay(game);
        Require(Grants(game).Length == 0 && game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Any(e =>
            e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash),
            "Failure does not prohibit a real paid ordinary Slash response.");
        Cold(game, registry);

        var (counter, counterRegistry) = Create(win: false, counterDeck: true); ReachPlay(counter);
        Contest(counter, counterRegistry, 1, take: false);
        Use(counter, "opponent-duel", [1]);
        for (var step = 0; step < 80 && P(counter) is not { PlayerSeat: 0, Kind: DecisionKind.PlayCard }; step++)
        {
            var p = P(counter);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.Nullification })
                Require(!p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "nullification"),
                    "The failed source cannot offer a true counterspell payment despite Response syntax.");
            Advance(counter);
        }
        Require(!counter.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Any(e =>
            e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Nullification),
            "A failed Qiaoshui blocks actual counterspell use before any physical response cost.");
        Cold(counter, counterRegistry);
    }

    public static void NextActualUseMultiMaterialSlashGlobalAndRecoveryKeepTypedReturns()
    {
        var (game, registry) = Create(); ReachPlay(game);
        Require(game.GetHumanLegalActions().Single(a => a.ProgramSkillId == Multi && a.ProgramActivationId == "double-slash").MaxTargetCount == 1,
            "The mature selected-material producer retains its fixed single-target contract without the new token.");
        Contest(game, registry, 1, take: true);
        var multi = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == Multi && a.ProgramActivationId == "double-slash");
        Require(multi.MaxTargetCount == 2, "Only a currently qualified new receipt expands this exact selected-material producer.");
        var costs = multi.SelectableCardIds.Take(2).Order().ToArray();
        var hp = game.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        Accept(game, new UseProgramSkillCommand(0, Multi, "double-slash", costs, [1, 3], game.Revision, P(game)!.PromptId));
        ReachObserver(game);
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Multi);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.SourceSeat == 0 && f.CardKind == CardKind.Slash);
        Require(parent.NextActualUseAdjustment is { Stage: ProgramNextActualUseAdjustmentStage.Applied } receipt &&
            receipt.CardUseFrameId == use.Id && use.AdjustedSlashReturn?.ParentProgramFrameId == parent.Id &&
            use.PhysicalCardIds!.Order().SequenceEqual(costs) && use.Action!.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(costs) &&
            costs.All(id => PayCount(game, id) == 1),
            "The material producer, use and attack own the exact typed parent return and both actual costs before target children run.");
        RejectUnpublished(game); Cold(game, registry); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Observer && game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>()
            .Any(w => w.Continuation == ProgramCardContinuation.CompletedSlash));
        Require(game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == parent.Id) && costs.All(id => CleanupCount(game, id) == 1),
            "The original material producer remains until its actual completed-use observer returns, with both costs already cleaned once.");
        Cold(game, registry); Continue(game); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[1].Hp == hp[1] - 1 && game.CreateSnapshot(0).Players[3].Hp == hp[3] - 1 &&
            costs.All(id => PayCount(game, id) == 1 && CleanupCount(game, id) == 1) &&
            game.Events.Select(e => e.Payload).OfType<ProgramSkillResolvedEvent>().Count(e => e.SkillId == Multi && e.Completed) == 1 &&
            Consumed(game).Length == 1 && !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "All targets finish before one exact producer return, both material costs clean once and the extra target adds no Slash use allowance.");
        Cold(game, registry);

        var (global, globalRegistry) = Create(); ReachPlay(global); Contest(global, globalRegistry, 1, take: false);
        var arrow = global.GetHumanLegalActions().Single(a => a.ProgramSkillId == Multi && a.ProgramActivationId == "double-arrow");
        var arrowCosts = arrow.SelectableCardIds.Take(2).Order().ToArray(); var globalHp = global.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        Accept(global, new UseProgramSkillCommand(0, Multi, "double-arrow", arrowCosts, [1], global.Revision, P(global)!.PromptId));
        ReachObserver(global); Cold(global, globalRegistry); Continue(global); ReachPlay(global);
        var actual = global.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Single(e => e.Action.EffectiveKind == CardKind.ArrowBarrage).Action;
        Require(actual.TargetSeats.SequenceEqual(new[] { 2, 3 }) && global.CreateSnapshot(0).Players[1].Hp == globalHp[1] &&
            global.CreateSnapshot(0).Players[2].Hp == globalHp[2] - 1 && global.CreateSnapshot(0).Players[3].Hp == globalHp[3] - 1 &&
            arrowCosts.All(id => PayCount(global, id) == 1 && CleanupCount(global, id) == 1),
            "A multi-material global card omits one real target, preserves all remaining targets and pays both entities only once.");
        Cold(global, globalRegistry);

        var (recovery, recoveryRegistry) = Create(); ReachPlay(recovery);
        Use(recovery, "hurt", [0]); ReachPlay(recovery); Use(recovery, "hurt", [1]); ReachPlay(recovery);
        Contest(recovery, recoveryRegistry, 1, take: false);
        var peach = recovery.GetHumanLegalActions().Single(a => a.ProgramSkillId == Multi && a.ProgramActivationId == "double-peach");
        var peachCosts = peach.SelectableCardIds.Take(2).Order().ToArray(); var oldHp = recovery.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        Accept(recovery, new UseProgramSkillCommand(0, Multi, "double-peach", peachCosts, [1], recovery.Revision, P(recovery)!.PromptId));
        ReachObserver(recovery); Cold(recovery, recoveryRegistry); Continue(recovery); ReachPlay(recovery);
        Require(recovery.CreateSnapshot(0).Players[0].Hp == oldHp[0] + 1 && recovery.CreateSnapshot(0).Players[1].Hp == oldHp[1] + 1 &&
            peachCosts.All(id => PayCount(recovery, id) == 1 && CleanupCount(recovery, id) == 1) && Consumed(recovery).Length == 1,
            "A material Peach keeps the normal owner recovery plus one legal extra beneficiary, one actual use and one cleanup for each cost.");
        Cold(recovery, recoveryRegistry);

        var (spear, spearRegistry) = Create(spearDeck: true); ReachPlay(spear);
        Play(spear, spear.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip)); ReachPlay(spear);
        Require(spear.GetHumanLegalActions().Single(a => a.EquipmentKind == CardKind.ZhangbaSerpentSpear).MaxTargetCount == 1,
            "An ordinary true Zhangba action still selects one target before new issuance.");
        Contest(spear, spearRegistry, 1, take: false);
        var zhangba = spear.GetHumanLegalActions().Single(a => a.EquipmentKind == CardKind.ZhangbaSerpentSpear);
        var spearCosts = zhangba.SelectableCardIds.Where(id => spear.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id)).Take(2).Order().ToArray();
        var spearHp = spear.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        Accept(spear, new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, spearCosts, [1, 3], spear.Revision, P(spear)!.PromptId));
        ReachObserver(spear); Cold(spear, spearRegistry); Continue(spear); ReachPlay(spear);
        Require(spear.CreateSnapshot(0).Players[1].Hp == spearHp[1] - 1 && spear.CreateSnapshot(0).Players[3].Hp == spearHp[3] - 1 &&
            spearCosts.All(id => PayCount(spear, id) == 1 && CleanupCount(spear, id) == 1) &&
            spear.Events.Select(e => e.Payload).OfType<ZhangbaSerpentSpearConvertedEvent>().Single(e => e.IsUse).PhysicalCardIds.Order().SequenceEqual(spearCosts) &&
            Consumed(spear).Length == 1 && !spear.GetHumanLegalActions().Any(a => a.EquipmentKind == CardKind.ZhangbaSerpentSpear),
            "A true Zhangba use keeps its equipment producer, two actual entities, one Slash quota debit and all selected target children.");
        Cold(spear, spearRegistry);

        var (allHand, allHandRegistry) = Create(); ReachPlay(allHand); Contest(allHand, allHandRegistry, 1, take: false);
        var qice = allHand.GetHumanLegalActions().Single(a => a.ProgramSkillId == "classic:qice");
        var allCosts = qice.SelectableCardIds.Order().ToArray(); var receiverCount = allHand.CreateSnapshot(0).Players[1].HandCount;
        Accept(allHand, new UseProgramSkillCommand(0, "classic:qice", qice.ProgramActivationId!, allCosts, [], allHand.Revision, P(allHand)!.PromptId));
        Require(P(allHand)!.IsPrivate && P(allHand)!.Choices.Any(c => c.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo) && c.Targets.SequenceEqual(new[] { 0, 1 })),
            "The mature all-hand choice publishes a precise extra beneficiary while preserving the exact private full-hand cost.");
        RejectUnpublished(allHand); Cold(allHand, allHandRegistry);
        Answer(allHand, c => c.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo) && c.Targets.SequenceEqual(new[] { 0, 1 }));
        ReachObserver(allHand); Cold(allHand, allHandRegistry); Continue(allHand); ReachPlay(allHand);
        Require(allHand.CreateSnapshot(0).Players[0].HandCount == 2 && allHand.CreateSnapshot(0).Players[1].HandCount == receiverCount + 2 &&
            allCosts.All(id => PayCount(allHand, id) == 1 && CleanupCount(allHand, id) == 1) && Consumed(allHand).Length == 1,
            "One all-hand actual DrawTwo use resolves both beneficiaries and cleans every original material once.");
        Cold(allHand, allHandRegistry);
    }

    public static void NextActualUseCounterspellResponseSourceLossNativeAndStrictLoader()
    {
        var (response, responseRegistry) = Create(); ReachPlay(response); Contest(response, responseRegistry, 1, take: false);
        Use(response, "opponent-duel", [1]); Reach(response, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondSlash);
        Answer(response, c => c.Parameters.GetValueOrDefault("response") == "slash"); ReachPlay(response);
        Require(Consumed(response).Length == 0 && response.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Count == 2),
            "An ordinary paid Slash response leaves the next actual Use token unconsumed.");
        Cold(response, responseRegistry);

        var (counter, counterRegistry) = Create(counterDeck: true); ReachPlay(counter); Contest(counter, counterRegistry, 1, take: false);
        Use(counter, "opponent-duel", [1]); Reach(counter, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.Nullification);
        Answer(counter, c => c.Parameters.GetValueOrDefault("response") == "nullification");
        var accepted = counter.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Last(e =>
            e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Nullification).Action;
        var consumed = Consumed(counter).Single();
        Require(consumed.IsNullificationUse && consumed.CardActionId == accepted.ActionId && accepted.ActorSeat == accepted.ProviderSeat &&
            accepted.ResponderSeat == accepted.ActorSeat && accepted.RequesterSeat is null && accepted.ParentActionId is not null &&
            accepted.PhysicalCards.Count == 1 && counter.CardMovements.Count(m => m.CardId == accepted.PhysicalCards[0].CardId &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1,
            "A true counterspell consumes the token using its precise Response action and original owning trick parent, with one actual material payment.");
        Cold(counter, counterRegistry); ReachPlay(counter); Cold(counter, counterRegistry);

        var (lost, lostRegistry) = Create(); ReachPlay(lost); Contest(lost, lostRegistry, 1, take: false);
        var grant = Grants(lost).Single(); Use(lost, "lose-qiaoshui"); ReachPlay(lost);
        Require(!lost.GetHumanLegalActions().Any(a => a.ProgramActivationId == "next-actual-use-target-adjustment") &&
            lost.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == "classic:zongshi-pindian"),
            "Source loss invalidates its unconsumed grant and leaves the independently acquired Zongshi instance intact.");
        Play(lost, lost.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash)); ReachObserver(lost); Cold(lost, lostRegistry); Continue(lost); ReachPlay(lost);
        Require(!Consumed(lost).Any(c => c.GrantProgramFrameId == grant.ProgramFrameId), "An invalid source is not revived by a later ordinary use.");
        Cold(lost, lostRegistry);

        var (native, nativeRegistry) = Create(native: true);
        var nativeOwner = native.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:jy-owner").Seat;
        // StartGame may complete all native turns. Read committed history now.
        var nativeResults = native.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().ToArray();
        for (var step = 0; nativeResults.Length == 0 && step < 80 && native.State.Status != EngineStatus.Completed; step++)
        { Accept(native, new AdvanceOneStepCommand(native.Revision)); nativeResults = native.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().ToArray(); }
        Require(nativeResults.Any(r => r.Result.SourceSeat == nativeOwner && r.Result.SourceWon) && Grants(native).Any(g => g.Source.OwnerSeat == nativeOwner) &&
            native.CardMovements.Any(m => m.From == CardLocation.Hand(nativeOwner) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal),
            "A fixed native source uses its own public-ranked paid contest through normal AI commands; the test never answers an AI prompt manually.");
        Cold(native, nativeRegistry);
        CheckStrictLoader();
    }

    private static PindianResult Contest(GameEngine game, ContentRegistry registry, int opponent, bool take)
    {
        var card = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        var prior = game.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().Count();
        Accept(game, new UseProgramSkillCommand(0, Skill, "contest", [card], [opponent], game.Revision, P(game)!.PromptId));
        Require(game.ResolutionStack.OfType<PindianFrame>().Any() && game.CreateSnapshot(2).PendingDecision is null &&
            game.CreateSnapshot(2).PublicRevealedCards.Count == 0,
            "Uncommitted real contest choices stay private until both actual participants commit.");
        Cold(game, registry);
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-claim"));
        RejectUnpublished(game); Cold(game, registry);
        var result = game.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().Skip(prior).Single().Result;
        Require(result.SourceCardId == card && game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) &&
            m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1,
            "The chosen source hand entity is actually paid once before the optional original-card claim.");
        Answer(game, c => c.Parameters.GetValueOrDefault("action") == "pindian-claim" && c.Parameters.GetValueOrDefault("take") == (take ? "true" : "false"));
        ReachPlay(game);
        var expected = result.SourceWon ? result.OpponentCardId : result.SourceCardId;
        if (take) Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == expected) &&
            game.CardMovements.Count(m => m.CardId == expected && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.pindian.claim") == 1,
            "Zongshi obtains the original correct contest entity rather than a reconstructed card.");
        Cold(game, registry); return result;
    }
    private static NextActualUseTargetAdjustmentGrantedEvent[] Grants(GameEngine game) => game.Events.Select(e => e.Payload).OfType<NextActualUseTargetAdjustmentGrantedEvent>().ToArray();
    private static NextActualUseTargetAdjustmentConsumedEvent[] Consumed(GameEngine game) => game.Events.Select(e => e.Payload).OfType<NextActualUseTargetAdjustmentConsumedEvent>().ToArray();
    private static int PayCount(GameEngine game, int entity) => game.CardMovements.Count(m => m.CardId == entity && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use);
    private static int CleanupCount(GameEngine game, int entity) => game.CardMovements.Count(m => m.CardId == entity && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile);
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void ReachObserver(GameEngine game) => Reach(game, p => p.SkillPrompt?.SkillId == Observer);
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void EndPlay(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
    private static void Use(GameEngine game, string activation, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game, LegalAction action) => Accept(game, new PlayCardCommand(0, action.CardId!.Value,
        action.TargetSeats, game.Revision, P(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 160; step++) { var p = P(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Jian Yong fixture did not reach its actual boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = P(game);
        if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Observer) Continue(game);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
            Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "Four private snapshots, owning material/target children, frozen event payloads, actual payments and return cursors cold-restore exactly.");
    private static void RejectUnpublished(GameEngine game)
    { var p = P(game)!; var before = State(game); Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && before == State(game), "Unpublished input changes no costs, claims, private view or issued source."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool win = true, bool counterDeck = false, bool native = false, bool spearDeck = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(win, counterDeck, native, spearDeck));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode,
            HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = native ? 2 : 8 }, registry);
        Accept(game, new StartGameCommand());
        if (!native) { Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral); Accept(game, new SelectGeneralCommand(0, "fixture:jy-owner", game.Revision, P(game)!.PromptId)); }
        return (game, registry);
    }
    private static void CheckStrictLoader()
    {
        const string effect = "{\"op\":\"grantNextActualUseTargetAdjustment\",\"target\":\"owner\",\"stateId\":\"failed\",\"condition\":{\"kind\":\"pindianWon\"}}";
        var presentation = "{\"schemaVersion\":3,\"skills\":{\"fixture:jy-invalid\":{\"name\":\"严格节点\",\"description\":\"不扩大旧拼点/选目标合同\"}}}";
        foreach (var body in new[] {
            "\"triggers\":[{\"id\":\"invalid\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"optional\":true,\"effects\":[" + effect + "]}]",
            "\"states\":[{\"id\":\"failed\",\"kind\":\"boolean\",\"initialValue\":false,\"visibility\":\"public\",\"resetScope\":\"game\"}],\"activations\":[{\"id\":\"invalid\",\"minCards\":1,\"maxCards\":1,\"sourceZones\":[\"hand\"],\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLivingWithHand\",\"effects\":[{\"op\":\"pindian\",\"target\":\"selectedTarget\",\"amount\":1}," + effect + "]}]",
            "\"activations\":[{\"id\":\"invalid\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[" + effect + "]}]" })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:jy-invalid\",\"revision\":1," + body + "}]}", presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new operation requires its exact paid hand Pindian activation and enabled public per-turn state; it does not widen old producers.");
        }
    }

    private sealed class Fixture(bool win, bool counterDeck, bool native, bool spearDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-jian-yong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString());
            var presentations = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Multi, Observer, "fixture:jy-quiet" }) presentations[id] = id == Observer
                ? new { name = id, description = "真实付款后的目标子链", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                : (object)new { name = id, description = "固定实体和实际程序夹具" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentations }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "真实夹具能力") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:jy-pick-owner", "固定原生主公", "只用公开身份权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            builder.AddSkill(new("fixture:jy-pick-other", "固定其他", "只用公开身份权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            var owner = new List<string> { "fixture:jy-pick-owner", "classic:zongshi-pindian" };
            if (win) owner.Add("classic:tianbian");
            if (!native) owner.AddRange([Driver, Multi, Observer, "classic:qice"]);
            builder.AddGeneral(new("fixture:jy-owner", "界简雍真实机制", "supporter", Skill, "shu", 8, owner.ToArray()));
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:jy-other-{i}", "固定其他角色", "supporter", "fixture:jy-pick-other", "shu", 12, ["fixture:jy-quiet"]));
            builder.AddDeck(new("fixture:jy-deck", "固定真实心牌实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard(spearDeck ? "classic:zhangba-serpent-spear" : counterDeck ? "standard:nullification" : "standard:slash", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "真实简雍付款", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:jy-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:jy-owner", "fixture:jy-other-1", "fixture:jy-other-2", "fixture:jy-other-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:jy-driver","revision":1,"activations":[
      {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
      {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
      {"id":"lose-qiaoshui","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:qiaoshui-current"],"sourceBind":"fixture:jy-pick-owner"}]}]},
     {"id":"fixture:jy-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
     {"id":"fixture:jy-committed","revision":1,"triggers":[{"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","arrowBarrage","peach","drawTwo"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]},{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash","arrowBarrage","peach","drawTwo"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:jy-multi","revision":1,"viewAs":[
      {"id":"two-slash","inputKinds":["slash"],"inputSuits":[],"allowSameKind":true,"outputKind":"slash","forPlay":true,"forResponse":false,"inputCount":2,"extendedUse":true,"sourceZones":["hand","equipment"]},
      {"id":"two-arrow","inputKinds":["slash"],"inputSuits":[],"outputKind":"arrowBarrage","forPlay":true,"forResponse":false,"inputCount":2,"sameSuit":true,"extendedUse":true,"sourceZones":["hand"]},
      {"id":"two-peach","inputKinds":["slash"],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"inputCount":2,"extendedUse":true,"sourceZones":["hand","equipment"]}],"activations":[
      {"id":"double-slash","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-slash","outputKind":"slash"}]},
      {"id":"double-arrow","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"two-arrow","outputKind":"arrowBarrage"}]},
      {"id":"double-peach","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"two-peach","outputKind":"peach"}]}]}]}
    """;
}
