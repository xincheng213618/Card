namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46500)] private void BindWangChangSlash(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { } use) return;
        var index = _resolutionStack.FindIndex(x => x.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { AnniversaryWangChang: { Stage: AnniversaryWangChangStage.IssuingSlash } r } f) return;
        if (!ValidWangChang(f) || r.Cursor != r.Targets.Count || r.SlashCursor >= r.Targets.Count || use.CardId != 0 || use.CardKind != CardKind.Slash || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } a || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.RequesterSeat is not null ||
            !a.ConversionChain.SequenceEqual([r.Source]) || !a.TargetSeats.SequenceEqual([r.Targets[r.SlashCursor]])) throw new InvalidOperationException("Pingxi cannot substitute another actor, target cursor or non-native Slash.");
        var origin = new AnniversaryWangChangSlashOrigin(f.Id, r.Source, r.Hash, r.SlashCursor, use.Id, a.ActionId, r.Targets[r.SlashCursor]);
        ReplaceRuntimeFrame(f.Id, f with { AnniversaryWangChang = r with { Stage = AnniversaryWangChangStage.NativeSlash, UseId = use.Id, ActionId = a.ActionId } });
        UpdateLifecycleCardUse(use.Id, u => u with { AnniversaryWangChangSlash = origin });
        AdvanceEventRulesAndQueueFact(new AnniversaryWangChangSlashIssuedEvent(f.Id, r.SlashCursor, use.Id, a.ActionId, origin.TargetSeat));
    }
    private bool IsWangChangSlash(CardUseFrame use)
    {
        if (use.AnniversaryWangChangSlash is not { } o || use.Id != o.UseId || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } a || a.ActionId != o.ActionId || a.ProviderSeat != o.Source.OwnerSeat || a.RequesterSeat is not null ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is not ProgramSkillFrame { AnniversaryWangChang: { Stage: AnniversaryWangChangStage.NativeSlash } r } f || !ValidWangChang(f) ||
            r.Source != o.Source || r.Hash != o.Hash || r.SlashCursor != o.Cursor || r.UseId != use.Id || r.ActionId != a.ActionId || r.Targets[r.SlashCursor] != o.TargetSeat ||
            !a.ConversionChain.Contains(o.Source) || ProgramEventHistory<AnniversaryWangChangSlashIssuedEvent>().Count(e => e == new AnniversaryWangChangSlashIssuedEvent(f.Id, o.Cursor, o.UseId, o.ActionId, o.TargetSeat)) != 1) return false;
        return ShownEntityUseActorMatches(use, o.Source.OwnerSeat, o.Source.OwnerSeat);
    }
    [NativeUseValidation(46500)] private void ValidateWangChangSlash(CardUseFrame use)
    { if (use.AnniversaryWangChangSlash is not null && (!IsWangChangSlash(use) || use.Step != ResolutionFrameStep.Completed)) throw new InvalidOperationException("Pingxi must finish its exact real native Slash before advancing the original group."); }
    [NativeUseReturn(46500)] private void ReturnWangChangSlash(CardUseFrame use)
    {
        if (use.AnniversaryWangChangSlash is not { } origin) return;
        var f = GetActiveProgramFrame(origin.FrameId); var r = f.AnniversaryWangChang!;
        if (!IsWangChangSlash(use) || r.NativeReturned || LifecycleCardUse(use.Id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Pingxi cannot return an unfinished, repeated or unrelated native Use.");
        ReplaceRuntimeTop(f with { AnniversaryWangChang = r with { NativeReturned = true } });
        if (use.CardAttack is not { Active: true }) CompleteWangChangSlash(f.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 46500)] private bool ReturnWangChangAttack(AttackCompletionReceipt receipt)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryWangChang: { Stage: AnniversaryWangChangStage.NativeSlash, NativeReturned: true } r } f || r.UseId != receipt.ResolutionId) return false;
        if (receipt.ProgramFrameId != f.Id) throw new InvalidOperationException("Pingxi returned its native attack to another owner frame.");
        CompleteWangChangSlash(f.Id); return true;
    }
    private void CompleteWangChangSlash(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.AnniversaryWangChang!;
        if (!ValidWangChang(f) || !r.NativeReturned || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<AnniversaryWangChangSlashReturnedEvent>().Any(e => e.FrameId == f.Id && e.Cursor == r.SlashCursor))
            throw new InvalidOperationException("Pingxi's original group must receive each whole native Slash exactly once.");
        AdvanceEventRulesAndQueueFact(new AnniversaryWangChangSlashReturnedEvent(f.Id, r.SlashCursor, r.UseId, r.ActionId));
        ReplaceRuntimeTop(f = f with { SelectedTargetSeats = [], AnniversaryWangChang = r with { Stage = AnniversaryWangChangStage.ReturnedSlash, SlashCursor = r.SlashCursor + 1 } });
        ContinueWangChang(f.Id);
    }
}
