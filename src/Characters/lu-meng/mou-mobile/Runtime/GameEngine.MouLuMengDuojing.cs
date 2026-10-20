using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouDuojingTakeReason = "skill-program.mou-mobile:duojing.obtain";
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29202, SkillProgramEffectOp.ResolveMouLuMengDuojing)]
    private bool CanOfferMouDuojing(ProgramTriggerLegalityContext c) => c.SkillId == MouDuojing && MouLuMengComposition.IsDuojing(c.Program) &&
        c.Context is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } &&
        identity.ActorSeat == c.OwnerSeat && c.Owner.IsAlive && c.Owner.Armor > 0 &&
        LifecycleCardUse(identity.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } use &&
        action.ActionId == identity.CardActionId && action.ActorSeat == c.OwnerSeat && use.SourceSeat == c.OwnerSeat && IsSlashCard(use.CardKind) &&
        use.TargetSeats.Any(seat => seat != c.OwnerSeat && _players[seat].IsAlive) && CurrentActualDiscardRecoveryPhase() is not null &&
        !ProgramEventHistory<MouLuMengDuojingStartedEvent>().Any(e => e.CardUseId == use.Id && e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == MouDuojing);
    private CardUseFrame? MouDuojingUse(ProgramSkillFrame f)
    {
        if (f.SkillId != MouDuojing || f.TriggerId != "after-slash-targets" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } context ||
            context.SourceSeat != f.OwnerSeat || identity.ActorSeat != f.OwnerSeat ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !MouLuMengComposition.IsDuojing(p) ||
            LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.ActionId != identity.CardActionId || action.ActorSeat != f.OwnerSeat || use.SourceSeat != f.OwnerSeat ||
            !IsSlashCard(use.CardKind) || action.EffectiveKind != use.CardKind ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.ParentFrameId != use.Id || window.Continuation != ProgramCardContinuation.Slash || GetCardActionWindow(window) != context.Window ||
            window.Action.ActionId != action.ActionId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex]))) return null;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return i > 1 && _resolutionStack[i - 1].Id == window.Id && _resolutionStack[i - 2].Id == use.Id ? use : null;
    }
    private bool ValidMouDuojing(ProgramSkillFrame f)
    {
        if (f.MouLuMengDuojing is not { } r || MouDuojingUse(f) is not { } use || r.Source != MouLuMengSource(f) ||
            r.GameplayHash != f.GameplayHash || r.WindowId != f.WindowContext!.ParentFrameId || r.CardUseId != use.Id || r.ActionId != use.Action!.ActionId ||
            !IsRecordedActualDiscardRecoveryPhase(r.Phase) || r.Targets.Count == 0 || r.Targets.Distinct().Count() != r.Targets.Count ||
            r.Targets.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat || !use.TargetSeats.Contains(s)) ||
            r.TargetIndex < 0 || r.TargetIndex > r.Targets.Count || !Enum.IsDefined(r.Stage) ||
            ProgramEventHistory<MouLuMengDuojingStartedEvent>().Count(e => e == new MouLuMengDuojingStartedEvent(f.Id, r.Source,
                r.GameplayHash, r.WindowId, r.CardUseId, r.ActionId, r.Phase, r.Targets.Count)) != 1) return false;
        if (r.Stage == MouLuMengDuojingStage.Complete) return r.TargetIndex == r.Targets.Count &&
            ProgramEventHistory<MouLuMengDuojingCompletedEvent>().Count(e => e.FrameId == f.Id) == 1;
        if (r.TargetIndex >= r.Targets.Count) return false;
        var target = r.Targets[r.TargetIndex];
        if (ProgramEventHistory<MouLuMengDuojingOfferedEvent>().Count(e => e.FrameId == f.Id && e.TargetIndex == r.TargetIndex && e.TargetSeat == target) != 1) return false;
        if (r.Stage == MouLuMengDuojingStage.Choosing) return r.TakenCardId is null && r.MovementSequence is null &&
            !ProgramEventHistory<MouLuMengDuojingArmorPaidEvent>().Any(e => e.FrameId == f.Id && e.TargetIndex == r.TargetIndex);
        if (ProgramEventHistory<MouLuMengDuojingArmorPaidEvent>().Where(e => e.FrameId == f.Id && e.TargetIndex == r.TargetIndex).ToArray() is not [var paid] ||
            paid.TargetSeat != target || paid.CardUseId != use.Id || paid.ActionId != r.ActionId || paid.ArmorBefore < 1 || paid.ArmorAfter != paid.ArmorBefore - 1 ||
            !use.IgnoresArmor || use.CardAttack?.IgnoresArmor != true || use.PreparedTargetAttacks?.Any(a => !a.IgnoresArmor) == true) return false;
        if (r.Stage == MouLuMengDuojingStage.Taking) return r.TakenCardId is null && r.MovementSequence is null;
        return ProgramEventHistory<MouLuMengDuojingTakenEvent>().Count(e => e == new MouLuMengDuojingTakenEvent(f.Id, r.TargetIndex, target, r.TakenCardId, r.MovementSequence)) == 1 &&
            (r.TakenCardId is null && r.MovementSequence is null || r.TakenCardId is { } card && r.MovementSequence is { } sequence &&
                _cardMovements.Count(m => m.Sequence == sequence && m.CardId == card && m.From == CardLocation.Hand(target) &&
                    m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == MouDuojingTakeReason) == 1);
    }
    private SkillProgramStepOutcome BeginMouDuojing(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (MouDuojingUse(f) is not { } use || f.MouLuMengDuojing is not null ||
            ProgramEventHistory<MouLuMengDuojingStartedEvent>().Any(e => e.CardUseId == use.Id && e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == MouDuojing))
            throw new InvalidOperationException("Duojing needs one exact finalized actual Slash Use from its actor.");
        var targets = Array.AsReadOnly(use.TargetSeats.Where(s => s != f.OwnerSeat && _players[s].IsAlive).Distinct().ToArray());
        if (targets.Count == 0) return SkillProgramStepOutcome.Continue;
        var r = new MouLuMengDuojingReceipt(MouLuMengSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId,
            use.Id, use.Action!.ActionId, MouLuMengPhase(), targets);
        ReplaceRuntimeTop(f = f with { MouLuMengDuojing = r });
        AdvanceEventRulesAndQueueFact(new MouLuMengDuojingStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, use.Id, r.ActionId, r.Phase, targets.Count));
        OfferMouDuojingTarget(f.Id);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void OfferMouDuojingTarget(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouLuMengDuojing!;
        if (r.Stage != MouLuMengDuojingStage.Choosing) throw new InvalidOperationException("Duojing cannot offer a paid target again.");
        while (r.TargetIndex < r.Targets.Count && !_players[r.Targets[r.TargetIndex]].IsAlive)
        { r = r with { TargetIndex = r.TargetIndex + 1 }; ReplaceRuntimeTop(f = f with { MouLuMengDuojing = r }); }
        if (r.TargetIndex >= r.Targets.Count || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || _players[f.OwnerSeat].Armor <= 0 ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CompleteMouDuojing(f); return; }
        if (!ProgramEventHistory<MouLuMengDuojingOfferedEvent>().Any(e => e.FrameId == id && e.TargetIndex == r.TargetIndex))
            AdvanceEventRulesAndQueueFact(new MouLuMengDuojingOfferedEvent(id, r.TargetIndex, r.Targets[r.TargetIndex]));
        PublishMouDuojingChoice(f, MouDuojingChoices(f), "夺荆：是否失去1点护甲，令此杀无视防具并获得目标一张手牌？");
    }
    private IReadOnlyList<PromptChoice> MouDuojingChoices(ProgramSkillFrame f)
    {
        var r = f.MouLuMengDuojing!; var target = r.Targets[r.TargetIndex];
        return Array.AsReadOnly(new[] { "pay", "skip" }.Select(option => new PromptChoice(new($"mou-duojing.{f.Id}.{r.TargetIndex}.{option}"),
            option == "pay" ? "失去1点护甲发动夺荆" : "不发动夺荆", [], [target], new Dictionary<string, string>
            { ["program-action"] = "mou-lu-meng-duojing", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["target-index"] = r.TargetIndex.ToString(CultureInfo.InvariantCulture), ["option-id"] = option })).ToArray());
    }
    private IReadOnlyList<PromptChoice> MouDuojingTakeChoices(ProgramSkillFrame f)
    {
        var r = f.MouLuMengDuojing!; var target = r.Targets[r.TargetIndex];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, target, [CardZoneKind.Hand], OwnedCardMoveIntent.Transfer,
            destination: SkillProgramCardDestination.OwnerHand).Select(c => c with
            { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-lu-meng-duojing-take" } }).ToArray());
    }
    private void PublishMouDuojingChoice(ProgramSkillFrame f, IReadOnlyList<PromptChoice> choices, string message)
    {
        var r = f.MouLuMengDuojing!; var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, message, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            [r.Targets[r.TargetIndex]], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.Targets[r.TargetIndex], Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("mou-lu-meng-duojing")]
    private void AnswerMouDuojing(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!ValidMouDuojing(f) || f.MouLuMengDuojing is not { Stage: MouLuMengDuojingStage.Choosing } r ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], MouDuojingChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Duojing can only pay its canonical current finalized target once.");
        ClearPendingDecision();
        var target = r.Targets[r.TargetIndex];
        if (selected.Parameters.GetValueOrDefault("option-id") == "skip" || !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || _players[f.OwnerSeat].Armor < 1)
        {
            AdvanceEventRulesAndQueueFact(new MouLuMengDuojingSkippedEvent(f.Id, r.TargetIndex, target));
            ReplaceRuntimeTop(f with { MouLuMengDuojing = r with { TargetIndex = r.TargetIndex + 1 } });
            OfferMouDuojingTarget(f.Id); return;
        }
        var before = _players[f.OwnerSeat].Armor;
        ReplaceRuntimeTop(f = f with { MouLuMengDuojing = r with { Stage = MouLuMengDuojingStage.Taking } });
        _players[f.OwnerSeat].Armor = before - 1;
        UpdateLifecycleCardUse(r.CardUseId, use => use with
        {
            IgnoresArmor = true,
            CardAttack = use.CardAttack! with { IgnoresArmor = true },
            PreparedTargetAttacks = use.PreparedTargetAttacks is null ? null : Array.AsReadOnly(use.PreparedTargetAttacks.Select(a => a with { IgnoresArmor = true }).ToArray())
        });
        AdvanceEventRulesAndQueueFact(new MouLuMengDuojingArmorPaidEvent(f.Id, r.TargetIndex, target, r.CardUseId, r.ActionId, before, before - 1));
        var choices = MouDuojingTakeChoices(f);
        if (choices.Count == 0) { FinishMouDuojingTake(f, null, null); ContinueMouDuojing(f.Id); return; }
        PublishMouDuojingChoice(f, choices, "夺荆：选择目标的一张手牌获得。");
    }
    [ProgramAnswer("mou-lu-meng-duojing-take")]
    private void AnswerMouDuojingTake(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!ValidMouDuojing(f) || f.MouLuMengDuojing is not { Stage: MouLuMengDuojingStage.Taking } r ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], MouDuojingTakeChoices(f).Where(c => c.Id == selected.Id).ToArray()) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("slot-index"), NumberStyles.None, CultureInfo.InvariantCulture, out var slot))
            throw new InvalidOperationException("Duojing must select one exact published opaque native Hand slot.");
        ClearPendingDecision();
        var target = r.Targets[r.TargetIndex]; var hand = GetHand(_players[target]);
        if (!_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive || slot < 0 || slot >= hand.Count)
        { FinishMouDuojingTake(f, null, null); ContinueMouDuojing(f.Id); return; }
        var card = hand[slot];
        MoveCard(card, CardLocation.Hand(target), CardLocation.Hand(f.OwnerSeat), new(MouDuojingTakeReason), movement =>
            FinishMouDuojingTake(GetActiveProgramFrame(f.Id), card.Id, movement.Sequence), disclosure: CardMovementDisclosure.FaceDown);
        ContinueMouDuojing(f.Id);
    }
    private void FinishMouDuojingTake(ProgramSkillFrame f, int? card, int? sequence)
    {
        var r = f.MouLuMengDuojing!;
        if (r.Stage != MouLuMengDuojingStage.Taking || ProgramEventHistory<MouLuMengDuojingTakenEvent>().Any(e => e.FrameId == f.Id && e.TargetIndex == r.TargetIndex))
            throw new InvalidOperationException("Duojing cannot take or finish the same target's paid reward twice.");
        ReplaceRuntimeTop(f with { MouLuMengDuojing = r with { Stage = MouLuMengDuojingStage.MovementChildren, TakenCardId = card, MovementSequence = sequence } });
        AdvanceEventRulesAndQueueFact(new MouLuMengDuojingTakenEvent(f.Id, r.TargetIndex, r.Targets[r.TargetIndex], card, sequence));
    }
    private bool DrainMouDuojing(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueMouDuojing(long id)
    {
        var f = GetActiveProgramFrame(id);
        if (!ValidMouDuojing(f) || f.MouLuMengDuojing is not { Stage: MouLuMengDuojingStage.MovementChildren } r)
            throw new InvalidOperationException("Duojing can resume only its paid current target's exact native children.");
        if (DrainMouDuojing(id)) return;
        f = GetActiveProgramFrame(id); r = f.MouLuMengDuojing!;
        if (ProgramEventHistory<MouLuMengDuojingPhaseCreditEvent>().Any(e => e.FrameId == id && e.TargetIndex == r.TargetIndex))
            throw new InvalidOperationException("Duojing cannot grant the original phase's Slash credit twice.");
        AdvanceEventRulesAndQueueFact(new MouLuMengDuojingPhaseCreditEvent(id, r.TargetIndex, f.OwnerSeat, r.CardUseId, r.Phase));
        ReplaceRuntimeTop(f with { MouLuMengDuojing = r with { TargetIndex = r.TargetIndex + 1, Stage = MouLuMengDuojingStage.Choosing, TakenCardId = null, MovementSequence = null } });
        OfferMouDuojingTarget(id);
    }
    private void CompleteMouDuojing(ProgramSkillFrame f)
    {
        var r = f.MouLuMengDuojing!;
        ReplaceRuntimeTop(f with { MouLuMengDuojing = r with { TargetIndex = r.Targets.Count, Stage = MouLuMengDuojingStage.Complete, TakenCardId = null, MovementSequence = null } });
        AdvanceEventRulesAndQueueFact(new MouLuMengDuojingCompletedEvent(f.Id));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29202)]
    private bool ResumeMouDuojing(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id ||
            f.MouLuMengDuojing is not { Stage: MouLuMengDuojingStage.MovementChildren }) return false;
        ContinueMouDuojing(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29202)]
    private bool ReturnMouDuojingMovement(ProgramSkillFrame f)
    {
        if (f.MouLuMengDuojing is not { Stage: MouLuMengDuojingStage.MovementChildren }) return false;
        ContinueMouDuojing(f.Id); return true;
    }
    private int MouDuojingPhaseSlashBonus(int owner) => CurrentActualDiscardRecoveryPhase() is not { } phase ? 0 :
        ProgramEventHistory<MouLuMengDuojingPhaseCreditEvent>().Count(e => e.OwnerSeat == owner && e.Phase == phase &&
            ProgramEventHistory<MouLuMengDuojingStartedEvent>().Any(s => s.FrameId == e.FrameId && s.Source.OwnerSeat == owner &&
                s.Source.SkillId == MouDuojing && s.CardUseId == e.CardUseId && s.Phase == phase) &&
            ProgramEventHistory<MouLuMengDuojingArmorPaidEvent>().Any(p => p.FrameId == e.FrameId && p.TargetIndex == e.TargetIndex && p.CardUseId == e.CardUseId));
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouLuMengDuojing)]
    private PromptChoice SelectAiMouDuojing(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.MouLuMengDuojing?.Stage == MouLuMengDuojingStage.Taking) return p.Choices[0];
        var owner = _players[f.OwnerSeat]; var target = f.MouLuMengDuojing!.Targets[f.MouLuMengDuojing.TargetIndex];
        var view = CreateSnapshot(owner.Seat);
        var enemy = _aiBrains[owner.Seat].ScoreProgramTarget(view, target, new(0, 0, 0, 0, 0, 1, false, false)) > 0;
        return p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == (enemy && owner.Armor > 1 ? "pay" : "skip"));
    }
}
