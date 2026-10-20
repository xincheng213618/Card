namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanMouSunJianOlCounter(ProgramSkillFrame f)
    {
        var e = f.MouSunJianOlHulie!.Entry;
        return _winner == Winner.None && _players[e.ActorSeat].IsAlive && _players[e.TargetSeat].IsAlive &&
            CanUseVirtualSlashTarget(_players[e.TargetSeat], _players[e.ActorSeat], CardKind.Slash, Suit.None, effectiveColor: null, effectiveRank: 0, physicalCardIds: []) &&
            !IsDirectedCardTargetProhibited(e.TargetSeat, e.ActorSeat, CardKind.Slash) && !IsCardTargetProhibited(_players[e.ActorSeat], CardKind.Slash, Suit.None, null);
    }
    private void IssueMouSunJianOlCounter(ProgramSkillFrame f)
    {
        var r = f.MouSunJianOlHulie!;
        if (!r.Accepted || !CanMouSunJianOlCounter(f)) { FinishMouSunJianOl(f); return; }
        var outer = LifecycleCardUse(r.Entry.UseId)!; var wasActive = outer.CardAttack?.Active == true;
        if (wasActive) UpdateCardAttackState(outer.Id, a => a! with { Active = false });
        ReplaceRuntimeTop(f = f with { MouSunJianOlHulie = r with { Stage = MouSunJianOlHulieStage.Issuing, OuterWasActive = wasActive } });
        // Reuse the committed native foreign-actor empty-material Slash producer.
        BeginOfferedProgramVirtualSlash(f, r.Entry.TargetSeat, f.OwnerSeat);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 32501)]
    private void BindMouSunJianOlCounter(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId) is not { } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { MouSunJianOlHulie: { Stage: MouSunJianOlHulieStage.Issuing, Accepted: true } r } f) return;
        if (!ValidMouSunJianOlHulie(f) || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a ||
            a.ActorSeat != r.Entry.TargetSeat || a.ProviderSeat != r.Entry.TargetSeat || a.RequesterSeat is not null || !a.TargetSeats.SequenceEqual([f.OwnerSeat]) || !use.TargetSeats.SequenceEqual([f.OwnerSeat]))
            throw new InvalidOperationException("Hulie cannot bind another actor, provider, target or original zero-material use.");
        var origin = new MouSunJianOlCounterOrigin(f.Id, MouSunJianOlSource(f), f.GameplayHash, r.Entry.UseId, r.Entry.ActionId, use.Id, a.ActionId, r.Entry.TargetSeat, f.OwnerSeat);
        ReplaceRuntimeFrame(f.Id, f with { MouSunJianOlHulie = r with { Stage = MouSunJianOlHulieStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        UpdateLifecycleCardUse(use.Id, u => u with { MouSunJianOlCounter = origin });
        if (_phase == TurnPhase.Play && a.ActorSeat == _currentSeat && use.UnlimitedUse != true && !IgnoresProgramSlashLimit(_players[a.ActorSeat], null)) RecordSlashUseDebit(use.Id, a.ActorSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(a.ActorSeat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new MouSunJianOlCounterIssuedEvent(origin));
    }
    private bool IsMouSunJianOlCounterUse(CardUseFrame use)
    {
        if (use.MouSunJianOlCounter is not { } o || use.CardId != 0 || !IsSlashCard(use.CardKind) || use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a ||
            a.ActionId != o.ActionId || o.UseId != use.Id || a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null ||
            _resolutionStack.SingleOrDefault(f => f.Id == o.FrameId) is not ProgramSkillFrame { MouSunJianOlHulie: { Stage: MouSunJianOlHulieStage.NativeUse, Accepted: true, NativeReturned: false } r } parent ||
            !ValidMouSunJianOlHulie(parent) || o.Source != MouSunJianOlSource(parent) || o.GameplayHash != parent.GameplayHash || r.UseId != use.Id || r.ActionId != a.ActionId ||
            o.OriginalUseId != r.Entry.UseId || o.OriginalActionId != r.Entry.ActionId || o.ActorSeat != r.Entry.TargetSeat || o.TargetSeat != parent.OwnerSeat ||
            ProgramEventHistory<MouSunJianOlCounterIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        return ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id) ? ShownEntityUseActorMatches(use, o.ActorSeat, o.ActorSeat) : use.SourceSeat == o.ActorSeat && a.ActorSeat == o.ActorSeat;
    }
    private bool HasMouSunJianOlSuspendedCompletedParent(ProgramCardTriggerWindowFrame window)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (window.Continuation != ProgramCardContinuation.CompletedSlash || index < 1 || index + 2 >= _resolutionStack.Count ||
            _resolutionStack[index - 1] is not CardUseFrame { Step: ResolutionFrameStep.Completed, CardAttack.Active: false } outer || outer.Id != window.ParentFrameId ||
            outer.Action?.ActionId != window.Action.ActionId || window.AttackOwnerFrameId != outer.Id ||
            _resolutionStack[index + 1] is not ProgramSkillFrame { MouSunJianOlHulie: { Stage: MouSunJianOlHulieStage.NativeUse, Accepted: true, OuterWasActive: true } r } owner ||
            !ValidMouSunJianOlHulie(owner) || owner.WindowContext?.ParentFrameId != window.Id || r.Entry.UseId != outer.Id || r.Entry.ActionId != window.Action.ActionId ||
            _resolutionStack[index + 2] is not CardUseFrame { MouSunJianOlCounter: { } origin } counter || origin.FrameId != owner.Id || origin.OriginalUseId != outer.Id ||
            origin.OriginalActionId != window.Action.ActionId || r.UseId != counter.Id || r.ActionId != origin.ActionId || !IsMouSunJianOlCounterUse(counter)) return false;
        return window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count && MountObserverCandidateMatches(owner, ToSharedCandidate(window.Candidates[window.CandidateIndex]));
    }
    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 32501)]
    private bool FinishMouSunJianOlCounterAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || LifecycleCardUse(attack.ResolutionId) is not { MouSunJianOlCounter: not null } use || !IsMouSunJianOlCounterUse(use)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed); AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
    [NativeUseValidation(32501)]
    private void ValidateMouSunJianOlCounterUse(CardUseFrame use)
    { if (use.MouSunJianOlCounter is not null && (!IsMouSunJianOlCounterUse(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Hulie must complete its entire genuine counter Use before returning."); }
    [NativeUseReturn(32501)]
    private void ReturnMouSunJianOlCounterUse(CardUseFrame use)
    {
        if (use.MouSunJianOlCounter is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.MouSunJianOlHulie!;
        if (r.UseId != use.Id || r.ActionId != o.ActionId || r.NativeReturned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Hulie lost its exact whole-Use typed return.");
        ReplaceRuntimeTop(f with { MouSunJianOlHulie = r with { NativeReturned = true } });
        if (use.CardAttack is not { Active: true }) CompleteMouSunJianOlCounter(o.FrameId);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 32501)]
    private bool ReturnMouSunJianOlCounterAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouSunJianOlHulie: { Stage: MouSunJianOlHulieStage.NativeUse, NativeReturned: true } r } f || r.UseId != completion.ResolutionId) return false;
        if (completion.ProgramFrameId != f.Id) throw new InvalidOperationException("Hulie returned to a different owning program.");
        CompleteMouSunJianOlCounter(f.Id); return true;
    }
    private void CompleteMouSunJianOlCounter(long frameId)
    {
        var f = GetActiveProgramFrame(frameId); var r = f.MouSunJianOlHulie!;
        if (!r.NativeReturned || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<MouSunJianOlCounterReturnedEvent>().Any(e => e.FrameId == frameId)) throw new InvalidOperationException("Hulie cannot repeat a returned counter Use.");
        if (r.OuterWasActive && LifecycleCardUse(r.Entry.UseId)?.CardAttack is { Active: false }) UpdateCardAttackState(r.Entry.UseId, a => a! with { Active = true });
        AdvanceEventRulesAndQueueFact(new MouSunJianOlCounterReturnedEvent(frameId, r.UseId, r.ActionId));
        ReplaceRuntimeTop(f = f with { MouSunJianOlHulie = r with { Stage = MouSunJianOlHulieStage.Children }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); ResumeMouSunJianOl(f.Id);
    }
}
