using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SaoguDiscardReason = "skill-program.duan-jiong.saogu.discard", SaoguDrawReason = "skill-program.duan-jiong.saogu.draw";
    private bool HasDuanJiongSaoguCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveSaoguConversionSweep);
    private static CardConversionSource DuanJiongSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static int SaoguSuitBit(Suit suit) => suit switch { Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0 };
    private int SaoguDiscardedSuitMask(int actor, ActualDiscardRecoveryPhaseKey phase) => CompleteProgramEventHistory().OfType<DuanJiongSaoguDiscardSuitEvent>()
        .Where(e => e.OwnerSeat == actor && e.Phase == phase).Aggregate(0, (bits, e) => bits | SaoguSuitBit(e.Suit));
    private void CaptureDuanJiongSaoguBatch(CardMovementBatchContext batch)
    {
        if (!HasDuanJiongSaoguCapability || batch.DiscardRecoveryPhase is not { } phase || !IsRecordedActualDiscardRecoveryPhase(phase)) return;
        foreach (var m in batch.Movements)
        {
            if (!_cardMovements.Contains(m) || GetProgramDiscardSource(m) is not { OwnerSeat: { } actor } || !IsValidPlayerSeat(actor) ||
                CompleteProgramEventHistory().OfType<DuanJiongSaoguDiscardSuitEvent>().Any(e => e.MovementSequence == m.Sequence)) continue;
            var suit = EffectiveSuit(_players[actor], GetAdvancedCard(m.CardId));
            AdvanceEventRulesAndQueueFact(new DuanJiongSaoguDiscardSuitEvent(batch.Id, phase, actor, m.CardId, m.Sequence, suit));
        }
    }
    private IReadOnlyList<DuanJiongSaoguCard> SaoguDiscardable(int actor, int forbidden, string skill, string instance) => Array.AsReadOnly(GetHand(_players[actor]).Concat(GetEquipment(actor))
        .Where(c => !c.IsGeneralWeapon && (SaoguSuitBit(EffectiveSuit(_players[actor], c)) & forbidden) == 0 &&
            !IsSelfHandCategoryDiscardForbidden(actor, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
            !IsForeignEquipmentDiscardPrevented(actor, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
            !IsActiveProgramSourceEquipmentCard(actor, skill, instance, c))
        .Select(c => new DuanJiongSaoguCard(c.Id, _cardZones.GetLocation(c.Id), c.Kind, EffectiveSuit(_players[actor], c))).ToArray());
    [ProgramCapability]
    private sealed class DuanJiongCapability(GameEngine e) : IDuanJiongProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginDuanJiongSaogu(f); }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 27060, SkillProgramEffectOp.ResolveSaoguConversionSweep)]
    private bool AllowsDuanJiongSaogu(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == owner.Seat &&
        GetSkillBindingShard(owner).ProgramInstances.Any(i => i.SkillId == "ol:saogu" &&
            (GetProgramBooleanState(owner.Seat, i.SkillId, i.SkillInstanceId, "yin") || SaoguDiscardable(owner.Seat, SaoguDiscardedSuitMask(owner.Seat, phase), i.SkillId, i.SkillInstanceId).Count >= 2));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27060)]
    private bool AllowsDuanJiongEnding(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        !t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveSaoguConversionSweep) ||
        context.Window == SkillProgramTriggerWindow.TurnEnding && c.OwnerSeat == _currentSeat && _players[c.OwnerSeat].IsAlive &&
        _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat) && SaoguDiscardable(c.OwnerSeat, 0, c.SkillId, c.SkillInstanceId).Count > 0;
    private SkillProgramStepOutcome BeginDuanJiongSaogu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var ending = f.TriggerId is not null;
        if (f.DuanJiongSaogu is not null || f.SkillId != "ol:saogu" || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.OwnerSeat != _currentSeat || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            CurrentActualDiscardRecoveryPhase() is not { } phase || phase.ActorSeat != f.OwnerSeat || phase.TurnNumber != _turnNumber ||
            (ending ? f.TriggerId != "delegate-current" || f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || phase.Kind != ActualDiscardRecoveryPhaseKind.Ending :
                f.ActivationId != "sweep" || f.WindowContext is not null || _phase != TurnPhase.Play || phase.Kind != ActualDiscardRecoveryPhaseKind.Play))
            throw new InvalidOperationException("Saogu requires its exact living source and actual own Play or Ending phase.");
        var yang = !GetProgramBooleanState(f, "yin");
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = new(DuanJiongSource(f), f.GameplayHash, f.InstructionIndex, phase, ending, yang, f.OwnerSeat,
            ending ? DuanJiongSaoguStage.DelegateChoice : yang ? DuanJiongSaoguStage.DiscardChoice : DuanJiongSaoguStage.DrawChildren) });
        AdvanceEventRulesAndQueueFact(new DuanJiongSaoguStartedEvent(f.Id, DuanJiongSource(f), f.GameplayHash, phase, ending, yang));
        if (ending) { PublishSaogu(f); return SkillProgramStepOutcome.AwaitChoice; }
        if (yang) PrepareSaoguDiscard(f); else PaySaoguDraw(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private Dictionary<string, string> SaoguArgs(string branch) => new() { ["program-action"] = "duan-jiong-saogu", ["branch"] = branch };
    private IReadOnlyList<PromptChoice> SaoguChoices(ProgramSkillFrame f)
    {
        var r = f.DuanJiongSaogu!;
        if (r.Stage == DuanJiongSaoguStage.DelegateChoice)
            return Array.AsReadOnly(SaoguDiscardable(f.OwnerSeat, 0, f.SkillId, f.SkillInstanceId).SelectMany(card => _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)
                .Select(p => new PromptChoice(new($"saogu.{f.Id}.delegate.{card.CardId}.{p.Seat}"), $"弃置【{GetAttackCard(card.CardId).DisplayName}】，令{p.Name}执行{(r.Yang ? "阳" : "阴")}项。", [card.CardId], [p.Seat], SaoguArgs("delegate")))).ToArray());
        if (r.Stage == DuanJiongSaoguStage.DiscardChoice)
        {
            var legal = SaoguDiscardable(r.ActorSeat, r.ForbiddenSuitMask, f.SkillId, f.SkillInstanceId);
            return Array.AsReadOnly(r.Candidates.Where(c => legal.Contains(c) && !r.Selected.Contains(c)).Select(c => new PromptChoice(new($"saogu.{f.Id}.discard.{r.Selected.Count}.{c.CardId}"),
                $"弃置【{GetAttackCard(c.CardId).DisplayName}】（{r.Selected.Count + 1}/{r.RequiredCount}）", [c.CardId], [], SaoguArgs("discard"))).ToArray());
        }
        if (r.Stage == DuanJiongSaoguStage.UseChoice) return Array.AsReadOnly(SaoguNativeActions(f).Select((a, i) =>
        {
            var args = SaoguArgs("use"); args["index"] = i.ToString(CultureInfo.InvariantCulture);
            return new PromptChoice(new($"saogu.{f.Id}.use.{r.UsedCardIds.Count}.{i}"), a.Description, [a.CardId!.Value], a.TargetSeats, args);
        }).ToArray());
        return [];
    }
    private void PublishSaogu(ProgramSkillFrame f)
    {
        var r = f.DuanJiongSaogu!; var choices = SaoguChoices(f); if (choices.Count == 0) throw new InvalidOperationException("Saogu cannot publish an empty mandatory native choice.");
        var actor = r.Stage == DuanJiongSaoguStage.DelegateChoice ? f.OwnerSeat : r.ActorSeat; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, actor, r.Stage == DuanJiongSaoguStage.UseChoice ? "必须使用其中可合法使用的杀。" : "扫谷：选择原生付款及执行者。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage != DuanJiongSaoguStage.UseChoice, Choices = choices, TargetSeat = actor, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[actor].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("duan-jiong-saogu")]
    private void AnswerSaogu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DuanJiongSaogu: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == DuanJiongSaoguStage.DelegateChoice ? f.OwnerSeat : r.ActorSeat) ||
            !AssistedChoicesEqual([choice], SaoguChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Saogu requires the exact original chooser, materials and native targets.");
        AssertSaogu(f); ClearPendingDecision();
        if (r.Stage == DuanJiongSaoguStage.UseChoice) { IssueSaoguUse(f, choice); return; }
        if (r.Stage == DuanJiongSaoguStage.DelegateChoice)
        {
            var fee = SaoguDiscardable(f.OwnerSeat, 0, f.SkillId, f.SkillInstanceId).Single(c => c.CardId == choice.Cards.Single());
            ReplaceRuntimeTop(f = f with { DuanJiongSaogu = r with { ActorSeat = choice.Targets.Single(), DelegateFee = fee, Selected = [fee], Stage = DuanJiongSaoguStage.DelegateChildren } });
            PaySaoguDiscard(f, true); ContinueSaogu(f.Id); return;
        }
        var selected = r.Selected.Append(r.Candidates.Single(c => c.CardId == choice.Cards.Single())).ToArray();
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = r with { Selected = selected } });
        if (selected.Length < r.RequiredCount) { PublishSaogu(f); return; }
        PaySaoguDiscard(f, false); ContinueSaogu(f.Id);
    }
    private void PrepareSaoguDiscard(ProgramSkillFrame f)
    {
        var r = f.DuanJiongSaogu!; var mask = SaoguDiscardedSuitMask(r.ActorSeat, r.Phase) | (r.DelegateFee is { } fee ? SaoguSuitBit(fee.Suit) : 0);
        var legal = SaoguDiscardable(r.ActorSeat, mask, f.SkillId, f.SkillInstanceId); var count = r.Ending ? Math.Min(2, legal.Count) : 2;
        if (legal.Count < count || count == 0) { FinishSaogu(f, "no-legal-discard"); return; }
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = r with { Stage = DuanJiongSaoguStage.DiscardChoice, ForbiddenSuitMask = mask, RequiredCount = count, Candidates = legal, Selected = [] } });
        PublishSaogu(f);
    }
    private void PaySaoguDiscard(ProgramSkillFrame f, bool fee)
    {
        var r = f.DuanJiongSaogu!; var actor = fee ? f.OwnerSeat : r.ActorSeat;
        var legal = SaoguDiscardable(actor, fee ? 0 : r.ForbiddenSuitMask, f.SkillId, f.SkillInstanceId);
        if (r.Selected.Count != (fee ? 1 : r.RequiredCount) || r.Selected.Any(c => !legal.Contains(c))) throw new InvalidOperationException("Saogu cannot replace its exact selected legal HE payment.");
        var grain = r.Selected.Any(c => c.From == CardLocation.Equipment(actor) && c.Kind == CardKind.WoodenOx) ? GetWoodenOxGrain(_players[actor]).Select(c => c.Id).ToArray() : [];
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = r with { Stage = fee ? DuanJiongSaoguStage.DelegateChildren : DuanJiongSaoguStage.DiscardChildren,
            Paid = true, Before = _movementSequence, After = _movementSequence, WoodenOxGrainIds = grain, Payments = fee ? r.Payments : r.Selected } });
        if (!fee && !r.Ending) SetProgramBooleanState(f, "yin", true);
        MoveProgramLogicalDiscard(f, actor, r.Selected.Select(c => c.CardId).ToArray(), new(SaoguDiscardReason), (_, movements) =>
        {
            var active = GetActiveProgramFrame(f.Id); var receipt = active.DuanJiongSaogu!;
            ReplaceRuntimeTop(active with { DuanJiongSaogu = receipt with { After = _movementSequence, PaidBatchId = _activeCardMovementBatchIds.Peek() } });
            foreach (var m in movements)
            {
                var paid = r.Selected.Single(c => c.CardId == m.CardId);
                AdvanceEventRulesAndQueueFact(new DuanJiongSaoguEntityPaidEvent(f.Id, fee, actor, m.CardId, m.From, m.Sequence, paid.Kind, paid.Suit));
            }
            if (!fee) AdvanceEventRulesAndQueueFact(new DuanJiongSaoguBranchPaidEvent(f.Id, actor, true, !r.Ending, movements.Count, receipt.Before, _movementSequence));
        });
        var current = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { DuanJiongSaogu = current.DuanJiongSaogu! with { After = _movementSequence } });
    }
    private void PaySaoguDraw(ProgramSkillFrame f)
    {
        var r = f.DuanJiongSaogu!;
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = r with { Stage = DuanJiongSaoguStage.DrawChildren, Paid = true, Before = _movementSequence, After = _movementSequence, Selected = [], WoodenOxGrainIds = [] } });
        if (!r.Ending) SetProgramBooleanState(f, "yin", false);
        var drawn = DrawCards(_players[r.ActorSeat], 1, true, new(SaoguDrawReason)); f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = f.DuanJiongSaogu! with { After = _movementSequence, DrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new DuanJiongSaoguBranchPaidEvent(f.Id, r.ActorSeat, false, !r.Ending, drawn.Count, f.DuanJiongSaogu!.Before, _movementSequence)); ContinueSaogu(f.Id);
    }
    private bool DrainSaoguChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueSaogu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DuanJiongSaogu: { } r } f || f.Id != id) return;
        AssertSaogu(f);
        if (r.Stage is DuanJiongSaoguStage.DelegateChoice or DuanJiongSaoguStage.DiscardChoice or DuanJiongSaoguStage.UseChoice) { if (_pendingDecision is null) PublishSaogu(f); return; }
        if (r.Stage == DuanJiongSaoguStage.NativeUse) throw new InvalidOperationException("An issued Saogu Slash must return through its native origin.");
        if (DrainSaoguChildren(id)) return; f = GetActiveProgramFrame(id); r = f.DuanJiongSaogu!;
        if (_winner != Winner.None || !_players[r.ActorSeat].IsAlive) { FinishSaogu(f, "actor-dead-or-game-ended"); return; }
        if (r.Stage == DuanJiongSaoguStage.DelegateChildren) { if (r.Yang) PrepareSaoguDiscard(f); else PaySaoguDraw(f); return; }
        if (r.Stage == DuanJiongSaoguStage.DrawChildren) { FinishSaogu(f, "yin-draw-finished"); return; }
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = r with { Stage = DuanJiongSaoguStage.UseChoice, SelectedUseCardId = null, ReservedUseId = null, NativeUse = null, NativeCompleted = false } });
        if (SaoguNativeActions(f).Count == 0) FinishSaogu(f, "all-available-slashes-used"); else PublishSaogu(f);
    }
    private void FinishSaogu(ProgramSkillFrame f, string reason)
    {
        var r = f.DuanJiongSaogu!; AdvanceEventRulesAndQueueFact(new DuanJiongSaoguCompletedEvent(f.Id, r.ActorSeat, r.Ending, r.Yang, r.UsedCardIds.Count, reason));
        ReplaceRuntimeTop(f = f with { DuanJiongSaogu = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27060)]
    private bool ResumeSaogu(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DuanJiongSaogu: not null } f || f.Id != id) return false; ContinueSaogu(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27060)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27060)]
    private bool ContinuePaidSaogu(ProgramSkillFrame f) => f.DuanJiongSaogu is { Paid: true } && ValidSaogu(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveSaoguConversionSweep)]
    private PromptChoice ChooseSaoguAi(PendingDecision p, ProgramSkillFrame f)
    {
        var view = CreateSnapshot(p.PlayerSeat);
        double Hostility(int seat) => _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, seat, new(0, 0, 0, 0, 0, 1, false, false));
        return p.Choices.OrderBy(c => c.Cards.Sum(id => GetKeepValue(GetAttackCard(id), _players[p.PlayerSeat])) - c.Targets.Sum(Hostility)).First();
    }
}
