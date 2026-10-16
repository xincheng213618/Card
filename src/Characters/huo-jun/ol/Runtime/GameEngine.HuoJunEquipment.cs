namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueHuoJunRandomEquipment(ProgramSkillFrame f)
    {
        var r = f.HuoJunFenrui!; var actor = _players[f.OwnerSeat];
        if (r.RestoredSlot is not { } slot || r.Stage != HuoJunFenruiStage.RestoreChildren)
            throw new InvalidOperationException("Fenrui equipment must follow its one actually restored native slot.");
        var candidates = actor.IsAlive && actor.Hp > 0 ? _cardZones.CardsAt(CardLocation.DrawPile).Where(c => EquipmentCatalog.IsEquipment(c.Kind) &&
            !c.IsGeneralWeapon && EquipmentCatalog.Get(c.Kind).Slot == slot && actor.EquipmentSlotCapacity(slot) > 0 &&
            !IsCardUseForbidden(actor.Seat, c.Kind, CardActionType.Use) && !IsSelfTargetForbiddenAction(actor, c.Kind, [actor.Seat]) &&
            !IsResponseEntityRestricted(actor.Seat, c.Id) && !IsPlayPhasePhysicalCardRestricted(actor, c)).OrderBy(c => c.Id).ToArray() : [];
        if (candidates.Length == 0)
        {
            ReplaceRuntimeTop(f with { HuoJunFenrui = r with { Stage = HuoJunFenruiStage.EquipmentChildren } });
            ContinueHuoJunFenrui(f.Id); return;
        }
        var card = candidates[_random.Next(candidates.Length)];
        ReplaceRuntimeTop(f = f with { HuoJunFenrui = r with { Stage = HuoJunFenruiStage.NativeEquipment,
            EquipmentCardId = card.Id, EquipmentKind = card.Kind } });
        var useId = BeginCardUse(card, actor.Seat, [], conversionSource: r.Source);
        var actionId = LifecycleCardUse(useId)?.Action?.ActionId ?? throw new InvalidOperationException("Fenrui random equipment requires an actual native accepted use action.");
        ReplaceRuntimeFrame(f.Id, f with { HuoJunFenrui = f.HuoJunFenrui! with { NativeUseId = useId, NativeActionId = actionId } });
        AdvanceEventRulesAndQueueFact(new HuoJunFenruiEquipmentIssuedEvent(f.Id, r.Source, slot, card.Id, card.Kind, useId, actionId));
        MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(actor, card, useId)) CompleteEquipmentUse(actor, card, useId);
    }
    private bool IsHuoJunFenruiNativeEquipment(ResolutionFrame ride, ResolutionFrame beneath)
    {
        if (ride is not CardUseFrame use || beneath is not ProgramSkillFrame { HuoJunFenrui: { Stage: HuoJunFenruiStage.NativeEquipment } r } f ||
            r.Source != HuoJunSource(f) || r.NativeUseId != use.Id || r.NativeActionId != use.Action?.ActionId ||
            r.EquipmentCardId != use.CardId || r.EquipmentKind != use.CardKind || r.RestoredSlot is not { } slot ||
            EquipmentCatalog.Get(use.CardKind).Slot != slot || use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat || action.EffectiveKind != use.CardKind ||
            action.PhysicalCards is not [var cost] || cost.CardId != use.CardId || cost.CardKind != use.CardKind || cost.From != CardLocation.DrawPile ||
            !action.ConversionChain.Contains(r.Source)) return false;
        return ProgramEventHistory<HuoJunFenruiEquipmentIssuedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source &&
            e.Slot == slot && e.CardId == use.CardId && e.CardKind == use.CardKind && e.NativeUseId == use.Id && e.NativeActionId == action.ActionId) == 1;
    }
    [NativeUseValidation(26210)]
    private void ValidateHuoJunEquipmentReturn(CardUseFrame use)
    {
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.HuoJunFenrui?.NativeUseId == use.Id);
        if (parent is null) return;
        if (use.Step != ResolutionFrameStep.Completed || _resolutionStack.LastOrDefault()?.Id != use.Id || !IsHuoJunFenruiNativeEquipment(use, parent))
            throw new InvalidOperationException("Fenrui must finish the exact paid physical random equipment through its original native use.");
    }
    [NativeUseReturn(26210)]
    private bool ReturnHuoJunEquipment(CardUseFrame use)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HuoJunFenrui: { Stage: HuoJunFenruiStage.NativeEquipment } r } f || r.NativeUseId != use.Id) return false;
        if (_resolutionStack.Any(frame => frame.Id == use.Id) || !IsHuoJunFenruiNativeEquipment(use, f) ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == r.EquipmentCardId) != 1 ||
            ProgramEventHistory<HuoJunFenruiEquipmentReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Fenrui random equipment may return only after the one actual native use finishes.");
        AdvanceEventRulesAndQueueFact(new HuoJunFenruiEquipmentReturnedEvent(f.Id, use.Id, r.NativeActionId));
        ReplaceRuntimeTop(f with { HuoJunFenrui = r with { Stage = HuoJunFenruiStage.EquipmentChildren } });
        ContinueHuoJunFenrui(f.Id); return true;
    }
}
