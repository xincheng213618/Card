using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryXuHuangChecks
{
    private const string Duanliang = "boundary:duanliang-current";
    private const string Jiezi = "boundary:jiezi-current";
    private const string Driver = "fixture:xh-driver";
    private const string Skip = "fixture:xh-skip";
    private const string Start = "fixture:xh-start";
    private const string End = "fixture:xh-end";
    private const string Gain = "fixture:xh-gain";
    private const string Hp = "fixture:xh-hp";
    private const string Mode = "identity:classic-boundary-xu-huang-fixture";
    private static PlayerMarkerKind Zi => Enum.Parse<PlayerMarkerKind>("Zi");

    public static void DuanliangDistanceTracksActualWholeTurnDamageAndPaidEquipment()
    {
        var (game, registry) = Create(silver: true); ReachPlay(game);
        Require(game.GetCombatDistance(0, 2) == 2 && Supplies(game, 2).Count > 0,
            "Before actual damage, black HE materials can create a real distance-two SupplyShortage action.");
        Use(game, "hp-only"); ReachPlay(game);
        Require(Supplies(game, 2).Count > 0 && !game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0),
            "A real HP-loss command does not falsely count as damage dealt and leaves no-distance qualification active.");
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip); Play(game, equip); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var conversion = Supplies(game, 1).Single(a => a.CardId == armor);
        Cold(game, registry); Play(game, conversion); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Hp);
        var paid = game.ResolutionStack.OfType<CardUseFrame>().Last();
        Require(paid.CardKind == CardKind.SupplyShortage && paid.PhysicalCardIds is { } physical && physical.SequenceEqual([armor]) &&
            paid.Action!.PhysicalCards is [var material] && material.CardId == armor && material.From == CardLocation.Equipment(0) &&
            paid.Action.ConversionChain.Any(c => c.SkillId == Duanliang) && game.State.Players[0].Hp > 3,
            "The real equipment conversion freezes its original material and exact source before its loss-of-SilverLion recovery child.");
        Private(game, 0); Cold(game, registry); Continue(game); ReachPlay(game);
        Require(game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
            game.CreateSnapshot(0).Players[1].Judgment.Any(c => c.Id == armor), "The paid use returns once to its actual delayed-trick placement.");
        Use(game, "damage", targets: [3]); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.Amount == 1) &&
            Supplies(game, 2).Count == 0 && Supplies(game, 3).Count > 0,
            "After real damage, the new policy withdraws distance two and preserves ordinary distance one; it does not inherit classic Duanliang's +1.");
        var turn = game.State.TurnNumber; EndPlay(game); ReachPlay(game);
        Require(game.State.TurnNumber > turn && Supplies(game, 2).Count > 0 &&
            game.Events.Select(e => e.Payload).OfType<DrawPhaseSkippedEvent>().Any(e => e.OwnerSeat == 1 && !e.IsExtra) &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Jiezi && e.OwnerSeat == 0 && e.Window == SkillProgramTriggerWindow.DrawPhaseSkipped && !e.Activated),
            "The next actual turn restores no-distance from the new public damage boundary, without resetting a private Boolean source sidecar."); Cold(game, registry);
    }

    public static void LeastHandMarkerWaitsForEndObserversThenRunsRealExtraDraw()
    {
        var (legacy, legacyRegistry) = Create(newCapability: false); StartSkippedDrawWithoutJiezi(legacy); ReachPlay(legacy);
        Require(!legacy.Events.Select(e => e.Payload).Any(e => e is DrawPhaseSkippedEvent or ActualDrawPhaseCompletedEvent or ExtraDrawPhaseStartedEvent) &&
            !legacy.ResolutionStack.Any(f => f is DrawPhaseObligationFrame),
            "A registry without the new skipped-draw capability keeps the existing synchronous skip flow, with no added facts or owning frame."); Cold(legacy, legacyRegistry);
        var (game, registry) = Create(normalGainRecovery: true); StartSkippedDraw(game);
        var skipped = game.Events.Select(e => e.Payload).OfType<DrawPhaseSkippedEvent>().Last();
        ActivateJiezi(game); Select(game, 0); ReachPlay(game);
        Require(Marker(game, 0) == 1 && game.State.Players[0].HandCount == 4 &&
            Decisions(game).All(view => view.Players[0].Markers!.Single(m => m.Kind == Zi).Count == 1) &&
            Branches(game).Last() is { GrantedMarker: true, TargetSeat: 0 },
            "All minimum ties qualify, including the owner; the skipped real draw gives a public marker and does not draw immediately.");
        Use(game, "hp-only"); ReachPlay(game);
        var woundedHp = game.State.Players[0].Hp;
        Require(woundedHp == game.State.Players[0].MaxHp - 1 && Marker(game, 0) == 1,
            "The existing real HP-loss driver makes normal gain recovery observable without changing the issued marker or drawing cards.");
        var hand = game.State.Players[0].HandCount;
        var endedBeforeNormal = game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == End && e.OwnerSeat == 0);
        EndPlay(game);
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Gain && !Extra(game));
        var normal = game.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single();
        var normalMovement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        var normalGain = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain);
        var normalCandidate = normalMovement.Candidates[normalMovement.CandidateIndex];
        var normalDrawMoves = game.CardMovements.Where(m => m.TurnNumber == normal.ActualTurnNumber &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw).ToArray();
        var normalMovementIds = normalDrawMoves.Select(m => m.Sequence).ToArray();
        var normalCardIds = normalDrawMoves.Select(m => m.CardId).ToArray();
        Require(!normal.IsExtra && normal.ParentFrameId is null && normal.OwnerSeat == 0 && normal.ActualTurnNumber == game.State.TurnNumber &&
            normal.Stage == DrawPhaseObligationStage.EndObservers && normal.ActiveWindowFrameId is null && normal.DrawCount == 2 &&
            normal.InheritedMovementBatchIds is { Count: 2 } inherited && inherited.Distinct().Count() == 2 &&
            inherited.Contains(normalMovement.Batch.Id) && normalMovement.Batch.ParentFrameId is null &&
            normalMovement.Batch.AwaitingProgramFrameId is null && normalMovement.ResumeProgramFrameId is null &&
            normalMovement.ResumeDrawPhaseObligationFrameId == normal.Id && normalMovement.Batch.Id == normalMovement.Id &&
            normalDrawMoves.Length == 2 && normalMovement.Batch.Movements.Count == 1 && normalMovement.Batch.Movements.All(m =>
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw && game.CardMovements.Contains(m)) &&
            normalGain.TriggerId == "normal-gained" && normalGain.WindowContext is { } normalContext && normalContext.ParentFrameId == normalMovement.Id &&
            normalContext.MovementBatch?.Id == normalMovement.Batch.Id && normalGain.OwnerSeat == normalCandidate.OwnerSeat &&
            normalGain.SkillId == normalCandidate.SkillId && normalGain.TriggerId == normalCandidate.BindingId &&
            normalGain.SkillInstanceId == normalCandidate.SkillInstanceId && normalGain.GameplayHash == normalCandidate.GameplayHash &&
            normalContext.OccurrenceIndex == normalCandidate.OccurrenceIndex &&
            Marker(game, 0) == 1 && game.State.Players[0].HandCount == hand + 2 && game.State.Players[0].Hp == woundedHp &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == End && e.OwnerSeat == 0) == endedBeforeNormal &&
            !game.Events.Select(e => e.Payload).OfType<ProgramMarkerExtraDrawConsumedEvent>().Any(e => e.TargetSeat == 0) &&
            !game.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Any(),
            "Normal Draw's two committed single-card rootless batches are inherited by their exact actual draw frame; the real gain candidate pauses before DrawEnded and marker consumption.");
        Private(game, 0); Cold(game, registry); Continue(game);
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Hp && !Extra(game));
        var normalHp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(normalHp.Continuation == PostEventContinuation.Program && normalHp.ResumeFrameId == normalGain.Id &&
            normalHp.Change.ParentFrameId == normalGain.Id && normalHp.Change.Kind == HpChangeKind.Recovery &&
            normalHp.Change.TargetSeat == 0 && normalHp.Change.SourceSeat == 0 && normalHp.Change.Amount == 1 &&
            normalHp.Change.HpBefore == woundedHp && normalHp.Change.HpAfter == woundedHp + 1 && game.State.Players[0].Hp == woundedHp + 1 &&
            game.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single() is { Stage: DrawPhaseObligationStage.EndObservers, ActiveWindowFrameId: null } stillNormal &&
            stillNormal.Id == normal.Id && game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single().ResumeDrawPhaseObligationFrameId == normal.Id &&
            Marker(game, 0) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == End && e.OwnerSeat == 0) == endedBeforeNormal &&
            !game.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Any(),
            "Recovery really pauses below the existing HP observer and returns to the gain Program, while its inherited normal batch keeps the draw owner and marker untouched.");
        Private(game, 0); Cold(game, registry); Continue(game);
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == End);
        Require(Marker(game, 0) == 1 && game.State.Players[0].HandCount == hand + 2 &&
            !game.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Any(),
            "The marker survives until all actual normal DrawEnded observers return; normal gain precedes the inserted phase.");
        Require(game.State.Players[0].Hp == woundedHp + 1 && !game.ResolutionStack.Any(f => f.Id == normalGain.Id || f.Id == normalMovement.Id) &&
            game.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single() is { Stage: DrawPhaseObligationStage.ConsumeMarker, ActiveWindowFrameId: { } endedId } normalEnding &&
            normalEnding.Id == normal.Id && game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single() is { } endedWindow &&
            endedWindow.Id == endedId && endedWindow.ResumeDrawPhaseObligationFrameId == normal.Id &&
            normalMovementIds.All(id => game.CardMovements.Count(m => m.Sequence == id) == 1) &&
            normalCardIds.All(card => game.CardMovements.Count(m => m.CardId == card && m.TurnNumber == normal.ActualTurnNumber &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 1) &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Gain && e.OwnerSeat == 0 && e.BindingId == "normal-gained" && e.Activated && e.Completed) == 1,
            "Only the completed gain and HP child return opens the exact normal ending window; the inherited two-card draw ledger and recovery execute once.");
        Cold(game, registry); Continue(game);
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Start && Extra(game));
        var extra = game.ResolutionStack.OfType<DrawPhaseObligationFrame>().Last();
        var endedParent = game.ResolutionStack.OfType<DrawPhaseObligationFrame>().First();
        Require(Marker(game, 0) == 0 && extra.ParentFrameId == endedParent.Id &&
            endedParent.Stage == DrawPhaseObligationStage.Finished && extra.ActiveWindowFrameId is { } childId &&
            game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Id == childId && w.ResumeDrawPhaseObligationFrameId == extra.Id) &&
            skipped.ActualTurnNumber < extra.ActualTurnNumber,
            "Marker consumption precedes a distinct real extra DrawStarting child, held by its exact completed phase owner.");
        Private(game, 0); Cold(game, registry); Continue(game);
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Gain && Extra(game));
        extra = game.ResolutionStack.OfType<DrawPhaseObligationFrame>().Last();
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        Require(extra.Stage == DrawPhaseObligationStage.DrawChildren && extra.DrawCount == 2 &&
            movement.ResumeDrawPhaseObligationFrameId == extra.Id && movement.Batch.ParentFrameId == extra.Id &&
            movement.Batch.Movements.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.extra-draw-phase.draw"),
            "Extra draw has actual gain children with a frozen producer; its ended window cannot race the card movements.");
        Cold(game, registry); Continue(game); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == End && Extra(game));
        Require(Marker(game, 0) == 0 && game.State.Players[0].HandCount == hand + 4,
            "The inserted draw has its own actual DrawEnded window and does not recreate the spent marker.");
        Cold(game, registry); Continue(game); ReachPlay(game);
        Require(game.State.Players[0].HandCount == hand + 4 &&
            game.Events.Select(e => e.Payload).OfType<ProgramMarkerExtraDrawConsumedEvent>().Count(e => e.TargetSeat == 0) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Count(e => e.OwnerSeat == 0) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ActualDrawPhaseCompletedEvent>().Count(e => e.OwnerSeat == 0 && e.IsExtra && !e.Skipped) == 1,
            "The child returns to the original phase and enters Play once, after one consumption and one extra draw."); Cold(game, registry);
    }

    public static void ExistingMarkerOrNonMinimumUsesOneAwaitedDrawBranch()
    {
        foreach (var existing in new[] { false, true })
        {
            var (game, registry) = Create(existingZi: existing, skipCardCost: !existing);
            Reach(game, p => p.PlayerSeat == 0 && HasBinding(p, Skip, "skip-draw")); Activate(game, Skip, "skip-draw");
            if (!existing)
            {
                Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-own-card-discard"));
                var cost = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); Private(game, 0); Cold(game, registry);
                Answer(game, c => c.Cards.SequenceEqual([cost])); ReachJiezi(game);
                Require(game.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
                    "The real private skip cost is paid once before the skipped-phase beneficiary window, including cold command replay.");
            }
            else ReachJiezi(game);
            ActivateJiezi(game);
            var target = existing ? 0 : 1;
            if (!existing)
                Require(game.State.Players[target].HandCount == 4 && game.State.Players[0].HandCount == 3,
                    "The initial real one-card skip cost creates a strict public nonminimum recipient without HOST state writes.");
            var count = game.State.Players[target].HandCount; var marker = Marker(game, target); Cold(game, registry); Select(game, target);
            Reach(game, p => p.SkillPrompt?.SkillId == Gain);
            var owner = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jiezi);
            var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
            Require(owner.LeastHandMarkerDraw is { TargetSeat: var receiptTarget } && receiptTarget == target &&
                game.State.Players[target].HandCount == count + 1 && Marker(game, target) == marker &&
                Branches(game).Last() is { GrantedMarker: false } && movement.ResumeProgramFrameId == owner.Id &&
                movement.Batch.ParentFrameId == owner.Id && movement.Batch.Movements.All(m => m.To == CardLocation.Hand(target)),
                "Existing markers and nonminimum hands use the same exact one-card draw receipt and actual movement child, with no marker insertion.");
            Cold(game, registry);
            if (Prompt(game)!.PlayerSeat == 0) Continue(game); else Accept(game, new AdvanceOneStepCommand(game.Revision));
            ReachPlay(game);
            Require(game.State.Players[target].HandCount == count + 1 && Marker(game, target) == marker && Branches(game).Count == 1,
                "A cold child return cannot redraw or re-evaluate the already resolved least-hand branch."); Cold(game, registry);
        }
    }

    public static void SkippedExtraDrawAndIssuedSourceDeathKeepExactNativeReturns()
    {
        var (game, registry) = Create(qiaobian: true); StartSkippedDraw(game); ActivateJiezi(game); Select(game, 0); ReachPlay(game); EndPlay(game);
        Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == End); Continue(game);
        Reach(game, p => p.PlayerSeat == 0 && Extra(game) && HasBinding(p, "boundary:qiaobian", "skip-draw-and-take-hands"));
        var normalTurn = game.State.TurnNumber; var hand = game.State.Players[0].HandCount;
        var endedBeforeExtra = game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == End && e.OwnerSeat == 0);
        Activate(game, "boundary:qiaobian", "skip-draw-and-take-hands");
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-marker"));
        Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "alternative-phase-cost-marker");
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets"));
        Answer(game, c => c.Targets.Count == 0); ReachJiezi(game);
        Require(game.Events.Select(e => e.Payload).OfType<DrawPhaseSkippedEvent>().Last() is { IsExtra: true } &&
            game.ResolutionStack.OfType<DrawPhaseObligationFrame>().Count() == 2 && Marker(game, 0) == 0,
            "A paid actual Qiaobian substitution skips only the inserted draw and exposes one real skipped-phase observer.");
        Cold(game, registry); ActivateJiezi(game); Select(game, 0); ReachPlay(game);
        Require(game.State.TurnNumber == normalTurn && game.State.Players[0].HandCount == hand &&
            Marker(game, 0) == 1 && game.Events.Select(e => e.Payload).OfType<ProgramMarkerExtraDrawConsumedEvent>().Count(e => e.TargetSeat == 0) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Count(e => e.OwnerSeat == 0) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ActualDrawPhaseCompletedEvent>().Any(e => e.IsExtra && e.Skipped) &&
            game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == End && e.OwnerSeat == 0) == endedBeforeExtra,
            "The inserted skip returns to normal Play without leaking flags or consuming a newly issued marker through its old parent's ended boundary."); Cold(game, registry);
        EndPlay(game); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == End && !Extra(game));
        Require(game.State.TurnNumber > normalTurn && Marker(game, 0) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Count(e => e.OwnerSeat == 0) == 1,
            "A marker issued after the old ended boundary stays held through the next actual normal draw and its ending observers."); Cold(game, registry);
        Continue(game); Reach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Start && Extra(game));
        Require(Marker(game, 0) == 0 && game.Events.Select(e => e.Payload).OfType<ProgramMarkerExtraDrawConsumedEvent>().Count(e => e.TargetSeat == 0) == 2,
            "Only the next actual DrawEnded consumes the new marker and starts its distinct extra draw."); Cold(game, registry);

        var (native, nativeRegistry) = Create(renegade: true); ReachPlay(native);
        Use(native, "empty-target", targets: [1]); ReachPlay(native);
        Require(native.State.Players[1].HandCount == 0, "The chosen recipient's minimum comes from real AI-owned discard payments.");
        Use(native, "extra-turn"); ReachPlay(native); EndPlay(native);
        Reach(native, p => p.PlayerSeat == 0 && HasBinding(p, Skip, "skip-draw")); Activate(native, Skip, "skip-draw");
        ReachJiezi(native); ActivateJiezi(native); Select(native, 1); ReachPlay(native);
        Require(Marker(native, 1) == 1 && Branches(native).Last() is { TargetSeat: 1, GrantedMarker: true },
            "A real skipped extra-turn draw signs the minimum recipient's deferred marker under its actual source instance.");
        Cold(native, nativeRegistry); Use(native, "die");
        ReachUntil(native, () => !native.State.Players[0].IsAlive);
        Require(native.State.Winner == Winner.None && Marker(native, 1) == 1,
            "Issued draw obligations and public marker attribution survive actual source death in a three-Renegade mode."); Cold(native, nativeRegistry);
        ReachUntil(native, () => native.Events.Select(e => e.Payload).OfType<ExtraDrawPhaseStartedEvent>().Any(e => e.OwnerSeat == 1 && e.SourceSeat == 0));
        Require(Marker(native, 1) == 0 && native.ResolutionStack.OfType<DrawPhaseObligationFrame>().Any(f => f.OwnerSeat == 1 && f.IsExtra) &&
            !native.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.ActorSeat == 1),
            "Native AI consumes the issued marker and enters its actual extra draw without an artificial human response for the dead provider or AI recipient."); Cold(native, nativeRegistry);
    }

    private static IReadOnlyList<LegalAction> Supplies(GameEngine g, int seat) => g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.SupplyShortage && a.TargetSeat == seat).ToArray();
    private static IReadOnlyList<ProgramLeastHandMarkerOrDrawEvent> Branches(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramLeastHandMarkerOrDrawEvent>().Where(e => e.OwnerSeat == 0).ToArray();
    private static int Marker(GameEngine g, int seat) => g.State.Players[seat].Markers?.SingleOrDefault(m => m.Kind == Zi)?.Count ?? 0;
    private static bool Extra(GameEngine g) => g.ResolutionStack.OfType<DrawPhaseObligationFrame>().LastOrDefault()?.IsExtra == true;
    private static bool HasBinding(PendingDecision p, string skill, string binding) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static GameSnapshot[] Decisions(GameEngine g) => Enumerable.Range(0, 4).Select(viewer => g.CreateSnapshot(viewer)).ToArray();
    private static PendingDecision? Prompt(GameEngine g) => Decisions(g).Select(v => v.PendingDecision).FirstOrDefault(p => p is not null);
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void ReachJiezi(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && HasBinding(p, Jiezi, "benefit-after-actual-skipped-draw"));
    private static void StartSkippedDraw(GameEngine g)
    { Reach(g, p => p.PlayerSeat == 0 && HasBinding(p, Skip, "skip-draw")); Activate(g, Skip, "skip-draw"); ReachJiezi(g); }
    private static void StartSkippedDrawWithoutJiezi(GameEngine g)
    { Reach(g, p => p.PlayerSeat == 0 && HasBinding(p, Skip, "skip-draw")); Activate(g, Skip, "skip-draw"); }
    private static void ActivateJiezi(GameEngine g) => Activate(g, Jiezi, "benefit-after-actual-skipped-draw");
    private static void Activate(GameEngine g, string skill, string binding) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Select(GameEngine g, int seat)
    { Reach(g, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets")); Answer(g, c => c.Targets.SequenceEqual([seat])); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Play(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, Prompt(g)!.PromptId, action.PlayedCardKind)
    { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, Prompt(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => ReachUntil(g, () => Prompt(g) is { } p && predicate(p));
    private static void ReachUntil(GameEngine g, Func<bool> predicate)
    { for (var i = 0; i < 280; i++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed Xu Huang fixture missed its real boundary: " + JsonSerializer.Serialize(Prompt(g))); }
    private static void Advance(GameEngine g)
    {
        var p = Prompt(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-own-card-discard")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "choose-own-card-discard");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Start or End or Gain or Hp) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Private(GameEngine g, int owner)
    { Require(Prompt(g) is { IsPrivate: true }, "The published skill choice is private to its actual chooser."); foreach (var seat in Enumerable.Range(0, 4).Where(s => s != owner)) Require(g.CreateSnapshot(seat).PendingDecision is null && g.CreateSnapshot(seat).Players[owner].Hand.Count == 0, "Other viewers do not receive private hand entities or choices."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), g.CardMovements, Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)),
        "Cold replay retains four private projections, exact stage/observer/payment ownership, public marker policies and actual materials.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool silver = false, bool existingZi = false, bool qiaobian = false, bool renegade = false, bool skipCardCost = false, bool newCapability = true, bool normalGainRecovery = false)
    {
        var packages = new List<IGameContentPackage> { new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage() };
        if (newCapability) packages.Add(new StandardClassicGeneralPackage());
        packages.Add(new Fixture(silver, existingZi, qiaobian, skipCardCost, newCapability, normalGainRecovery));
        var registry = ContentRegistry.Build(packages.ToArray());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = renegade ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:xh-owner", game.Revision, Prompt(game)!.PromptId)); return (game, registry);
    }
    private sealed class Fixture(bool silver, bool existingZi, bool qiaobian, bool skipCardCost, bool newCapability, bool normalGainRecovery) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-xu-huang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var skipCost = skipCardCost ? "{\"op\":\"chooseOwnCardDiscard\",\"target\":\"owner\",\"zones\":[\"hand\"]}," : "";
            var normalGain = normalGainRecovery ? $$"""
                ,{"id":"normal-gained","window":"cardsGained","subject":"owner","optional":false,"usageScope":"turn","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":[{{JsonSerializer.Serialize(CardMoveReasons.Draw.Value)}}],"effects":[{"op":"chooseOption","target":"owner","resultBind":"normal-seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}
                """ : "";
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"hp-only","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"empty-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":64}]},
                {"id":"extra-turn","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
                {"id":"{{Skip}}","revision":1,"triggers":[{"id":"skip-draw","window":"turnStartBeforeNormalFlow","subject":"owner","priority":300,"optional":true,"effects":[{{skipCost}}{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]},
                {"id":"fixture:xh-quiet","revision":1,"triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash","silverLion"],"actionTypes":["use"]}]}]},
                {"id":"fixture:xh-initial-zi","revision":1,"triggers":[{"id":"one-marker","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"owner"},"marker":"zi","amount":1}]}]},
                {"id":"{{Start}}","revision":1,"triggers":[{"id":"starting","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{End}}","revision":1,"triggers":[{"id":"ended","window":"drawPhaseEnded","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.extra-draw-phase.draw","program.least-hand-marker.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}{{normalGain}}]},
                {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令准备", description = "真实伤害/HP损失/AI弃牌/额外回合" }, [Skip] = new { name = "实际跳过摸牌", description = "旧通用阶段替代" },
                    ["fixture:xh-quiet"] = new { name = "安静回合", description = "禁止固定牌堆的AI用牌" }, ["fixture:xh-initial-zi"] = new { name = "既有标记", description = "真实游戏开始独立标记" },
                    [Start] = Label("实际摸牌开始"), [End] = Label("实际摸牌结束"), [Gain] = Label("实际获得牌"), [Hp] = Label("实际回复") } }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "程序观察夹具") { Program = catalog.Programs[id] });
            if (!newCapability) { builder.AddSkill(new(Duanliang, "旧兼容夹具主技能", "无程序")); builder.AddSkill(new(Jiezi, "旧兼容夹具副技能", "无程序")); }
            builder.AddSkill(new("fixture:xh-selection", "固定选将", "无运行程序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var ownerSkills = new List<string> { Jiezi, Driver, Skip, Start, End, Gain, Hp };
            if (existingZi) ownerSkills.Add("fixture:xh-initial-zi"); if (qiaobian) ownerSkills.Add("boundary:qiaobian");
            builder.AddGeneral(new("fixture:xh-owner", "当前断粮真实程序", "supporter", Duanliang, "wei", 4, ownerSkills.ToArray()) { InitialHp = silver ? 3 : 4 });
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:xh-target-{i}", "真实目标", "supporter", "fixture:xh-selection", "shu", 8,
                ["fixture:xh-quiet", Gain, Start, End]) { InitialHp = 8 });
            builder.AddDeck(new("fixture:xh-deck", "固定实际黑实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(silver ? "classic:silver-lion" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实断粮截辎", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:xh-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xh-owner", "fixture:xh-target-1", "fixture:xh-target-2", "fixture:xh-target-3"]));
        }
        private static object Label(string name) => new { name, description = "实际阶段/移牌/回复孩子观察", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
