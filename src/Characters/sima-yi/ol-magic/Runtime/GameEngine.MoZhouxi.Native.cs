namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanMoSimaYiRetaliationSlash(int actor, int target) => _winner == Winner.None &&
        _players[actor].IsAlive && _players[target].IsAlive && actor != target &&
        CanUseVirtualSlashTarget(_players[actor], _players[target], CardKind.Slash, Suit.None,
            effectiveColor: null, effectiveRank: 0, physicalCardIds: [], ignoreDistance: true, countsTowardLimit: false) &&
        !IsDirectedCardTargetProhibited(actor, target, CardKind.Slash) && !IsCardTargetProhibited(_players[target], CardKind.Slash, Suit.None, null);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33900)]
    private void BindMoSimaYiSlash(CardUseDeclaredEvent fact)
    {
        if (LifecycleCardUse(fact.ResolutionId) is not { } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame
            { MoSimaYi: { Stage: MoSimaYiStage.IssuingSlash, Operation: SkillProgramEffectOp.ResolveMoSimaYiRetaliation } r } f) return;
        var actor = r.Targets[r.Index];
        if (!MoSimaYi.Valid(f) || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                EffectiveSuit: Suit.None, EffectiveRank: 0 } a || a.ActorSeat != actor || a.ProviderSeat != actor ||
            a.RequesterSeat is not null || !a.TargetSeats.SequenceEqual([f.OwnerSeat]))
            throw new InvalidOperationException("Zhouxi must issue the frozen character's actual neutral forced Slash.");
        var origin = new MoSimaYiSlashOrigin(f.Id, r.Source, r.GameplayHash, r.Index, use.Id, a.ActionId, actor, f.OwnerSeat);
        ReplaceRuntimeFrame(f.Id, f with { MoSimaYi = r with { Stage = MoSimaYiStage.NativeSlash,
            NativeUseId = use.Id, NativeActionId = a.ActionId } });
        UpdateLifecycleCardUse(use.Id, u => u with { MoSimaYiSlash = origin });
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new MoSimaYiSlashIssuedEvent(origin));
    }

    private bool IsMoSimaYiRetaliation(CardUseFrame use)
    {
        if (use.MoSimaYiSlash is not { } o || use.Id != o.UseId || use.CardId != 0 || use.CardKind != CardKind.Slash ||
            use.PhysicalCardIds is not { Count: 0 } || use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a ||
            a.ActionId != o.ActionId || a.ProviderSeat != o.Actor || a.RequesterSeat is not null ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is not ProgramSkillFrame
                { MoSimaYi: { Stage: MoSimaYiStage.NativeSlash } r } f || !MoSimaYi.Valid(f) ||
            o.Source != r.Source || o.GameplayHash != f.GameplayHash || o.Index != r.Index ||
            r.NativeUseId != use.Id || r.NativeActionId != a.ActionId || o.Actor != r.Targets[r.Index] || o.Target != f.OwnerSeat ||
            ProgramEventHistory<MoSimaYiSlashIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        return ShownEntityUseActorMatches(use, o.Actor, o.Actor);
    }

    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 33900)]
    private bool FinishMoSimaYiSlash(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (attack.Card is not null || LifecycleCardUse(attack.ResolutionId) is not { MoSimaYiSlash: not null } use ||
            !IsMoSimaYiRetaliation(use)) return false;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
                use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }

    [NativeUseValidation(33900)]
    private void ValidateMoSimaYiUse(CardUseFrame use)
    {
        if (use.MoSimaYiSlash is not null && (!IsMoSimaYiRetaliation(use) || use.Step != ResolutionFrameStep.Completed))
            throw new InvalidOperationException("Zhouxi requires its exact whole native use to finish.");
    }
    [NativeUseReturn(33900)]
    private void ReturnMoSimaYiUse(CardUseFrame use)
    {
        if (use.MoSimaYiSlash is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId);
        var r = f.MoSimaYi!;
        if (r.NativeUseId != use.Id || r.NativeActionId != o.ActionId || r.NativeReturned || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Zhouxi cannot return a repeated or unrelated native Slash.");
        ReplaceRuntimeTop(f with { MoSimaYi = r with { NativeReturned = true } });
        if (use.CardAttack is not { Active: true }) MoSimaYi.SlashReturned(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 33900)]
    private bool ReturnMoSimaYiAttack(AttackCompletionReceipt receipt)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame
            { MoSimaYi: { Stage: MoSimaYiStage.NativeSlash, NativeReturned: true } r } f || r.NativeUseId != receipt.ResolutionId) return false;
        if (receipt.ProgramFrameId != f.Id) throw new InvalidOperationException("Zhouxi's attack returned to another producer.");
        MoSimaYi.SlashReturned(f.Id);
        return true;
    }
}
