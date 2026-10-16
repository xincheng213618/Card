namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 26622)]
    private void FreezeWeifuActorChange(IGameEvent fact)
    {
        if (fact is not ProgramCardUseActorReplacedEvent change || LifecycleCardUse(change.CardUseFrameId) is not { WeifuUse: { } r, Action: { } action } use) return;
        if (action.ActionId != r.ActionId || change.ActorSeat != use.SourceSeat || change.ActorSeat != action.ActorSeat || change.ProviderSeat != r.ProviderSeat ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == change.FrameId) is not { } producer || producer.SkillId != change.SkillId ||
            producer.OwnerSeat != change.OwnerSeat || change.OwnerSeat != change.PreviousActorSeat || producer.InstructionIndex < 1 ||
            GetPausedPrivateOfferEffect(producer)?.Op != SkillProgramEffectOp.ReplaceCurrentCardUseActor ||
            producer.SelectedTargetSeats is not [var target] || target != action.ActorSeat ||
            producer.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } context } window ||
            context.ParentCardUseFrameId != use.Id || context.CardActionId != action.ActionId ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == window.ParentFrameId) is not { } native ||
            native.ParentFrameId != use.Id || native.Action.ActionId != action.ActionId || native.CandidateIndex < 0 || native.CandidateIndex >= native.Candidates.Count ||
            !MountObserverCandidateMatches(producer, ToSharedCandidate(native.Candidates[native.CandidateIndex])) ||
            ProgramEventHistory<WeifuActorChangedEvent>().Any(e => e.UseId == use.Id && e.ProgramFrameId == producer.Id && e.InstructionIndex == producer.InstructionIndex))
            throw new InvalidOperationException("Weifu cannot acknowledge an unrelated native actor replacement.");
        AdvanceEventRulesAndQueueFact(new WeifuActorChangedEvent(use.Id, action.ActionId, producer.Id, QianZhaoSource(producer), producer.GameplayHash,
            producer.InstructionIndex, change.PreviousActorSeat, change.ActorSeat, change.ProviderSeat));
    }
    private bool WeifuCurrentActorMatches(CardUseFrame use, WeifuUseReceipt r)
    {
        if (use.Action is not { Type: CardActionType.Use } action || action.ActionId != r.ActionId || action.ProviderSeat != r.ProviderSeat || action.ActorSeat != use.SourceSeat) return false;
        var changes = ProgramEventHistory<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == use.Id).ToArray();
        var proofs = ProgramEventHistory<WeifuActorChangedEvent>().Where(e => e.UseId == use.Id).ToArray();
        if (changes.Length != proofs.Length) return false;
        if (changes.Length == 0) return action.ActorSeat == r.ActorSeat;
        if (ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.SourceSeat == r.ActorSeat && e.CardId == use.CardId) != 1 ||
            !ProgramEventHistory<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId && e.Action.Type == CardActionType.Use &&
                e.Action.ActorSeat == r.ActorSeat && e.Action.ProviderSeat == r.ProviderSeat && e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards))) return false;
        var actor = r.ActorSeat;
        for (var i = 0; i < changes.Length; i++)
        {
            var change = changes[i]; var proof = proofs[i]; var source = proof.Producer;
            if (change.FrameId != proof.ProgramFrameId || proof.ActionId != action.ActionId || proof.PreviousActor != actor || change.PreviousActorSeat != actor ||
                change.OwnerSeat != actor || source.OwnerSeat != actor || change.SkillId != source.SkillId || change.ActorSeat != proof.Actor ||
                change.ProviderSeat != proof.Provider || proof.Provider != r.ProviderSeat || !IsValidPlayerSeat(proof.Actor) || proof.Actor == actor ||
                _contentRegistry.GetSkill(source.SkillId).Program is not { } p || p.GameplayHash != proof.GameplayHash ||
                ProgramInstructionResolver.Default.Find(p, ProgramInstructionSourceKind.Trigger, source.BindingId) is not { } plan ||
                plan.Trigger?.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || proof.InstructionIndex < 1 || proof.InstructionIndex > plan.Instructions.Count ||
                plan.GetPausedInstruction(proof.InstructionIndex).Effect.Op != SkillProgramEffectOp.ReplaceCurrentCardUseActor ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == proof.ProgramFrameId && e.SkillId == source.SkillId && e.OwnerSeat == actor &&
                    e.SkillInstanceId == source.SkillInstanceId && e.BindingId == source.BindingId && e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) != 1) return false;
            actor = proof.Actor;
        }
        return actor == action.ActorSeat;
    }
}
