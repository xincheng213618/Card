namespace CardGame.Core;
public sealed partial class GameEngine
{
    // The common native fact is emitted at declaration, including response Uses
    // and legacy Duel. Full committed history preserves the previous card across turns.
    private bool MouZhangXiuOlUseDamaged(long origin, int? instruction) => ProgramEventHistory<MouZhangXiuOlUseDamageEvent>().Any(e => e.OriginFrameId == origin && e.InstructionIndex == instruction && e.Amount > 0);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33001)]
    private void CaptureMouZhangXiuOlPreviousUse(IGameEvent fact)
    {
        if (!HasMouZhangXiuOl || fact is not CurrentTurnCardUseKindsRecordedEvent { CardActionId: { } actionId, RoundActionType: CardActionType.Use } current ||
            LifecycleCardUse(current.OriginFrameId) is not { Action: { Type: CardActionType.Use } action } use || action.ActionId != actionId || action.ActorSeat != current.ActorSeat || !IsSlashCard(use.CardKind)) return;
        var previous = ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().LastOrDefault(e => e.ActorSeat == current.ActorSeat && e.CardActionId != actionId);
        // No previous true Use is not a zero-damage previous card. Pure Played
        // actions do not enter the common declaration ledger in the first place.
        if (previous is null || previous.RoundActionType == CardActionType.Use && MouZhangXiuOlUseDamaged(previous.OriginFrameId, previous.ProgramInstructionIndex)) return;
        foreach (var c in CollectProgramTriggerCandidates(_players[current.ActorSeat], SkillProgramTriggerWindow.CardUseCommitted)
            .Where(c => c.SkillId == MouZhangXiuOlComposition.Zhuijiao && c.BindingId == "previous-undamaged"))
        {
            if (use.MouZhangXiuOlZhuijiao?.Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillInstanceId == c.SkillInstanceId) == true) continue;
            var entry = new MouZhangXiuOlZhuijiaoEntry(use.Id, actionId, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash, previous);
            UpdateLifecycleCardUse(use.Id, u => u with { MouZhangXiuOlZhuijiao = (u.MouZhangXiuOlZhuijiao ?? []).Append(entry).ToArray() });
            AdvanceEventRulesAndQueueFact(new MouZhangXiuOlQualifiedEvent(entry)); use = LifecycleCardUse(use.Id)!;
        }
    }
    private bool ValidMouZhangXiuOlEntry(CardUseFrame use, MouZhangXiuOlZhuijiaoEntry e)
    {
        if (use.Action is not { Type: CardActionType.Use } a || a.ActionId != e.ActionId || use.Id != e.UseId || !IsSlashCard(use.CardKind) ||
            e.Source.SkillId != MouZhangXiuOlComposition.Zhuijiao || e.Source.BindingId != "previous-undamaged" || e.Source.OwnerSeat != e.Previous.ActorSeat ||
            _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash != e.GameplayHash ||
            ProgramEventHistory<MouZhangXiuOlQualifiedEvent>().Count(q => q.Entry == e with { Boosted = false, DrawReturned = false, PenaltyIssued = false, PenaltyReturned = false }) != 1) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var index = Array.FindIndex(history, f => f is CurrentTurnCardUseKindsRecordedEvent c && c.CardActionId == e.ActionId && c.OriginFrameId == e.UseId && c.ActorSeat == e.Source.OwnerSeat);
        if (index < 0 || history.Take(index).OfType<CurrentTurnCardUseKindsRecordedEvent>().LastOrDefault(c => c.ActorSeat == e.Source.OwnerSeat) != e.Previous) return false;
        return !e.Boosted || ProgramEventHistory<MouZhangXiuOlBoostedEvent>().Count(q => q.UseId == use.Id && q.Source == e.Source) == 1;
    }
    private MouZhangXiuOlZhuijiaoEntry? MouZhangXiuOlEntry(CardUseFrame use, ProgramTriggerCandidate c) => use.MouZhangXiuOlZhuijiao?.SingleOrDefault(e =>
        e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId && e.Source.SkillInstanceId == c.SkillInstanceId && e.GameplayHash == c.GameplayHash && ValidMouZhangXiuOlEntry(use, e));
    private void SetMouZhangXiuOlEntry(long useId, CardConversionSource source, Func<MouZhangXiuOlZhuijiaoEntry, MouZhangXiuOlZhuijiaoEntry> update) => UpdateLifecycleCardUse(useId, u => u with
    { MouZhangXiuOlZhuijiao = u.MouZhangXiuOlZhuijiao!.Select(e => e.Source.OwnerSeat == source.OwnerSeat && e.Source.SkillId == source.SkillId && e.Source.SkillInstanceId == source.SkillInstanceId ? update(e) : e).ToArray() });
    private SkillProgramStepOutcome BeginMouZhangXiuOlZhuijiao(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id); var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0);
        if (f.MouZhangXiuOl is not null || f.InstructionIndex != 1 || f.ActivationId != f.TriggerId || f.WindowContext is not { } w || !CanRunIssuedMouZhangXiuOl(c, w)) throw new InvalidOperationException("Zhuijiao requires its frozen previous-Use qualification and exact current Slash window.");
        var use = MouZhangXiuOlContextUse(c, w)!; var entry = MouZhangXiuOlEntry(use, c)!;
        var r = new MouZhangXiuOlReceipt(MouZhangXiuOlSource(f), f.GameplayHash, operation, operation == SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao ? MouZhangXiuOlStage.Drawing : MouZhangXiuOlStage.Penalty)
        { UseId = use.Id, ActionId = use.Action!.ActionId };
        ReplaceRuntimeTop(f = f with { MouZhangXiuOl = r }); AdvanceEventRulesAndQueueFact(new MouZhangXiuOlStartedEvent(f.Id, r.Source, r.GameplayHash, operation, use.Id));
        if (operation == SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao)
        {
            SetMouZhangXiuOlEntry(use.Id, entry.Source, e => e with { Boosted = true });
            AdvanceEventRulesAndQueueFact(new MouZhangXiuOlBoostedEvent(f.Id, use.Id, entry.Source));
            BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(MouZhangXiuOlDrawReason));
        }
        else
        {
            SetMouZhangXiuOlEntry(use.Id, entry.Source, e => e with { PenaltyIssued = true });
            AdvanceEventRulesAndQueueFact(new MouZhangXiuOlPenaltyStartedEvent(f.Id, use.Id, entry.Source)); PublishMouZhangXiuOl(f);
        }
        return operation == SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.AwaitChoice;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao)]
    private void ReturnMouZhangXiuOlDraw(ProgramSkillFrame f, NativeDrawOutcome draw)
    {
        if (!ValidMouZhangXiuOl(f) || f.MouZhangXiuOl is not { Stage: MouZhangXiuOlStage.Drawing, Draw: null } r || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != 1) throw new InvalidOperationException("Zhuijiao cannot redirect or repeat its native Draw1.");
        ReplaceRuntimeTop(f = f with { MouZhangXiuOl = r with { Draw = draw, Before = draw.SequenceBefore, After = draw.SequenceAfter, Stage = MouZhangXiuOlStage.DrawChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouZhangXiuOlDrawReturnedEvent(f.Id, r.UseId, f.OwnerSeat, 1, draw.ActualDrawIds.Count, draw.ReplacementGainedIds.Count)); ContinueMouZhangXiuOl(f.Id);
    }
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 33003)]
    private int MouZhangXiuOlDamageBonus(IDamageAttempt attack) => !attack.IsChainPropagation && attack is CardAttackHandle && LifecycleCardUse(attack.ResolutionId) is { MouZhangXiuOlZhuijiao: { } entries } use
        ? entries.Count(e => e.Boosted && ValidMouZhangXiuOlEntry(use, e)) : 0;
    [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 33003)]
    private bool CaptureMouZhangXiuOlUseDamage(IDamageAttempt attack, long damageId)
    {
        if (!HasMouZhangXiuOl || !attack.DamageWasApplied || _resolutionStack.SingleOrDefault(f => f.Id == damageId) is not DamageFrame { Amount: > 0 } damage || damage.ParentFrameId != attack.ResolutionId) return false;
        var origin = attack.ResolutionId;
        var instruction = _resolutionStack.SingleOrDefault(f => f.Id == origin) is ProgramSkillFrame { CardAttack: { ProgramUseInstructionIndex: { } i } } ? i : (int?)null;
        if (!ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.OriginFrameId == origin && e.ProgramInstructionIndex == instruction && e.RoundActionType == CardActionType.Use)) return false;
        if (!ProgramEventHistory<MouZhangXiuOlUseDamageEvent>().Any(e => e.DamageId == damageId)) AdvanceEventRulesAndQueueFact(new MouZhangXiuOlUseDamageEvent(origin, instruction, damageId, damage.Amount));
        return false;
    }
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedAfterCategory, 33003)]
    private bool ObserveMouZhangXiuOlAllUses(ProgramTargetCardWindowQuery query) => HasMouZhangXiuOl;
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 33003)]
    private bool NormalizeMouZhangXiuOlDrawTwo(ProgramTargetCardWindowQuery query) => HasMouZhangXiuOl;
}
