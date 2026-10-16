namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26100)]
    private void AssertWoLongFengChuFrame(ProgramSkillFrame frame)
    {
        if (frame.EquipmentSlotNamedUse is { } d)
        {
            if (frame.Luanfeng is not null || frame.TriggerId is not null || frame.ActivationId != "equipment-slot-payment" ||
                frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
                d.Source.SkillId != frame.SkillId || d.Source.OwnerSeat != frame.OwnerSeat || d.Source.SkillInstanceId != frame.SkillInstanceId ||
                d.GameplayHash != frame.GameplayHash || ViewAsRule(d.Source) is not { InputCount: 0, UseOnly: true, TieredRoundConversion: not null } rule ||
                rule.OutputKind != d.EffectiveKind || !Enum.IsDefined(d.Intent) || !Enum.IsDefined(d.Stage) ||
                d.RoundNumber != _roundNumber || d.ActualTurn != _turnNumber || d.ActualTurnOwner != _turnProgression.OwnerSeat ||
                d.PhaseInstance != _cardUseDebitPhaseInstanceId || d.OriginalDecision.PlayerSeat != frame.OwnerSeat ||
                d.OriginalParentId is { } parent && !_resolutionStack.TakeWhile(f => f.Id != frame.Id).Any(f => f.Id == parent) ||
                d.TargetSignature != EquipmentSlotNamedTargetSignature(d.PlayAction, d.ResponseChoice) ||
                d.OriginalDecisionSignature != EquipmentSlotNamedDecisionSignature(d.OriginalDecision) ||
                (d.Intent == EquipmentSlotNamedUseIntent.Play ? d.PlayAction is not { CardId: 0 } action || action.ConversionSource != d.Source || action.PlayedCardKind != d.EffectiveKind || d.ResponseChoice is not null :
                    d.ResponseChoice is not { Cards.Count: 0 } response || d.PlayAction is not null || RequireConversionSource(response) != d.Source) ||
                CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseStartedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is not [var start] ||
                start.Source != d.Source || start.GameplayHash != d.GameplayHash || start.Intent != d.Intent || start.EffectiveKind != d.EffectiveKind || start.Yin != d.Yin ||
                start.RoundNumber != d.RoundNumber || start.OriginalPromptId != d.OriginalDecision.PromptId || start.OriginalRevision != d.OriginalDecision.Revision ||
                start.OriginalParentId != d.OriginalParentId || start.TargetSignature != d.TargetSignature || start.OriginalDecisionSignature != d.OriginalDecisionSignature ||
                start.ActualTurn != d.ActualTurn || start.ActualTurnOwner != d.ActualTurnOwner || start.PhaseInstance != d.PhaseInstance)
                throw new InvalidOperationException("Youlong lost its exact frozen native producer, accepted target invoice, actual round/turn/phase or source hash.");
            var paid = CompleteProgramEventHistory().OfType<EquipmentSlotNamedUsePaidEvent>().Where(e => e.Payment.FrameId == frame.Id).ToArray();
            if (d.Stage == EquipmentSlotNamedUseStage.ChoosingSlot)
            {
                if (d.Payment is not null || paid.Length != 0 || _resolutionStack.LastOrDefault()?.Id == frame.Id &&
                    (_pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != frame.OwnerSeat || prompt.SkillPrompt?.SkillId != frame.SkillId ||
                     prompt.Choices.Count == 0 || prompt.Choices.Any(c => c.Cards.Count != 0 || c.Targets.Count != 0 ||
                        c.Parameters.GetValueOrDefault("program-action") != "equipment-slot-named-use" ||
                        c.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                    throw new InvalidOperationException("Youlong's unpaid private slot selection cannot already carry a payment or another owner's prompt.");
            }
            else if (d.Payment is not { } p || paid is not [var fact] || fact.Payment != p || p.FrameId != frame.Id || p.Source != d.Source ||
                p.GameplayHash != d.GameplayHash || p.RoundNumber != d.RoundNumber || p.ActualTurn != d.ActualTurn || p.ActualTurnOwner != d.ActualTurnOwner ||
                p.PhaseInstance != d.PhaseInstance || p.Yin != d.Yin || p.EffectiveKind != d.EffectiveKind || p.Intent != d.Intent ||
                p.OriginalPromptId != d.OriginalDecision.PromptId || p.OriginalRevision != d.OriginalDecision.Revision || p.OriginalParentId != d.OriginalParentId ||
                p.TargetSignature != d.TargetSignature || !Enum.IsDefined(p.Slot) || p.PreviousCapacity <= 0 ||
                p.Before < 0 || p.After < p.Before || p.After > _movementSequence ||
                _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId, TieredRoundUsage(ViewAsRule(d.Source)!.TieredRoundConversion!), SkillUsageScope.Round) != 1 ||
                CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseConsumedEvent>().Any(e => e.PaymentFrameId == frame.Id) ||
                CompleteProgramEventHistory().OfType<EquipmentSlotNamedUseIssuedEvent>().Any(e => e.PaymentFrameId == frame.Id))
                throw new InvalidOperationException("Youlong's owning cost frame must preserve its single paid slot and retire before native consumption or issue.");
        }
        if (frame.Luanfeng is not { } r) return;
        if (r.FrameId != frame.Id || r.Source != new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) ||
            r.GameplayHash != frame.GameplayHash || frame.TriggerId is null || frame.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) ||
            !IsValidPlayerSeat(r.VictimSeat) || r.RecoveryAmount <= 0 || r.RestoredSlots is < 0 or > 5 || r.RequestedDraw is < 0 or > 6 ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: { } victim } context || victim != r.VictimSeat ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == r.DyingFrameId) is not { } dying || dying.VictimSeat != r.VictimSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId) is not { Window: SkillProgramTriggerWindow.DyingEntering } entry ||
            entry.ResumeDyingFrameId != dying.Id || entry.CandidateIndex >= entry.Candidates.Count || !MountObserverCandidateMatches(frame, entry.Candidates[entry.CandidateIndex]) ||
            CompleteProgramEventHistory().OfType<LuanfengIssuedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is not [var issued] ||
            issued.Source != r.Source || issued.DyingFrameId != r.DyingFrameId || issued.VictimSeat != r.VictimSeat)
            throw new InvalidOperationException("Luanfeng lost its exact original dying victim, accepted limited source, native recovery or owning lifecycle candidate.");
        var restores = CompleteProgramEventHistory().OfType<LuanfengSlotsRestoredEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        if (r.Stage == LuanfengStage.RecoveryChild ? restores.Length != 0 || r.RestoredSlots != 0 || r.RequestedDraw != 0 :
            restores is not [var restored] || restored.VictimSeat != r.VictimSeat || restored.RestoredSlots != r.RestoredSlots || restored.RequestedDraw != r.RequestedDraw)
            throw new InvalidOperationException("Luanfeng restores and counts native equipment categories only once after its real recovery children.");
    }
}
