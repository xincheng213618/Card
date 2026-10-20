namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 33400)]
    private bool MouDengAiJiewanDistance(ProgramCardDistanceQuery q)
    {
        if (q.Kind != CardKind.Snatch) return false;
        var f = q.CardUseFrameId is { } id && LifecycleCardUse(id) is { MouDengAiJiewan: { } o } ? _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(candidate => candidate.Id == o.FrameId) :
            _resolutionStack.LastOrDefault() as ProgramSkillFrame;
        return f is { MouDengAiOl: { Operation: SkillProgramEffectOp.UseMouDengAiJiewan } r } && ValidMouDengAiOl(f) && f.OwnerSeat == q.Actor.Seat &&
            r.Stage is MouDengAiOlStage.Cost or MouDengAiOlStage.Material or MouDengAiOlStage.Snatch or MouDengAiOlStage.Issuing or MouDengAiOlStage.NativeUse &&
            r.Distance > 0 && GetCombatDistance(q.Actor.Seat, q.Target.Seat) <= r.Distance;
    }
    private IReadOnlyList<ProgramOrdinaryTrickUseOption> MouDengAiSnatchOptions(ProgramSkillFrame f, int id)
    {
        if (_cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat)) return [];
        var card = GetAdvancedCard(id); var owner = _players[f.OwnerSeat];
        if (IsTurnHandCardRestricted(owner, card) || IsTurnPhysicalUseForbidden(owner.Seat, [id])) return [];
        var suit = EffectiveSuit(owner, card); var color = CapturePhysicalCardColor(owner.Seat, card);
        return BuildProgramOrdinaryTrickUseOptions(owner, CardKind.Snatch, suit, enforceUsePermission: true, beneficiaryShieldSuit: suit,
            actualEffectiveColor: color, hasActualColor: true, physicalCardIds: [id], includeNextActualUseAdjustment: true, conversionSource: MouDengAiSource(f));
    }
    private void IssueMouDengAiSnatch(ProgramSkillFrame f, ProgramOrdinaryTrickUseOption selected)
    {
        var r = f.MouDengAiOl!;
        if (!ValidMouDengAiOl(f) || !r.Paid || r.Stage != MouDengAiOlStage.Snatch || _cardZones.GetLocation(r.CardId) != CardLocation.Hand(f.OwnerSeat)) throw new InvalidOperationException("Jiewan must use its actual current Hand entity after its accepted cost.");
        var option = MouDengAiSnatchOptions(f, r.CardId).Single(candidate => candidate.Id == selected.Id);
        var card = GetAdvancedCard(r.CardId); var o = new MouDengAiSnatchOrigin(f.Id, r.Source, f.GameplayHash, _resolutionSequence + 1, _cardActionSequence + 1, card.Id, r.Distance);
        ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.Issuing, UseId = o.UseId, ActionId = o.ActionId } });
        AdvanceEventRulesAndQueueFact(new MouDengAiSnatchIssuedEvent(o));
        if (option.NextActualUseAdjusted) _selectedNextCardTargetSeats = option.TargetSeats;
        var id = BeginCardUse(card, f.OwnerSeat, option.TargetSeats, CardKind.Snatch, physicalCardIds: [card.Id], conversionSource: r.Source);
        if (id != o.UseId || !IsMouDengAiSnatch(id)) throw new InvalidOperationException("Jiewan's declaration changed its original native identity.");
        MoveCard(card, CardLocation.Hand(f.OwnerSeat), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, f.SkillId, GetProgramBindingId(f), f.OwnerSeat, [card.Id], CardKind.Snatch, true, option.TargetSeats));
        BeginJizhiOrNullificationWindow(id, card, f.OwnerSeat, option.TargetSeats, option.ActionKind, option.TargetCardId, option.RequiredCardKind, CardKind.Snatch);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 33401)]
    private void ObserveMouDengAiSnatchDeclaration(IGameEvent fact)
    {
        if (fact is not CardUseDeclaredEvent d || LifecycleCardUse(d.ResolutionId) is not { Action: { } a } u) return;
        var index = _resolutionStack.FindIndex(x => x.Id == u.Id);
        if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame { MouDengAiOl: { Stage: MouDengAiOlStage.Issuing } r } f) return;
        var o = ProgramEventHistory<MouDengAiSnatchIssuedEvent>().Single(e => e.Origin.FrameId == f.Id).Origin;
        if (!ValidMouDengAiOl(f) || o.UseId != u.Id || o.ActionId != a.ActionId || u.CardKind != CardKind.Snatch || u.CardId != o.CardId || a.Type != CardActionType.Use || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat ||
            !a.PhysicalCards.Select(c => c.CardId).SequenceEqual([o.CardId]) || !a.ConversionChain.SequenceEqual([r.Source])) throw new InvalidOperationException("Jiewan cannot attach its receipt to another producer.");
        UpdateLifecycleCardUse(u.Id, x => x with { MouDengAiJiewan = o });
        ReplaceRuntimeFrame(f.Id, f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.NativeUse } });
    }
    private bool IsMouDengAiSnatch(long id)
    {
        if (LifecycleCardUse(id) is not { MouDengAiJiewan: { } o, Action: { Type: CardActionType.Use } a } u || u.Id != o.UseId || u.CardId != o.CardId || u.CardKind != CardKind.Snatch || a.ActionId != o.ActionId || a.EffectiveKind != CardKind.Snatch ||
            a.ProviderSeat != o.Source.OwnerSeat || !a.PhysicalCards.Select(c => c.CardId).SequenceEqual([o.CardId]) || !a.ConversionChain.SequenceEqual([o.Source]) ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.FrameId) is not ProgramSkillFrame { MouDengAiOl: { Stage: MouDengAiOlStage.NativeUse, NativeReturned: false } r } f || !ValidMouDengAiOl(f) || !r.Paid ||
            r.UseId != id || r.ActionId != a.ActionId || r.CardId != o.CardId || r.Distance != o.Distance || r.Source != o.Source || r.GameplayHash != o.GameplayHash ||
            ProgramEventHistory<MouDengAiSnatchIssuedEvent>().Count(e => e.Origin == o) != 1) return false;
        return ShownEntityUseActorMatches(u, o.Source.OwnerSeat, o.Source.OwnerSeat);
    }
    [NativeUseValidation(33400)]
    private void ValidateMouDengAiSnatch(CardUseFrame u)
    { if (u.MouDengAiJiewan is not null && (!IsMouDengAiSnatch(u.Id) || u.Step != ResolutionFrameStep.Completed || u.PendingRecoveryAttempts is { Count: > 0 } || u.RecoveryPaidContinuation is not null)) throw new InvalidOperationException("Jiewan must complete its entire native Snatch and children before returning."); }
    [NativeUseReturn(33400)]
    private void ReturnMouDengAiSnatch(CardUseFrame u)
    {
        if (u.MouDengAiJiewan is not { } o) return;
        var f = GetActiveProgramFrame(o.FrameId); var r = f.MouDengAiOl!;
        if (!ValidMouDengAiOl(f) || r.NativeReturned || r.UseId != u.Id || r.ActionId != o.ActionId || LifecycleCardUse(u.Id) is not null) throw new InvalidOperationException("Jiewan can return once only to its original program.");
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == u.Id && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId).ToArray();
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new MouDengAiNativeBatchEvent(f.Id, u.Id, b.Id, b.ParentBatchId, b.Movements.Count));
        ReplaceRuntimeTop(f = f with { MouDengAiOl = r with { Stage = MouDengAiOlStage.Children, NativeReturned = true, NativeBatches = batches.Select(b => b.Id).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouDengAiSnatchReturnedEvent(f.Id, u.Id, o.ActionId)); ContinueMouDengAiOl(f.Id);
    }
    private bool IsMouDengAiNativeSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.MouDengAiOl is { Stage: MouDengAiOlStage.Children, NativeReturned: true } r && ValidMouDengAiOl(f) && r.NativeBatches.Contains(b.Id) && b.ParentFrameId == r.UseId && b.AwaitingProgramFrameId is null &&
        b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId && b.Movements.All(m => _cardMovements.Contains(m)) &&
        ProgramEventHistory<MouDengAiNativeBatchEvent>().Count(e => e == new MouDengAiNativeBatchEvent(f.Id, r.UseId, b.Id, b.ParentBatchId, b.Movements.Count)) == 1;
}
