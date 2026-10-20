namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void IssueMouLzCounter(ProgramSkillFrame f)
    {
        var r = f.MouLuZhiOl!;
        if (!r.Paid || !MouLzSlashTargets(f.OwnerSeat).Contains(r.CounterTarget)) { FinishMouLz(f); return; }
        var outer = _resolutionStack.SingleOrDefault(x => x.Id == r.OriginalId);
        var wasActive = outer is CardUseFrame { CardAttack.Active: true };
        if (wasActive) UpdateCardAttackState(r.OriginalId, a => a! with { Active = false });
        ReplaceRuntimeTop(f = f with { MouLuZhiOl = r with { Stage = MouLuZhiOlStage.Issuing, OuterWasActive = wasActive } });
        BeginOfferedProgramVirtualSlash(f, f.OwnerSeat, r.CounterTarget);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 32901)]
    private void BindMouLzCounter(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId) is not { } use) return;
        var n = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (n < 1 || _resolutionStack[n - 1] is not ProgramSkillFrame { MouLuZhiOl: { Stage: MouLuZhiOlStage.Issuing, Paid: true } r } f) return;
        if (!ValidMouLz(f) || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.RequesterSeat is not null || !a.TargetSeats.SequenceEqual([r.CounterTarget])) throw new InvalidOperationException("Sibing cannot bind another native zero-material actor or target.");
        var o = new MouLuZhiOlCounterOrigin(f.Id, r.Source, r.GameplayHash, r.OriginalId, r.OriginalActionId, use.Id, a.ActionId, f.OwnerSeat, r.CounterTarget);
        ReplaceRuntimeFrame(f.Id, f with { MouLuZhiOl = r with { Stage = MouLuZhiOlStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } }); UpdateLifecycleCardUse(use.Id, u => u with { MouLuZhiOlCounter = o });
        if (_phase == TurnPhase.Play && a.ActorSeat == _currentSeat && use.UnlimitedUse != true && !IgnoresProgramSlashLimit(_players[a.ActorSeat], null)) RecordSlashUseDebit(use.Id, a.ActorSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(a.ActorSeat, CardKind.Slash); AdvanceEventRulesAndQueueFact(new MouLuZhiOlCounterIssuedEvent(o));
    }
    private bool IsMouLzCounterUse(CardUseFrame u) => u.MouLuZhiOlCounter is { } o && u.Id == o.UseId && u.CardId == 0 && u.CardKind == CardKind.Slash && u.PhysicalCardIds is { Count: 0 } && u.Action is { Type: CardActionType.Use, PhysicalCards.Count: 0 } a && a.ActionId == o.ActionId && a.ProviderSeat == o.ActorSeat && a.RequesterSeat is null &&
        _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is ProgramSkillFrame { MouLuZhiOl: { Stage: MouLuZhiOlStage.NativeUse, Paid: true } r } f && ValidMouLz(f) && r.Source == o.Source && r.GameplayHash == o.GameplayHash && r.UseId == u.Id && r.ActionId == a.ActionId && r.CounterTarget == o.TargetSeat && o.ActorSeat == f.OwnerSeat && ProgramEventHistory<MouLuZhiOlCounterIssuedEvent>().Count(e => e.Origin == o) == 1 && ShownEntityUseActorMatches(u, o.ActorSeat, o.ActorSeat);
    private bool HasMouLzSuspendedCompletedParent(ProgramCardTriggerWindowFrame w)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == w.Id);
        return w.Continuation == ProgramCardContinuation.CompletedSlash && n > 0 && n + 2 < _resolutionStack.Count && _resolutionStack[n - 1] is CardUseFrame { Step: ResolutionFrameStep.Completed, CardAttack.Active: false } outer && outer.Id == w.ParentFrameId && outer.Action?.ActionId == w.Action.ActionId &&
            _resolutionStack[n + 1] is ProgramSkillFrame { MouLuZhiOl: { Stage: MouLuZhiOlStage.NativeUse, OuterWasActive: true } r } f && ValidMouLz(f) && r.OriginalId == outer.Id && f.WindowContext?.ParentFrameId == w.Id &&
            _resolutionStack[n + 2] is CardUseFrame { MouLuZhiOlCounter: { } o } counter && o.FrameId == f.Id && IsMouLzCounterUse(counter) && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, ToSharedCandidate(w.Candidates[w.CandidateIndex]));
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 32901)]
    private bool FinishMouLzCounterAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (LifecycleCardUse(attack.ResolutionId) is not { MouLuZhiOlCounter: not null } u || !IsMouLzCounterUse(u)) return false;
        SetCardUseStep(u.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(u.Id, 0, CardKind.Slash));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, u.Action!, SkillProgramTriggerWindow.CardUseCompleted, u.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(u.Id); CompleteFinishedAttackCardUse(attack); } return true;
    }
    [NativeUseValidation(32901)] private void ValidateMouLzCounter(CardUseFrame u)
    { if (u.MouLuZhiOlCounter is not null && (!IsMouLzCounterUse(u) || u.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Sibing must complete its real native counter Use."); }
    [NativeUseReturn(32901)] private void ReturnMouLzCounter(CardUseFrame u)
    {
        if (u.MouLuZhiOlCounter is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.MouLuZhiOl!;
        if (r.UseId != u.Id || r.ActionId != o.ActionId || r.NativeReturned || LifecycleCardUse(u.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == u.Id) != 1) throw new InvalidOperationException("Sibing lost its exact counter Use return.");
        ReplaceRuntimeTop(f with { MouLuZhiOl = r with { NativeReturned = true } }); if (u.CardAttack is not { Active: true }) CompleteMouLzCounter(o.FrameId);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 32901)] private bool ReturnMouLzCounterAttack(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuZhiOl: { Stage: MouLuZhiOlStage.NativeUse, NativeReturned: true } r } f || r.UseId != c.ResolutionId) return false;
        if (c.ProgramFrameId != f.Id) throw new InvalidOperationException("Sibing counter returned to another frame."); CompleteMouLzCounter(f.Id); return true;
    }
    private void CompleteMouLzCounter(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouLuZhiOl!;
        if (!r.NativeReturned || ProgramEventHistory<MouLuZhiOlCounterReturnedEvent>().Any(e => e.FrameId == id)) throw new InvalidOperationException("Sibing counter cannot return twice.");
        if (r.OuterWasActive && LifecycleCardUse(r.OriginalId)?.CardAttack is { Active: false }) UpdateCardAttackState(r.OriginalId, a => a! with { Active = true });
        AdvanceEventRulesAndQueueFact(new MouLuZhiOlCounterReturnedEvent(id, r.UseId, r.ActionId)); ReplaceRuntimeTop(f with { MouLuZhiOl = r with { Stage = MouLuZhiOlStage.NativeReturned } }); ContinueMouLz(id);
    }
    private bool MouLzCannotRespond(int source, int responder, CardKind kind, IReadOnlyList<int> targets)
    {
        // Innermost incoming native effect only: an ancestor ban cannot affect a nested different Use.
        var incoming = _resolutionStack.LastOrDefault(x => x is CardUseFrame || x is ProgramSkillFrame { MouLuZhiOlDirectDuel: not null, CardAttack.Active: true });
        long id; long action; var instruction = 0;
        if (incoming is CardUseFrame { Action: { } a } u) { if (a.ActorSeat != source || a.EffectiveKind != kind) return false; id = u.Id; action = a.ActionId; }
        else if (incoming is ProgramSkillFrame { MouLuZhiOlDirectDuel: { } d }) { if (d.Actor != source || kind != CardKind.Duel) return false; id = d.FrameId; action = 0; instruction = d.Instruction; }
        else return false;
        return targets.Contains(responder) && ProgramEventHistory<MouLuZhiOlResponseBannedEvent>().Any(e => e.Ban.UseId == id && e.Ban.ActionId == action && e.Ban.TargetSeat == responder && e.Ban.Instruction == instruction);
    }
    [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 32900)] private bool RecordMouLzDamage(IDamageAttempt attack, long damageId)
    {
        if (attack is not CardAttackHandle a || !a.DamageWasApplied || _resolutionStack.SingleOrDefault(f => f.Id == damageId) is not DamageFrame { Amount: > 0 } d) return false;
        var use = LifecycleCardUse(attack.ResolutionId); var direct = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == attack.ResolutionId && f.MouLuZhiOlDirectDuel is not null);
        if (use is null && direct is null || use is not null && !IsMouLzDamageCard(use.CardKind)) return false;
        if (!ProgramEventHistory<MouLuZhiOlUseDamageEvent>().Any(e => e.DamageId == damageId)) AdvanceEventRulesAndQueueFact(new MouLuZhiOlUseDamageEvent(attack.ResolutionId, direct?.MouLuZhiOlDirectDuel?.Instruction ?? 0, damageId, d.TargetSeat, d.Amount)); return false;
    }
}
