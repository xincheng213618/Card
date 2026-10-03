namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? BoundRankBonusMovementRoot(long damageWindowId)
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame program ||
                program.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
                context.ParentFrameId != damageWindowId ||
                _resolutionStack[index - 1] is not DamageTriggerWindowFrame damage || damage.Id != damageWindowId ||
                damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
                !MountObserverCandidateMatches(program, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) ||
                program.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending ||
                pending.SubjectSeat != program.OwnerSeat ||
                _resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame first ||
                first.Batch.ParentFrameId != program.Id || first.Batch.AwaitingProgramFrameId is not null ||
                first.ResumeProgramFrameId is not null || first.Batch.OriginOwnerSeat != program.OwnerSeat ||
                first.Batch.OriginSkillId != program.SkillId || first.Batch.OriginSkillInstanceId != program.SkillInstanceId)
                continue;
            var instructions = ProgramInstructionResolver.Default.Resolve(program,
                _contentRegistry.GetSkill(program.SkillId).Program!).Instructions;
            if (program.InstructionIndex < 1 || program.InstructionIndex > instructions.Count) continue;
            var effect = instructions[program.InstructionIndex - 1];
            var gain = effect.Op == SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus;
            var remainder = effect is { Op: SkillProgramEffectOp.MoveBoundCards, AwaitMovementTriggers: true,
                Destination: SkillProgramCardDestination.DiscardPile, ExceptBind: not null } &&
                instructions.Take(program.InstructionIndex - 1).Any(e =>
                    e.Op == SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus && e.SourceBind == effect.ExceptBind) &&
                instructions.Take(program.InstructionIndex - 1).Any(e =>
                    e.Op == SkillProgramEffectOp.RevealTopCardsWithNextBooleanBonus && e.ResultBind == effect.SourceBind);
            if (!gain && !remainder) continue;
            var binding = program.CardSetBindings.SingleOrDefault(b => b.Name == effect.SourceBind);
            if (binding is null || binding.SourceLocations.Any(location => location != CardLocation.Processing)) continue;
            var expected = binding.CardIds.ToHashSet();
            if (remainder)
            {
                var excluded = program.CardSetBindings.SingleOrDefault(b => b.Name == effect.ExceptBind);
                if (excluded is null) continue;
                expected.ExceptWith(excluded.CardIds);
            }
            var reason = gain ? $"skill-program.{program.SkillId}.rank-bonus-obtain"
                : $"skill-program.{program.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
            var destination = gain ? CardLocation.Hand(program.OwnerSeat) : CardLocation.DiscardPile;
            var movements = first.Batch.Movements;
            if (movements.Count == 0 || movements.Count != expected.Count ||
                !expected.SetEquals(movements.Select(m => m.CardId)) ||
                movements.Any(m => m.From != CardLocation.Processing || m.To != destination ||
                    m.Reason.Value != reason || !_cardMovements.Contains(m))) continue;
            var aligned = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
                if (!PaidMountObserverEdge(child)) { aligned = false; break; }
            if (aligned) return program;
        }
        return null;
    }

    private bool HasBoundRankBonusMovementObserver(long damageWindowId) =>
        BoundRankBonusMovementRoot(damageWindowId) is not null;

    private bool IsBoundRankBonusMovementDying() => ActiveDamageTrigger is { } damage &&
        ActiveDying is { ResumesProgramSkill: true } dying && BoundRankBonusMovementRoot(damage.Id) is not null &&
        _resolutionStack.OfType<DyingFrame>().Any(f => f.Id == dying.FrameId && f.ParentFrameId == dying.ParentFrameId);
}
