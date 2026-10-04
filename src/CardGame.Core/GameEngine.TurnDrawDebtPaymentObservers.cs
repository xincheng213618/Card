namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? TurnDrawDebtPaymentObserverRoot()
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index - 1] is not TurnEndingBoundaryFrame ending || ending.OwnerSeat != _currentSeat || ending.TurnNumber != _turnNumber ||
                _resolutionStack[index] is not ProgramSkillFrame root || root.TurnDrawDebtPayment is not { InstructionIndex: 0 } paid ||
                root.OwnerSeat != ending.OwnerSeat || root.InstructionIndex is < 2 or > 3 ||
                root.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context || context.ParentFrameId != ending.Id ||
                ending.ItemIndex < 0 || ending.ItemIndex >= ending.Items.Count || ending.Items[ending.ItemIndex].Candidate is not { } candidate ||
                !MountObserverCandidateMatches(root, candidate) ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat)
                continue;
            var program = _contentRegistry.GetSkill(root.SkillId).Program;
            if (program is null || program.GameplayHash != root.GameplayHash) continue;
            var plan = ProgramInstructionResolver.Default.Resolve(root, program);
            if (plan.Instructions.Count != 3 || plan.Instructions[0].Op != SkillProgramEffectOp.SelectTurnDamageUseDebtPayment ||
                plan.Instructions[0].StateId != paid.StateId || plan.Instructions[0].ResultBind != paid.ResultBind ||
                plan.Instructions[1] is not { Op: SkillProgramEffectOp.MoveBoundCards, Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true } ||
                plan.Instructions[1].SourceBind != paid.ResultBind || plan.Instructions[2].Op != SkillProgramEffectOp.AwaitBoundCardMovements) continue;
            AssertTurnDrawDebtReceipts(root, plan.Instructions);
            var binding = root.CardSetBindings.SingleOrDefault(b => b.Name == paid.ResultBind);
            if (binding is null || binding.CardIds.Count != paid.RequiredPaymentCount || binding.CardIds.Count == 0 ||
                binding.SourceLocations.Count != binding.CardIds.Count ||
                binding.SourceLocations.Any(l => l.OwnerSeat != root.OwnerSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) continue;
            var reason = $"skill-program.{root.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
            if (binding.CardIds.Where((id, n) => _cardMovements.Count(m => m.Sequence > paid.MovementSequenceBefore &&
                m.CardId == id && m.From == binding.SourceLocations[n] && m.To == CardLocation.DiscardPile && m.Reason.Value == reason) != 1).Any()) continue;
            if (_resolutionStack[index + 1] is HpChangedTriggerWindowFrame hp)
            {
                if (hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.ParentFrameId != root.Id) continue;
            }
            else if (_resolutionStack[index + 1] is RecoveryReplacementFrame recovery)
            {
                if (!RecoveryReplacementFrameRidesOn(recovery, root) ||
                    recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                    recovery.Attempt.SourceSeat != root.OwnerSeat || recovery.Attempt.TargetSeat != root.OwnerSeat ||
                    recovery.Attempt.Amount != 1 || recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion ||
                    recovery.Attempt.Completion.MoveReason?.Value != reason ||
                    !binding.CardIds.Where((id, n) => binding.SourceLocations[n] == CardLocation.Equipment(root.OwnerSeat))
                        .Any(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Any(c => c.Id == id && c.Kind == CardKind.SilverLion))) continue;
            }
            else if (_resolutionStack[index + 1] is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                    moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId ||
                    moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.Movements.Count == 0 ||
                    moved.Batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= paid.MovementSequenceBefore ||
                        !binding.CardIds.Contains(m.CardId) || m.To != CardLocation.DiscardPile || m.Reason.Value != reason)) continue;
            }
            else continue;
            var aligned = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
            {
                if (!PreventionDrawObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && IsPaidHandRepaymentProgramAlcoholRide(child, dying)) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool IsTurnDrawDebtPaymentProgramDying() => ActiveDying is { ResumesProgramSkill: true } dying &&
        TurnDrawDebtPaymentObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
}
