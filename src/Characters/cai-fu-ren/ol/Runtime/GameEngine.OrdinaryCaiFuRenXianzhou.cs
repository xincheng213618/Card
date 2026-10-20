namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasOrdinaryCaiFuRenCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting);
    private static CardConversionSource OrdinaryCaiFuRenSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cai_fu_ren_ol_OrdinaryCaiFuRenXianzhou.OrdinaryCaiFuRenSource(f);

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 27595, SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou)]
    private bool CanActivateOrdinaryCaiFuRenXianzhou(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat && HasPayableAllEquipmentDonation(c.OwnerSeat, c.Program.Id, "ordinary-xianzhou");

    // This predicate deliberately does not call the shared validity/source predicates:
    // the shared validator may use it as its exact new-op/source-loss admission.
    private bool IsIssuedOrdinaryCaiFuRenXianzhou(ProgramSkillFrame f)
    {
        if (f.SkillId != "ol:xianzhou" || f.ActivationId != "all-equipment-donation" || f.TriggerId is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.EquipmentDonation is not { } r || r.InstructionIndex != 0 || r.Source != OrdinaryCaiFuRenSource(f) || r.GameplayHash != f.GameplayHash || r.UsageId != "ordinary-xianzhou" || r.TurnNumber != _turnNumber || r.TurnOwnerSeat != _currentSeat || f.SelectedTargetSeats is not [var recipient] || recipient != r.RecipientSeat || recipient == f.OwnerSeat || !IsValidPlayerSeat(recipient) || r.PaidCardIds.Count == 0 || r.PaidCardIds.Distinct().Count() != r.PaidCardIds.Count || r.ActualDeliveredCount < 0 || r.ActualDeliveredCount > r.PaidCardIds.Count || r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore || r.SequenceAfter > _movementSequence || _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || p.Activations is not [{ Id: "all-equipment-donation", Effects: [{ Op: SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou, StateId: "ordinary-xianzhou" }] }] || _skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, EquipmentDonationUsage(r.UsageId), SkillUsageScope.Game) != 1) return false;
        if (ProgramEventHistory<EquipmentDonationPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray() is not [var paid] || paid != new EquipmentDonationPaidEvent(f.Id, r.Source, r.GameplayHash, r.UsageId, r.TurnNumber, r.TurnOwnerSeat, recipient, r.PaidCardIds.Count, r.ActualDeliveredCount, r.SequenceBefore, r.SequenceAfter)) return false;
        var entities = ProgramEventHistory<EquipmentDonationEntityPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        return entities.Length == r.PaidCardIds.Count && entities.Select(e => e.CardId).SequenceEqual(r.PaidCardIds) && entities.Count(e => e.Delivered) == r.ActualDeliveredCount && entities.Select((e, i) => e.PaidIndex == i && e.Source == r.Source && e.RecipientSeat == recipient && e.MovementSequence > r.SequenceBefore && e.MovementSequence <= r.SequenceAfter && _cardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && EquipmentCatalog.IsEquipment(m.CardKind) && m.From == CardLocation.Equipment(f.OwnerSeat) && m.To == CardLocation.Processing && m.Reason.Value == EquipmentDonationMoveReason) == 1 && e.Delivered == (_cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.CardId == e.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(recipient) && m.Reason.Value == EquipmentDonationMoveReason) == 1)).All(v => v);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27595)]
    private bool ReturnPaidOrdinaryXianzhouMovement(ProgramSkillFrame f)
    {
        if (f.SkillId != "ol:xianzhou" || f.EquipmentDonation is not { Stage: EquipmentDonationStage.PaidMovement }) return false;
        if (!IsIssuedOrdinaryCaiFuRenXianzhou(f) || !IsValidAllEquipmentDonation(f) ||
            f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat)
            throw new InvalidOperationException("Ordinary Xianzhou movement must return its exact already-paid donation.");
        // The generic movement return cancels dead holders. This paid operation
        // instead drains the remaining native payment children and lets the
        // recipient-owned benefit cursor decide whether a new effect is legal.
        if (AwaitEquipmentDonationMovements(f.Id) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(f.Id);
        return true;
    }
    [ProgramCapability]
    private sealed class OrdinaryCaiFuRenCapability(GameEngine engine) : IOrdinaryCaiFuRenHost
    {
        public SkillProgramStepOutcome Xianzhou(ProgramSkillFrame f, string stateId) => engine.RunAllEquipmentDonation(f, stateId);
        public SkillProgramStepOutcome Qieting(ProgramSkillFrame f) => engine.BeginOrdinaryQieting(f);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveOrdinaryCaiFuRenXianzhou)]
    private PromptChoice ChooseOrdinaryXianzhouAi(PendingDecision d, ProgramSkillFrame f) => SelectAiEquipmentDonation(d, f);
}
