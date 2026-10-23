namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.MaterialPayment, 40600)]
    private bool PayMobileJwReservedSlash(IReadOnlyList<Card> cards, int provider, CardLocation? source, CardMoveReason reason)
    {
        if (source != CardLocation.Processing || reason != CardMoveReasons.Use || cards is not [var card] ||
            _resolutionStack.LastOrDefault() is not CardUseFrame { MobileJiangWeiReturn: { } ret } use) return false;
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == ret.ParentId);
        if (parent is not { MobileJiangWei: { Stage: MobileJiangWeiStage.SlashIssued } r } || r.Use != ret ||
            provider != parent.OwnerSeat || ret.CardId != card.Id || ret.Kind != card.Kind || ret.UseId != use.Id || ret.ActionId != use.Action?.ActionId ||
            ret.From != CardLocation.Processing || _cardZones.GetLocation(card.Id) != CardLocation.Processing ||
            !r.Shown.Any(s => s.Id == card.Id && s.Kind == card.Kind && s.From == CardLocation.DrawPile &&
                _cardMovements.Count(m => m.Sequence == s.ProcessingSequence && m.CardId == card.Id && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == "mobile.xinghun.show") == 1) ||
            ProgramEventHistory<MobileJiangWeiUseIssuedEvent>().Count(e => e.Return == ret) != 1)
            throw new InvalidOperationException("Xinghun's shown deck Slash requires its already-paid original Processing entry and exact native Use.");
        RequireMobileJw(parent);
        return true;
    }
    private void NextMobileJwSlash(ProgramSkillFrame f)
    {
        var r = f.MobileJiangWei!;
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.Target].IsAlive || _winner != Winner.None || r.Index >= r.Shown.Count)
        { ReturnMobileJwShownTop(f); return; }
        var shown = r.Shown[r.Index]; var from = shown.From == CardLocation.DrawPile ? CardLocation.Processing : shown.From;
        ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Index = r.Index + 1 } }); r = f.MobileJiangWei!;
        var card = GetAttackCard(shown.Id);
        if (!IsSlashCard(shown.Kind) || card.Kind != shown.Kind || _cardZones.GetLocation(card.Id) != from ||
            !CanUseSlashTarget(_players[f.OwnerSeat], _players[r.Target], card, effectiveKind: card.Kind, physicalCardIds: [card.Id]))
        { NextMobileJwSlash(f); return; }
        ReplaceRuntimeTop(f = f with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.SlashIssued, Use = null, Before = _movementSequence, After = _movementSequence } });
        ResolveSlashCore(_players[f.OwnerSeat], _players[r.Target], card, card.Kind, f.OwnerSeat,
            countsTowardSlashLimit: false, programSkillCardUseFrameId: f.Id, physicalSourceLocation: from);
    }
    private bool IsMobileJwXinghunSlashSource(long? parentId, int actor, int target, Card card, CardKind kind, int physicalOwner,
        CardLocation from, IReadOnlyList<Card>? materials, bool countsTowardLimit)
    {
        if (parentId is not { } id || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileJiangWei: { Stage: MobileJiangWeiStage.SlashIssued, Use: null } r } f ||
            f.Id != id || r.Operation != SkillProgramEffectOp.ResolveMobileJiangWeiXinghun || actor != f.OwnerSeat || physicalOwner != actor || target != r.Target ||
            countsTowardLimit || materials is not null || kind != card.Kind || !IsSlashCard(kind) || r.Index < 1 || r.Index > r.Shown.Count ||
            r.Shown[r.Index - 1] is not { } shown || shown.Id != card.Id || shown.Kind != kind || from != (shown.From == CardLocation.DrawPile ? CardLocation.Processing : shown.From) ||
            _cardZones.GetLocation(card.Id) != from) return false;
        RequireMobileJw(f);
        return CanUseSlashTarget(_players[actor], _players[target], card, effectiveKind: kind, physicalCardIds: [card.Id]);
    }
    private void BindMobileJwXinghunSlashUse(long useId, long? parentId)
    {
        if (parentId is not { } id || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(x => x.Id == id) is not { MobileJiangWei: { Stage: MobileJiangWeiStage.SlashIssued, Use: null } r } f) return;
        var shown = r.Shown[r.Index - 1]; var from = shown.From == CardLocation.DrawPile ? CardLocation.Processing : shown.From;
        if (LifecycleCardUse(useId) is not { Action: { Type: CardActionType.Use, PhysicalCards: [var material], ConversionChain.Count: 0 } action } use ||
            action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.RequesterSeat is not null || action.EffectiveKind != shown.Kind ||
            !action.TargetSeats.SequenceEqual([r.Target]) || material.CardId != shown.Id || material.CardKind != shown.Kind || material.From != from ||
            use.SourceSeat != f.OwnerSeat || use.CardId != shown.Id || use.PhysicalCardIds is not [var physical] || physical != shown.Id)
            throw new InvalidOperationException("Xinghun lost its exact public shown physical Slash and native Use declaration.");
        var ret = new MobileJiangWeiUseReturn(f.Id, f.InstructionIndex, r.Source, f.GameplayHash, useId, action.ActionId, shown.Id, shown.Kind, from, r.Target);
        ReplaceRuntimeFrame(f.Id, f with { MobileJiangWei = r with { Use = ret } });
        UpdateLifecycleCardUse(useId, u => u with { MobileJiangWeiReturn = ret, UnlimitedUse = true });
        AdvanceEventRulesAndQueueFact(new MobileJiangWeiUseIssuedEvent(ret));
    }
    [NativeUseValidation(40600)] private void ValidateMobileJwUse(CardUseFrame use)
    {
        if (use.MobileJiangWeiReturn is not { } ret) return;
        if (_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == ret.ParentId) is not { MobileJiangWei: { Stage: MobileJiangWeiStage.SlashIssued } r } parent ||
            r.Use != ret || ret.Source != MobileJwSource(parent) || ret.GameplayHash != parent.GameplayHash || ret.InstructionIndex != parent.InstructionIndex ||
            use.Id != ret.UseId || use.Action?.ActionId != ret.ActionId || use.CardId != ret.CardId || use.CardKind != ret.Kind ||
            use.Action.PhysicalCards is not [var c] || c.CardId != ret.CardId || c.From != ret.From || use.Step != ResolutionFrameStep.Completed ||
            ProgramEventHistory<MobileJiangWeiUseIssuedEvent>().Count(e => e.Return == ret) != 1)
            throw new InvalidOperationException("Xinghun cannot replace its whole native Slash return.");
    }
    [NativeUseReturn(40600)] private void ReturnMobileJwUse(CardUseFrame use)
    {
        if (use.MobileJiangWeiReturn is not { } ret) return;
        var parent = GetActiveProgramFrame(ret.ParentId); var r = parent.MobileJiangWei!;
        if (r.Use != ret || r.Stage != MobileJiangWeiStage.SlashIssued || LifecycleCardUse(use.Id) is not null ||
            ProgramEventHistory<MobileJiangWeiUseReturnedEvent>().Any(e => e.UseId == use.Id) ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Xinghun must wait for its once-paid physical Slash and complete native card cleanup.");
        ReplaceRuntimeFrame(parent.Id, parent = parent with { MobileJiangWei = r with { Stage = MobileJiangWeiStage.SlashReturned } });
        AdvanceEventRulesAndQueueFact(new MobileJiangWeiUseReturnedEvent(parent.Id, use.Id, ret.ActionId));
        if (use.CardAttack is not { Active: true }) ContinueMobileJw(parent.Id);
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 40600)] private bool ReturnMobileJwAttack(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileJiangWei: { Stage: MobileJiangWeiStage.SlashReturned, Use: { } ret } } f || ret.UseId != completion.ResolutionId) return false;
        if (completion.ProgramFrameId != f.Id || LifecycleCardUse(ret.UseId) is not null) throw new InvalidOperationException("Xinghun attack completed outside its original Use parent.");
        ContinueMobileJw(f.Id); return true;
    }
}
