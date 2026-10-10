namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidPlacedEquipmentBenefit(ProgramSkillFrame f)
    {
        if (!PlacedEquipmentParentMatches(f) || f.PlacedEquipmentBenefit is not { } r || !Enum.IsDefined(r.Stage) ||
            r.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            r.TurnNumber != _turnNumber || r.TurnOwnerSeat != _currentSeat || r.EndingFrameId != f.WindowContext!.ParentFrameId ||
            r.OccurrenceIndex != f.WindowContext.OccurrenceIndex || string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) ||
            CompleteProgramEventHistory().OfType<PlacedEquipmentBenefitStartedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.TurnNumber == r.TurnNumber && e.TurnOwnerSeat == r.TurnOwnerSeat &&
                e.EndingFrameId == r.EndingFrameId && e.OccurrenceIndex == r.OccurrenceIndex) != 1) return false;
        if ((r.SelectedCardId is null) != (r.SelectedFrom is null) || r.SelectedCardId is { } selected &&
            (selected <= 0 || r.SelectedFrom!.Value.OwnerSeat != f.OwnerSeat || r.SelectedFrom.Value.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var placementFacts = history.OfType<PlacedEquipmentBenefitPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.Placement is not { } p)
            return r.Stage is PlacedEquipmentBenefitStage.EquipmentChoice or PlacedEquipmentBenefitStage.RecipientChoice &&
                (r.Stage == PlacedEquipmentBenefitStage.EquipmentChoice ? r.SelectedCardId is null : r.SelectedCardId is not null) &&
                placementFacts.Length == 0 && r.Discard is null && r.Draw is null && !r.RecoveryIssued && r.ActualRecoveryRequest == 0 && r.WeaponTargetSeat is null && f.PendingMovementContinuation is null;
        if (r.Stage is PlacedEquipmentBenefitStage.EquipmentChoice or PlacedEquipmentBenefitStage.RecipientChoice ||
            p.CardId != r.SelectedCardId || p.From != r.SelectedFrom || !IsValidPlayerSeat(p.RecipientSeat) || p.CardId <= 0 ||
            !EquipmentCatalog.IsEquipment(p.PrintedKind) || p.Slot != EquipmentCatalog.Get(p.PrintedKind).Slot ||
            p.From == CardLocation.Equipment(p.RecipientSeat) || p.SequenceBefore < 0 || p.SequenceAfter <= p.SequenceBefore ||
            placementFacts is not [var placementFact] || placementFact.Payment != p || !ValidPlacedEquipmentLedger(p)) return false;
        if (r.WeaponTargetSeat is { } target && (!IsValidPlayerSeat(target) || target == p.RecipientSeat) ||
            r.Stage is PlacedEquipmentBenefitStage.WeaponTarget or PlacedEquipmentBenefitStage.WeaponCard or PlacedEquipmentBenefitStage.DiscardChildren && p.Slot != EquipmentSlot.Weapon ||
            r.Stage is PlacedEquipmentBenefitStage.WeaponCard or PlacedEquipmentBenefitStage.DiscardChildren && r.WeaponTargetSeat is null ||
            r.Stage == PlacedEquipmentBenefitStage.DrawChildren && (p.Slot != EquipmentSlot.Armor || r.Draw is null) ||
            r.Stage == PlacedEquipmentBenefitStage.RecoveryChildren && (p.Slot is not (EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse) || !r.RecoveryIssued)) return false;
        var discardFacts = history.OfType<PlacedEquipmentBenefitDiscardedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.Discard is { } d)
        {
            if (r.Stage != PlacedEquipmentBenefitStage.DiscardChildren || d.TargetSeat != r.WeaponTargetSeat || d.CardId <= 0 ||
                d.From.OwnerSeat != d.TargetSeat || d.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) ||
                d.SequenceBefore < p.SequenceAfter || d.SequenceAfter <= d.SequenceBefore || discardFacts is not [var fact] || fact.Payment != d) return false;
            var to = d.GeneralWeapon && d.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile;
            var ledger = _cardMovements.Where(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter).ToArray();
            if (GetAdvancedCard(d.CardId).IsGeneralWeapon != d.GeneralWeapon || ledger.Count(m => m.CardId == d.CardId &&
                m.CardKind == d.PrintedKind && m.From == d.From && m.To == to && m.Reason.Value == PlacedEquipmentDiscardReason) != 1 ||
                ledger.Any(m => !(m.CardId == d.CardId && m.From == d.From && m.To == to && m.Reason.Value == PlacedEquipmentDiscardReason) &&
                    !(d.PrintedKind == CardKind.WoodenOx && d.From.Zone == CardZoneKind.Equipment && m.From == CardLocation.WoodenOxGrain(d.TargetSeat) &&
                        m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard))) return false;
        }
        else if (discardFacts.Length != 0) return false;
        var drawFacts = history.OfType<PlacedEquipmentBenefitDrawnEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.Draw is { } draw)
        {
            if (r.Stage != PlacedEquipmentBenefitStage.DrawChildren || draw.RecipientSeat != p.RecipientSeat || draw.ActualCount is < 0 or > 1 ||
                draw.SequenceBefore < p.SequenceAfter || draw.SequenceAfter < draw.SequenceBefore || drawFacts is not [var fact] || fact.Invoice != draw) return false;
            var ledger = _cardMovements.Where(m => m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter).ToArray();
            if (ledger.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.RecipientSeat) && m.Reason.Value == PlacedEquipmentDrawReason) != draw.ActualCount ||
                ledger.Any(m => !(m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.RecipientSeat) && m.Reason.Value == PlacedEquipmentDrawReason) &&
                    !(m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle))) return false;
        }
        else if (drawFacts.Length != 0) return false;
        var recoverFacts = history.OfType<PlacedEquipmentBenefitRecoveryIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.RecoveryIssued ? r.Stage != PlacedEquipmentBenefitStage.RecoveryChildren || r.ActualRecoveryRequest is < 0 or > 1 ||
            recoverFacts is not [var recovery] || recovery.RecipientSeat != p.RecipientSeat || recovery.RequestedAmount != r.ActualRecoveryRequest
            : r.ActualRecoveryRequest != 0 || recoverFacts.Length != 0) return false;
        return true;
    }
    private bool ValidPlacedEquipmentLedger(PlacedEquipmentPayment p)
    {
        if (GetAdvancedCard(p.CardId).IsGeneralWeapon) return false;
        var ledger = _cardMovements.Where(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter).ToArray();
        if (ledger.Count(m => m.CardId == p.CardId && m.CardKind == p.PrintedKind && m.From == p.From &&
            m.To == CardLocation.Equipment(p.RecipientSeat) && m.Reason == CardMoveReasons.EquipmentEnter) != 1) return false;
        var replacements = ledger.Where(m => m.From == CardLocation.Equipment(p.RecipientSeat) && m.Reason == CardMoveReasons.EquipmentReplace).ToArray();
        if (p.ReplacedCardId is { } replaced)
        {
            if (replaced == p.CardId || replacements is not [var replacement] || replacement.CardId != replaced ||
                GetAdvancedCard(replaced).IsGeneralWeapon != p.ReplacedGeneralWeapon || !EquipmentCatalog.IsEquipment(replacement.CardKind) ||
                replacement.To != (p.ReplacedGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile)) return false;
        }
        else if (p.ReplacedGeneralWeapon || replacements.Length != 0) return false;
        return ledger.All(m => m.CardId == p.CardId && m.From == p.From && m.To == CardLocation.Equipment(p.RecipientSeat) && m.Reason == CardMoveReasons.EquipmentEnter ||
            p.ReplacedCardId == m.CardId && m.From == CardLocation.Equipment(p.RecipientSeat) &&
                m.To == (p.ReplacedGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile) && m.Reason == CardMoveReasons.EquipmentReplace ||
            p.PrintedKind == CardKind.WoodenOx && p.From.Zone == CardZoneKind.Equipment && m.From == CardLocation.WoodenOxGrain(p.From.OwnerSeat!.Value) &&
                m.To == CardLocation.WoodenOxGrain(p.RecipientSeat) && m.Reason == CardMoveReasons.WoodenOxTransfer ||
            p.ReplacedCardId is { } ox && GetAdvancedCard(ox).Kind == CardKind.WoodenOx && m.From == CardLocation.WoodenOxGrain(p.RecipientSeat) &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard);
    }
    private bool PlacedEquipmentFirstObserver(int index, ProgramSkillFrame root)
    {
        var r = root.PlacedEquipmentBenefit!; var child = _resolutionStack[index + 1]; var p = r.Placement!;
        if (r.Stage == PlacedEquipmentBenefitStage.RecoveryChildren)
        {
            if (child is RecoveryReplacementFrame replacement)
                return RecoveryReplacementFrameRidesOn(replacement, root) && replacement.Return.Continuation == PostEventContinuation.Program &&
                    replacement.Attempt.SourceSeat == root.OwnerSeat && replacement.Attempt.TargetSeat == p.RecipientSeat &&
                    replacement.Attempt.Amount == r.ActualRecoveryRequest && replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.Program;
            return child is HpChangedTriggerWindowFrame hp && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.Program &&
                hp.Change.ParentFrameId == root.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == root.OwnerSeat &&
                hp.Change.TargetSeat == p.RecipientSeat && hp.Change.Amount == r.ActualRecoveryRequest && r.ActualRecoveryRequest > 0;
        }
        if (root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat) return false;
        long before, after;
        if (r.Stage == PlacedEquipmentBenefitStage.PlacementChildren) { before = p.SequenceBefore; after = p.SequenceAfter; }
        else if (r.Stage == PlacedEquipmentBenefitStage.DiscardChildren && r.Discard is { } discarded) { before = discarded.SequenceBefore; after = discarded.SequenceAfter; }
        else if (r.Stage == PlacedEquipmentBenefitStage.DrawChildren && r.Draw is { } drawn) { before = drawn.SequenceBefore; after = drawn.SequenceAfter; }
        else return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == root.Id) &&
                moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.OriginOwnerSeat == root.OwnerSeat &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after);
        var removals = _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after && m.CardKind == CardKind.SilverLion &&
            m.From.Zone == CardZoneKind.Equipment && m.From.OwnerSeat is not null && m.To != m.From).ToArray();
        if (child is RecoveryReplacementFrame silver)
            return RecoveryReplacementFrameRidesOn(silver, root) && silver.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                silver.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && silver.Attempt.Amount == 1 &&
                silver.Attempt.SourceSeat == silver.Attempt.TargetSeat && removals.Any(m => m.From.OwnerSeat == silver.Attempt.TargetSeat && silver.Attempt.Completion.MoveReason == m.Reason);
        return child is HpChangedTriggerWindowFrame lion && lion.ResumeFrameId == root.Id && lion.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            lion.Change.ParentFrameId == root.Id && lion.Change.Kind == HpChangeKind.Recovery && lion.Change.Amount == 1 && lion.Change.SourceSeat == lion.Change.TargetSeat &&
            removals.Any(m => m.From.OwnerSeat == lion.Change.TargetSeat);
    }
    private ProgramSkillFrame? PlacedEquipmentObserverRoot(long? damageWindowId = null)
    {
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.PlacedEquipmentBenefit is not null))
        {
            if (!ValidPlacedEquipmentBenefit(root)) continue;
            var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
            if (damageWindowId is { } id && !_resolutionStack.Skip(index + 1).Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)) continue;
            if (index == _resolutionStack.Count - 1) return root;
            if (root.PlacedEquipmentBenefit!.Placement is null || !PlacedEquipmentFirstObserver(index, root)) continue;
            var valid = true;
            // The incoming first edge was proved against this new receipt and actual payment ledger above.
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (!PaidColorDamageClaimObserverEdge(child)) { valid = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && (IsPaidHandRepaymentRescueRide(child, dying) ||
                    IsPaidHandRepaymentProgramAlcoholRide(child, dying) || PolicyCounterspellVirtualAlcoholRide(child, dying) ||
                    PaidObserverDamageVirtualAlcoholRide(child, dying) || ExactLegacyDyingAlcoholReturnRide(child, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool IsPlacedEquipmentBenefitMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.PlaceOwnedEquipmentThenResolveSlotBenefit && pending.SubjectSeat == f.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && f.PlacedEquipmentBenefit is
            { Stage: PlacedEquipmentBenefitStage.PlacementChildren or PlacedEquipmentBenefitStage.DrawChildren or PlacedEquipmentBenefitStage.DiscardChildren } && ValidPlacedEquipmentBenefit(f);
    private bool IsPlacedEquipmentBenefitProgramDying() => ActiveDying is { } dying && PlacedEquipmentObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPlacedEquipmentBenefitDamageObserver(long damageWindowId) => PlacedEquipmentObserverRoot(damageWindowId) is not null;
    private bool AllowsPlacedEquipmentBenefitNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (_resolutionStack.LastOrDefault()?.Id != observer.Id || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost) || PlacedEquipmentObserverRoot() is null) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            !sourceLess && target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private void AssertPlacedEquipmentBenefits()
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.PlacedEquipmentBenefit is not null))
            if (!ValidPlacedEquipmentBenefit(f) || PlacedEquipmentObserverRoot()?.Id != f.Id)
                throw new InvalidOperationException("Equipment placement benefit lost its original Ending, real payment and contiguous typed child tree.");
    }
}
