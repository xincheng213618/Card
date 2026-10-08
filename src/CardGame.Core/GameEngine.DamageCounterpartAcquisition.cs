namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanOfferDamageCounterpartAcquisition(int owner, SkillProgramTrigger trigger, IDamageAttempt attack) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordDamageCounterpartAcquisition) ||
        !attack.IsSourceLess && attack.DamageWasApplied && attack.DamageAmount > 0 &&
        IsValidPlayerSeat(attack.SourceSeat) && IsValidPlayerSeat(attack.TargetSeat) && attack.SourceSeat != attack.TargetSeat &&
        (owner == attack.SourceSeat || owner == attack.TargetSeat);

    private bool CanOfferDamageCounterpartAcquisition(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RecordDamageCounterpartAcquisition)) return true;
        return context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source,
                   TargetSeat: { } target, Amount: > 0, DamageFrameId: { } damageId } && source != target &&
            (candidate.OwnerSeat == source || candidate.OwnerSeat == target) &&
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is DamageTriggerWindowFrame window &&
            window.ParentFrameId == damageId && window.TriggerWindow == context.Window &&
            CanOfferDamageCounterpartAcquisition(candidate.OwnerSeat, trigger, GetDamageTriggerAttack(window)) &&
            !CompleteProgramEventHistory().OfType<DamageCounterpartAcquisitionGrantedEvent>().Any(e =>
                e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId &&
                e.StateId == trigger.Effects[0].StateId && e.TargetSkillId == trigger.Effects[0].SkillIds.Single() && e.DamageFrameId == damageId);
    }

    private SkillProgramStepOutcome RecordDamageCounterpartAcquisition(ProgramSkillFrame supplied, string stateId, string skillId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source,
                TargetSeat: { } target, DamageFrameId: { } damageId, Amount: > 0 } context ||
            _resolutionStack.Count < 3 || _resolutionStack[^2] is not DamageTriggerWindowFrame window ||
            window.Id != context.ParentFrameId || window.ParentFrameId != damageId || window.TriggerWindow != context.Window ||
            _resolutionStack[^3] is not DamageFrame damage || damage.Id != damageId ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } program || program.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Counterpart acquisition lost its exact positive applied-damage candidate.");
        var trigger = ProgramInstructionResolver.Default.Resolve(f, program).Trigger!;
        DamageCounterpartAcquisitionContract.ValidateTrigger(f.SkillId, trigger);
        if (trigger.Effects[0].StateId != stateId || trigger.Effects[0].SkillIds.Single() != skillId || context.OccurrenceIndex != 0)
            throw new InvalidOperationException("Counterpart acquisition changed its declared state or target skill.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0);
        if (!CanOfferDamageCounterpartAcquisition(candidate, trigger, context)) return SkillProgramStepOutcome.Continue;
        var attack = GetDamageTriggerAttack(window);
        if (window.SourceSeat != source || window.TargetSeat != target || damage.SourceSeat != source || damage.TargetSeat != target ||
            damage.Amount != context.Amount || damage.Nature != GetDamageNature(attack) || damage.ParentFrameId != attack.ResolutionId ||
            attack.SourceSeat != source || attack.TargetSeat != target || attack.DamageAmount != context.Amount ||
            !CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Any(e => e.ResolutionId == damageId &&
                e.SourceSeat == source && e.TargetSeat == target && e.Amount == context.Amount && e.Nature == damage.Nature && !e.SourceLess))
            throw new InvalidOperationException("Counterpart acquisition cannot be issued from HP loss, sourceless or unrelated damage.");
        AdvanceEventRulesAndQueueFact(new DamageCounterpartAcquisitionGrantedEvent(f.Id,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, stateId, skillId,
            source == f.OwnerSeat ? target : source, damageId, attack.ResolutionId, source, target,
            context.Amount, damage.Nature, _turnNumber, _turnProgression.OwnerSeat));
        return SkillProgramStepOutcome.Continue;
    }

    private bool HasDamageCounterpartAcquisition(int owner, string skillId, string stateId, int counterpart)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var cutoff = Array.FindLastIndex(history, e => e is TurnStartedEvent t && t.ActorSeat == owner);
        return history.Skip(cutoff + 1).OfType<DamageCounterpartAcquisitionGrantedEvent>().Any(e =>
            e.Source.OwnerSeat == owner && e.TargetSkillId == skillId && e.StateId == stateId && e.CounterpartSeat == counterpart &&
            e.Amount > 0 && e.DamageSourceSeat != e.DamageTargetSeat &&
            (e.DamageSourceSeat == owner && e.DamageTargetSeat == counterpart || e.DamageTargetSeat == owner && e.DamageSourceSeat == counterpart) &&
            _contentRegistry.GetSkill(e.Source.SkillId).Program is { } program && program.GameplayHash == e.GameplayHash &&
            ProgramInstructionResolver.Default.FindTrigger(program, e.Source.BindingId) is { } trigger &&
            trigger.Effects is [{ Op: SkillProgramEffectOp.RecordDamageCounterpartAcquisition, SkillIds: [var targetSkill] } effect] &&
            targetSkill == skillId && effect.StateId == stateId);
    }

    private sealed partial class ProgramSkillHost : IDamageCounterpartAcquisitionHost
    {
        public SkillProgramStepOutcome RecordDamageCounterpartAcquisition(ProgramSkillFrame f, string stateId, string skillId) =>
            engine.RecordDamageCounterpartAcquisition(f, stateId, skillId);
    }
}
