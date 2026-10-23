namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<ProgramTriggerCandidate> CollectIssuedAnniversaryGuoZhaoCandidates(CardMovementBatchContext batch)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor)) yield break;
        foreach (var source in ProgramEventHistory<AnniversaryGuoZhaoLeaseEvent>().Select(e => (e.Source.OwnerSeat, e.Source.SkillInstanceId, e.Hash)).Distinct())
        {
            if (!_players[source.OwnerSeat].IsAlive || GuoZhaoLostBenefits(source.OwnerSeat, source.SkillInstanceId, source.Hash, batch).Count == 0) continue;
            var p = _contentRegistry.GetSkill(AnniversaryGuoZhaoComposition.Pianchong).Program!;
            if (p.GameplayHash != source.Hash) throw new InvalidOperationException("Pianchong's issued duration cannot cross a changed program hash.");
            var t = p.Triggers.Single(t => t.Id == "lost-color-benefit");
            yield return new(source.OwnerSeat, p.Id, t.Id, source.SkillInstanceId, source.Hash, t.Priority);
        }
    }
    private bool IsIssuedAnniversaryGuoZhaoCandidate(ProgramTriggerCandidate c) => c.SkillId == AnniversaryGuoZhaoComposition.Pianchong && c.BindingId == "lost-color-benefit" &&
        ProgramEventHistory<AnniversaryGuoZhaoLeaseEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillInstanceId == c.SkillInstanceId && e.Hash == c.GameplayHash);
    private bool CanRunIssuedAnniversaryGuoZhaoCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsIssuedAnniversaryGuoZhaoCandidate(c) || !_players[c.OwnerSeat].IsAlive || c.GameplayHash != _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash ||
            context is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch } || context.OwnerSeat != c.OwnerSeat || context.SourceSeat != c.OwnerSeat ||
            context.TargetSeat != c.OwnerSeat || _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } w ||
            w.Batch.Id != batch.Id || !w.Batch.Movements.SequenceEqual(batch.Movements) || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex] != c) return false;
        return GuoZhaoLostBenefits(c.OwnerSeat, c.SkillInstanceId, c.GameplayHash, batch).Count > 0;
    }
    private Card[] GuoZhaoEquipmentCandidates(ProgramSkillFrame f)
    {
        var owner = _players[f.OwnerSeat];
        return !owner.IsAlive || _winner != Winner.None ? [] : _cardZones.CardsAt(CardLocation.DrawPile).Where(c => EquipmentCatalog.IsEquipment(c.Kind) && !c.IsGeneralWeapon &&
            owner.EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot) > 0 &&
            !IsCardUseForbidden(owner.Seat, c.Kind, CardActionType.Use) && !IsSelfTargetForbiddenAction(owner, c.Kind, [owner.Seat]) &&
            !IsResponseEntityRestricted(owner.Seat, c.Id) && !IsPlayPhasePhysicalCardRestricted(owner, c)).OrderBy(c => c.Id).ToArray();
    }
    private void BeginGuoZhaoEquipment(ProgramSkillFrame f)
    {
        var r = f.AnniversaryGuoZhao!;
        if (!ValidGuoZhao(f) || !r.Paid || r.Option != "equipment" || r.Stage is not (AnniversaryGuoZhaoStage.EquipmentNext or AnniversaryGuoZhaoStage.EquipmentChildren))
            throw new InvalidOperationException("Zunwei equipment needs its original paid option and returned whole equipment Use.");
        if (!_players[f.OwnerSeat].IsAlive || !_players[r.Target].IsAlive || GetEquipment(f.OwnerSeat).Count >= GetEquipment(r.Target).Count || _winner != Winner.None) { FinishGuoZhao(f); return; }
        var cards = GuoZhaoEquipmentCandidates(f);
        if (cards.Length == 0) { FinishGuoZhao(f); return; }
        var before = _random.State; var card = cards[_random.Next(cards.Length)]; var after = _random.State;
        var receipt = new AnniversaryGuoZhaoEquipment(r.Cursor, card.Id, card.Kind, 0, 0, before, after) { Candidates = cards.Select(c => c.Id).ToArray() };
        ReplaceRuntimeTop(f = f with { AnniversaryGuoZhao = r with { Stage = AnniversaryGuoZhaoStage.NativeEquipment, Equipment = receipt, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var id = BeginCardUse(card, f.OwnerSeat, [], conversionSource: r.Source);
        var use = LifecycleCardUse(id) ?? throw new InvalidOperationException("Zunwei needs its actual physical equipment Use.");
        var actionId = use.Action!.ActionId;
        f = _resolutionStack.OfType<ProgramSkillFrame>().Single(x => x.Id == f.Id);
        ReplaceRuntimeFrame(f.Id, f = f with { AnniversaryGuoZhao = f.AnniversaryGuoZhao! with { Equipment = receipt with { UseId = id, ActionId = actionId } } });
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoEquipmentIssuedEvent(f.Id, r.Cursor, card.Id, card.Kind, id, actionId));
        MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(_players[f.OwnerSeat], card, id)) CompleteEquipmentUse(_players[f.OwnerSeat], card, id);
    }
    private bool IsAnniversaryGuoZhaoNativeEquipment(ResolutionFrame child, ResolutionFrame parent)
    {
        if (child is not CardUseFrame use || parent is not ProgramSkillFrame { AnniversaryGuoZhao: { Paid: true, Option: "equipment", Stage: AnniversaryGuoZhaoStage.NativeEquipment, Equipment: { } equipment } r } f ||
            !ValidGuoZhao(f) || use.Id != equipment.UseId || use.CardId != equipment.CardId || use.CardKind != equipment.Kind || !(use.PhysicalCardIds ?? [use.CardId]).SequenceEqual([equipment.CardId]) ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost] } action || action.ActionId != equipment.ActionId || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat ||
            action.EffectiveKind != equipment.Kind || cost.CardId != equipment.CardId || cost.From != CardLocation.DrawPile || !action.ConversionChain.Contains(r.Source)) return false;
        return ProgramEventHistory<AnniversaryGuoZhaoEquipmentIssuedEvent>().Count(e => e == new AnniversaryGuoZhaoEquipmentIssuedEvent(f.Id, equipment.Ordinal, equipment.CardId, equipment.Kind, equipment.UseId, equipment.ActionId)) == 1;
    }
    [NativeUseValidation(44300)] private void ValidateGuoZhaoEquipment(CardUseFrame use)
    {
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.AnniversaryGuoZhao?.Equipment?.UseId == use.Id);
        if (parent is not null && (use.Step != ResolutionFrameStep.Completed || _resolutionStack.LastOrDefault()?.Id != use.Id || !IsAnniversaryGuoZhaoNativeEquipment(use, parent)))
            throw new InvalidOperationException("Zunwei cannot complete a different or unfinished equipment Use.");
    }
    [NativeUseReturn(44300)] private bool ReturnGuoZhaoEquipment(CardUseFrame use)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryGuoZhao: { Stage: AnniversaryGuoZhaoStage.NativeEquipment, Equipment: { } paid } r } f || paid.UseId != use.Id) return false;
        if (_resolutionStack.Any(x => x.Id == use.Id) || !IsAnniversaryGuoZhaoNativeEquipment(use, f) || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == paid.CardId) != 1 ||
            ProgramEventHistory<AnniversaryGuoZhaoEquipmentReturnedEvent>().Any(e => e.FrameId == f.Id && e.Ordinal == paid.Ordinal))
            throw new InvalidOperationException("Zunwei must return one exact whole physical equipment Use before its next random choice.");
        AdvanceEventRulesAndQueueFact(new AnniversaryGuoZhaoEquipmentReturnedEvent(f.Id, paid.Ordinal, use.Id, paid.ActionId));
        ReplaceRuntimeTop(f with { AnniversaryGuoZhao = r with { Stage = AnniversaryGuoZhaoStage.EquipmentChildren, Cursor = r.Cursor + 1, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        ContinueGuoZhao(f.Id); return true;
    }
}
