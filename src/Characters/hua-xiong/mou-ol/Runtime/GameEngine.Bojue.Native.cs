namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanMouHxBojueSlash(int actor, int target) => _winner == Winner.None && _players[actor].IsAlive && _players[target].IsAlive &&
        actor != target && CanUseVirtualSlashTarget(_players[actor], _players[target], CardKind.Slash, Suit.None,
            effectiveColor: null, effectiveRank: 0, physicalCardIds: [], countsTowardLimit: true) &&
        !IsDirectedCardTargetProhibited(actor, target, CardKind.Slash) && !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null);

    private void IssueMouHxBojueSlash(ProgramSkillFrame f)
    {
        var r = f.MouHxBojue!;
        var actor = MouHxBojueActor(f);
        var target = MouHxBojueOther(f);
        if (!CanMouHxBojueSlash(actor, target)) { CompleteMouHxBojueItem(f, false); return; }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MouHxBojue = r with { Stage = MouHxBojueStage.Issuing } });
        BeginOfferedProgramVirtualSlash(f, actor, target);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33300)]
    private void BindMouHxBojueSlash(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId) is not { } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { MouHxBojue: { Stage: MouHxBojueStage.Issuing } r } f) return;
        var actor = MouHxBojueActor(f);
        var target = MouHxBojueOther(f);
        if (!ValidMouHxBojue(f) || r.Delta != 2 || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a ||
            a.ActorSeat != actor || a.ProviderSeat != actor || a.RequesterSeat is not null || !a.TargetSeats.SequenceEqual([target]))
            throw new InvalidOperationException("Bojue cannot substitute another participant, material, or native Slash target.");
        var origin = new MouHxBojueSlashOrigin(f.Id, r.Source, r.GameplayHash, r.Cursor, use.Id, a.ActionId, actor, target);
        ReplaceRuntimeFrame(f.Id, f with { MouHxBojue = r with { Stage = MouHxBojueStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        UpdateLifecycleCardUse(use.Id, u => u with { MouHxBojueSlash = origin });
        if (_phase == TurnPhase.Play && actor == _currentSeat && use.UnlimitedUse != true && !IgnoresProgramSlashLimit(_players[actor], null)) RecordSlashUseDebit(use.Id, actor);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new MouHxBojueSlashIssuedEvent(origin));
    }

    private bool IsMouHxBojueUse(CardUseFrame use)
    {
        if (use.MouHxBojueSlash is not { } o || use.Id != o.UseId || use.CardId != 0 || !IsSlashCard(use.CardKind) ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a ||
            a.ActionId != o.ActionId || a.ProviderSeat != o.Actor || a.RequesterSeat is not null ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is not ProgramSkillFrame { MouHxBojue: { Stage: MouHxBojueStage.NativeUse } r } f ||
            !ValidMouHxBojue(f) || o.Source != r.Source || o.GameplayHash != f.GameplayHash || o.Cursor != r.Cursor || r.UseId != use.Id || r.ActionId != a.ActionId ||
            o.Actor != MouHxBojueActor(f) || o.Target != MouHxBojueOther(f) || ProgramEventHistory<MouHxBojueSlashIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        return ShownEntityUseActorMatches(use, o.Actor, o.Actor);
    }

    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 33300)]
    private bool FinishMouHxBojueAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || LifecycleCardUse(attack.ResolutionId) is not { MouHxBojueSlash: not null } use || !IsMouHxBojueUse(use)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats,
                ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }

    [NativeUseValidation(33300)]
    private void ValidateMouHxBojueUse(CardUseFrame use)
    {
        if (use.MouHxBojueSlash is not null && (!IsMouHxBojueUse(use) || use.Step != ResolutionFrameStep.Completed))
            throw new InvalidOperationException("Bojue must finish the exact whole native Slash before returning to the second participant.");
    }
    [NativeUseReturn(33300)]
    private void ReturnMouHxBojueUse(CardUseFrame use)
    {
        if (use.MouHxBojueSlash is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId);
        var r = f.MouHxBojue!;
        if (r.UseId != use.Id || r.ActionId != o.ActionId || r.Returned || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Bojue cannot return a repeated or unrelated native Slash.");
        ReplaceRuntimeTop(f with { MouHxBojue = r with { Returned = true } });
        if (use.CardAttack is not { Active: true }) CompleteMouHxBojueSlash(o.FrameId);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 33300)]
    private bool ReturnMouHxBojueAttack(AttackCompletionReceipt receipt)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHxBojue: { Stage: MouHxBojueStage.NativeUse, Returned: true } r } f || r.UseId != receipt.ResolutionId) return false;
        if (receipt.ProgramFrameId != f.Id) throw new InvalidOperationException("Bojue's native attack returned to another producer.");
        CompleteMouHxBojueSlash(f.Id);
        return true;
    }
    private void CompleteMouHxBojueSlash(long id)
    {
        var f = GetActiveProgramFrame(id);
        var r = f.MouHxBojue!;
        if (!r.Returned || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<MouHxBojueSlashReturnedEvent>().Any(e => e.FrameId == id && e.Cursor == r.Cursor))
            throw new InvalidOperationException("Bojue must release each original participant's paid native child exactly once.");
        AdvanceEventRulesAndQueueFact(new MouHxBojueSlashReturnedEvent(id, r.Cursor, r.UseId, r.ActionId));
        ReplaceRuntimeTop(f = f with { MouHxBojue = r with { Stage = MouHxBojueStage.NativeChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueMouHxBojue(id);
    }
}
