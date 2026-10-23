namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<HappyWeiYanDiscardedBone> DiscardedHappyBones(int actualTurn) => _cardMovements.Where(m => m.TurnNumber == actualTurn && m.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.DiscardPile)
        .Select(m => HappyBoneAt(m.CardId, m.From, m.Sequence - 1) is { } bone ? new HappyWeiYanDiscardedBone(m.CardId, bone.PrintedKind, m.From.OwnerSeat!.Value, bone.Slot, m.Sequence) : null)
        .OfType<HappyWeiYanDiscardedBone>().OrderBy(b => b.Sequence).ToArray();
    private bool IsHappyWeiYanBoneReturnQuery(CharacterState actor, int? id)
    {
        if (id is not { } cardId || cardId <= 0 || !actor.IsAlive) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { HappyWeiYan: { Operation: SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn } receipt } frame && actor.Seat == frame.OwnerSeat && receipt.Cursor >= 0 && receipt.Cursor < receipt.Discarded.Count && receipt.Discarded[receipt.Cursor].CardId == cardId)
            return (receipt.Stage is HappyWeiYanStage.ReturnChoice or HappyWeiYanStage.IssuingUse) && _cardZones.Contains(CardLocation.DiscardPile, cardId);
        return _resolutionStack.LastOrDefault() is CardUseFrame { HappyWeiYanReturn: { } native } use && use.SourceSeat == actor.Seat && native.Bone.CardId == cardId &&
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == native.FrameId) is { } parent && HappyBoneUseRidesOn(use, parent) && _cardZones.Contains(CardLocation.DiscardPile, cardId);
    }
    private Card? HappyWeiYanBoneReturnCard(CharacterState actor, int? id) => IsHappyWeiYanBoneReturnQuery(actor, id) ? GetAdvancedCard(id!.Value) : null;
    private IReadOnlyList<LegalAction> HappyBoneReturnPlans(ProgramSkillFrame frame)
    {
        var receipt = frame.HappyWeiYan!;
        if (receipt.Cursor >= receipt.Discarded.Count || !IsHappyWeiYanBoneReturnQuery(_players[frame.OwnerSeat], receipt.Discarded[receipt.Cursor].CardId) || _winner != Winner.None) return [];
        var bone = receipt.Discarded[receipt.Cursor];
        return Array.AsReadOnly(BuildLegalActions(_players[frame.OwnerSeat], false, bone.CardId).Where(action => action.CardId == bone.CardId && action.ConversionSource is null && (action.AdditionalConversionSources?.Count ?? 0) == 0 &&
            action.MinCardCount <= 1 && action.MaxCardCount <= 1 && action.Kind is not (LegalActionKind.Recast or LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral) &&
            ((action.PlayedCardKind ?? bone.PrintedKind) == bone.PrintedKind || bone.PrintedKind == CardKind.Slash && action.PlayedCardKind == CardKind.FireSlash && HasZhuqueFan(_players[frame.OwnerSeat])))
            .Select(action => action with { TargetSeats = Array.AsReadOnly(action.TargetSeats.ToArray()), AdditionalConversionSources = Array.AsReadOnly((action.AdditionalConversionSources ?? []).ToArray()), SelectableCardIds = Array.AsReadOnly(action.SelectableCardIds.ToArray()),
                SelectableTargetSeats = Array.AsReadOnly(action.SelectableTargetSeats.ToArray()), SelectableCardTargets = action.SelectableCardTargets is null ? null : Array.AsReadOnly(action.SelectableCardTargets.ToArray()),
                SelectableTargetSequences = action.SelectableTargetSequences is null ? null : Array.AsReadOnly(action.SelectableTargetSequences.Select(s => (IReadOnlyList<int>)Array.AsReadOnly(s.ToArray())).ToArray()) }).ToArray());
    }
    private bool ContinueHappyBoneReturn(ProgramSkillFrame initial)
    {
        var id = initial.Id;
        while (_pendingDecision is null)
        {
            var frame = GetActiveProgramFrame(id); RequireHappyWeiYan(frame); var receipt = frame.HappyWeiYan!;
            if (receipt.Stage is HappyWeiYanStage.IssuingUse or HappyWeiYanStage.NativeUse) return true;
            if (receipt.Stage == HappyWeiYanStage.ReturnedUse)
            {
                ReplaceRuntimeTop(frame = frame with { HappyWeiYan = receipt = receipt with { Stage = HappyWeiYanStage.ReturnChoice, Cursor = receipt.Cursor + 1, NativeUse = null } });
            }
            if (receipt.Cursor >= receipt.Discarded.Count || !_players[frame.OwnerSeat].IsAlive || _winner != Winner.None) { FinishHappyWeiYan(frame); return true; }
            if (HappyBoneReturnPlans(frame).Count == 0) { ReplaceRuntimeTop(frame with { HappyWeiYan = receipt with { Cursor = receipt.Cursor + 1 } }); continue; }
            PresentHappyWeiYanAdditional(frame, frame.OwnerSeat, HappyWeiYanAdditionalChoices(frame)); return true;
        }
        return true;
    }
    private IReadOnlyList<int> HappyBoneExpectedTargets(HappyWeiYanBoneUse native)
    {
        var actor = _players[native.Source.OwnerSeat]; var action = native.Action; var card = ApplyProgramUseAppearance(actor, GetAdvancedCard(native.Bone.CardId), null);
        if (GuanduExpectedNativeTargets(actor, card, action) is { } guandu) return guandu;
        if (DragonBoatExpectedNativeTargets(actor, card, action) is { } dragon) return dragon;
        if (action.Kind is not (LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage)) return GetImplicitSelfCardUseTargets(action.PlayedCardKind ?? card.Kind, actor.Seat, action.TargetSeats);
        return Array.AsReadOnly(Enumerable.Range(1, _playerCount - 1).Select(offset => _players[TableStepSeat(actor.Seat, offset)]).Where(p => p.IsAlive && !HasCardPolicy(p, SkillProgramCardPolicyKind.ExcludeGlobalTarget, card.Kind) &&
            !IsCardTargetProhibited(p, card.Kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) && !HasBeneficiarySuitShield(actor.Seat, p.Seat, EffectiveSuit(actor, card)) && !TargetRules.IsDeclaredGlobalProhibited(new(actor.Seat, p.Seat, card.Kind))).Select(p => p.Seat).ToArray());
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 42301)] private void BindHappyWeiYanBoneUse(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent declared || LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use) return;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame { HappyWeiYan: { Stage: HappyWeiYanStage.IssuingUse, NativeUse: { UseId: 0, ActionId: 0 } native } receipt } frame) return;
        RequireHappyWeiYan(frame);
        if (use.SourceSeat != frame.OwnerSeat || action.ActorSeat != frame.OwnerSeat || action.ProviderSeat != frame.OwnerSeat || action.RequesterSeat is not null || action.ConversionChain.Count != 0 ||
            use.CardId != native.Bone.CardId || use.CardKind != (native.Action.PlayedCardKind ?? native.Bone.PrintedKind) || action.PhysicalCards is not [{ CardId: var cardId, From: var from }] || cardId != native.Bone.CardId || from != CardLocation.DiscardPile ||
            use.PhysicalCardIds is not [var physical] || physical != cardId || _cardZones.GetLocation(cardId) != from || !use.TargetSeats.SequenceEqual(HappyBoneExpectedTargets(native)))
            throw new InvalidOperationException("Ezhi must issue the original discarded bone as one genuine legal native Use.");
        var bound = native with { UseId = use.Id, ActionId = action.ActionId };
        ReplaceRuntimeFrame(frame.Id, frame with { HappyWeiYan = receipt with { Stage = HappyWeiYanStage.NativeUse, NativeUse = bound } });
        UpdateLifecycleCardUse(use.Id, owner => owner with { HappyWeiYanReturn = bound });
        AdvanceEventRulesAndQueueFact(new HappyWeiYanBoneUseIssuedEvent(frame.Id, receipt.Cursor, cardId, native.Bone.Sequence, use.Id, action.ActionId));
    }
    private bool HappyBoneUseRidesOn(CardUseFrame use, ProgramSkillFrame frame) => use.HappyWeiYanReturn is { } native && frame.HappyWeiYan is { Operation: SkillProgramEffectOp.ResolveHappyWeiYanBoneReturn, NativeUse: { } pending } receipt &&
        receipt.Stage is HappyWeiYanStage.NativeUse or HappyWeiYanStage.ReturnedUse && native == pending && native.FrameId == frame.Id && native.Source == receipt.Source && native.GameplayHash == frame.GameplayHash && native.ActualTurn == _turnNumber &&
        native.Ordinal == receipt.Cursor && native.Bone == receipt.Discarded[receipt.Cursor] && use.Id == native.UseId && use.SourceSeat == frame.OwnerSeat && use.CardId == native.Bone.CardId && use.CardKind == (native.Action.PlayedCardKind ?? native.Bone.PrintedKind) &&
        use.Action is { Type: CardActionType.Use } action && action.ActorSeat == frame.OwnerSeat && action.ProviderSeat == frame.OwnerSeat && action.ActionId == native.ActionId && action.ConversionChain.Count == 0 &&
        action.PhysicalCards is [{ CardId: var cardId, From: var from }] && cardId == native.Bone.CardId && from == CardLocation.DiscardPile && use.PhysicalCardIds is [var entity] && entity == cardId &&
        ProgramEventHistory<HappyWeiYanBoneUseIssuedEvent>().Count(e => e == new HappyWeiYanBoneUseIssuedEvent(frame.Id, receipt.Cursor, cardId, native.Bone.Sequence, use.Id, native.ActionId)) == 1;
    [NativeUseValidation(42300)] private void ValidateHappyBoneUse(CardUseFrame use)
    {
        if (use.HappyWeiYanReturn is { } native && (use.Step != ResolutionFrameStep.Completed || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == native.FrameId) is not { } parent || !HappyBoneUseRidesOn(use, parent)))
            throw new InvalidOperationException("A returned bone cannot retire another or incomplete native Use.");
    }
    [NativeUseReturn(42300)] private bool ReturnHappyBoneUse(CardUseFrame use)
    {
        if (use.HappyWeiYanReturn is not { } native) return false;
        var frame = GetActiveProgramFrame(native.FrameId);
        if (LifecycleCardUse(use.Id) is not null || use.Step != ResolutionFrameStep.Completed || !HappyBoneUseRidesOn(use, frame) || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) != 1)
            throw new InvalidOperationException("Ezhi requires the single completed native return of its original discarded bone.");
        ReplaceRuntimeTop(frame = frame with { HappyWeiYan = frame.HappyWeiYan! with { Stage = HappyWeiYanStage.ReturnedUse } });
        AdvanceEventRulesAndQueueFact(new HappyWeiYanBoneUseReturnedEvent(frame.Id, native.Ordinal, use.Id, native.ActionId));
        if (use.CardAttack is not { Active: true }) ContinueHappyWeiYan(frame.Id);
        return true;
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 42300)] private bool ResumeHappyBoneAttack()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HappyWeiYan: { Stage: HappyWeiYanStage.ReturnedUse } } frame) return false;
        return ContinueHappyWeiYan(frame.Id);
    }
}
