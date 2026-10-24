namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheXingtianSkill = "shanhe:xingtian-axe-20766-effect";
    private bool ResolveShanheXingtianEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (card.Kind != CardKind.ShanheXingtianAxe) return false;
        var grant = EquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } old }) _players[old].SkillGrants.RemoveGrant(grant);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } next } && _contentRegistry.Skills.ContainsKey(ShanheXingtianSkill))
            _players[next].SkillGrants.Grant(new SkillGrant(grant, ShanheXingtianSkill, grant, grant));
        return true;
    }

    private bool HasShanheXingtianSeal(int seat) => _turnCardUseEffects.TargetRestrictions.Any(r =>
        r.TurnNumber == _turnNumber && r.TurnSeat == _currentSeat && r.TargetSeat == seat &&
        r.Restriction == SkillProgramCardTargetRestriction.CannotUseOrPlayForTurn);

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 59300)]
    private bool ShanheXingtianCardUseForbidden(ProgramCardUseQuery query) => HasShanheXingtianSeal(query.ActorSeat);

    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 59300)]
    private bool ShanheXingtianNativeResponseForbidden(ProgramNativeResponseQuery query) => HasShanheXingtianSeal(query.Provider.Seat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 59300, SkillProgramEffectOp.ApplyShanheXingtianSeal)]
    private bool CanOfferShanheXingtian(ProgramTriggerLegalityContext gate) => gate.SkillId != ShanheXingtianSkill ||
        GetEquipment(_players[gate.OwnerSeat]).Any(c => c.Kind == CardKind.ShanheXingtianAxe &&
            IsActiveProgramSourceEquipmentCard(gate.OwnerSeat, gate.SkillId, gate.Candidate.SkillInstanceId, c)) &&
        _cardZones.CardsAt(CardLocation.Hand(gate.OwnerSeat)).Count + GetEquipment(_players[gate.OwnerSeat]).Count >= 2;

    private bool HasShanheXingtianPaidCost(ProgramSkillFrame f)
    {
        if (f.SkillId != ShanheXingtianSkill || f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects } ||
            f.CardSetBindings.SingleOrDefault(b => b.Name == "axe-cost") is not { CardIds.Count: 2, SourceLocations.Count: 2 } cost)
            return false;
        var invoices = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ParentFrameId == f.Id &&
            e.InitiatorSeat == f.OwnerSeat && e.ExecutorSeat == f.OwnerSeat && e.SubjectSeat == f.OwnerSeat && e.ActualCount == 2 &&
            e.Reason.Value == $"skill-program.{f.SkillId}.MoveBoundCards").ToArray();
        if (invoices is not [var invoice]) return false;
        return cost.CardIds.Select((id, i) => (id, from: cost.SourceLocations[i])).All(c =>
            c.from.OwnerSeat == f.OwnerSeat && c.from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Count(e => e.InvocationId == invoice.InvocationId &&
                e.CardId == c.id && e.From == c.from && e.CompletionSequence > invoice.SequenceBefore &&
                e.CompletionSequence <= invoice.SequenceAfter) == 1);
    }

    private void ApplyShanheXingtianSeal(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (!HasShanheXingtianPaidCost(f) || f.SelectedTargetSeats is not [var target] ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == f.WindowContext!.ParentFrameId) is not { } parent ||
            parent.Action.ActorSeat != f.OwnerSeat || parent.Action.EffectiveDesignatedTargetSeats is not [var original] || original != target)
            throw new InvalidOperationException("The S5 axe lost its exact use, target or completed two-card cost.");
        if (!_players[target].IsAlive) return;
        ValidateProgramTurnEffectGrant(f);
        var index = f.InstructionIndex - 1;
        var source = CreateProgramTurnEffectSource(f);
        var ban = _turnCardUseEffects.GrantTargetRestriction(_turnNumber, _currentSeat, f.Id, -2 * index - 1,
            source, SkillProgramCardTargetRestriction.CannotUseOrPlayForTurn, target);
        AdvanceEventRulesAndQueueFact(new CardTargetRestrictionGrantedEvent(ban));
        var armor = _turnCardUseEffects.GrantTargetRestriction(_turnNumber, _currentSeat, f.Id, -2 * index - 2,
            source, SkillProgramCardTargetRestriction.ArmorIneffectiveForTurn, target);
        AdvanceEventRulesAndQueueFact(new CardTargetRestrictionGrantedEvent(armor));
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 59300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 59300)]
    private bool ContinueShanheXingtianPaidCost(ProgramSkillFrame f) => HasShanheXingtianPaidCost(f);

    [ProgramContinuation(ProgramContinuationKind.Movement, 59300)]
    private bool ReturnShanheXingtianPaidMovement(ProgramSkillFrame f)
    {
        if (!HasShanheXingtianPaidCost(f)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)
            .GetPausedInstruction(f.InstructionIndex).Effect;
        if (effect is not { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "axe-cost", AwaitMovementTriggers: true } ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } movement ||
            movement.SubjectSeat != f.OwnerSeat)
            throw new InvalidOperationException("The paid S5 axe lost its exact native discard-child return.");
        // Removing the source axe is a legal material payment. This original
        // invoice owns the continuation before the generic live-source guard.
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id);
        return true;
    }

    [ProgramCapability]
    private sealed class ShanheXingtianCapability(GameEngine engine) : IShanheXingtianHost
    { public void ApplyShanheXingtianSeal(ProgramSkillFrame f) => engine.ApplyShanheXingtianSeal(f); }
}
