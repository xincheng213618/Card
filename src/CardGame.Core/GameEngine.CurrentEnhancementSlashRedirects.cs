namespace CardGame.Core;

public sealed record CurrentEnhancementSlashRedirectedEvent(long ProgramFrameId, long CardUseFrameId,
    long ActionId, CardConversionSource Source, string GameplayHash, int InstructionIndex, int TargetIndex,
    int PreviousTargetSeat, int NewTargetSeat, IReadOnlyList<int> BeforeTargets,
    IReadOnlyList<int> AfterTargets, long WindowFrameId) : IGameEvent;

public sealed partial class GameEngine
{
    private bool HasCurrentEnhancementVirtualProducer(CardUseFrame use)
    {
        if (use is not { CardId: 0, CardKind: CardKind.Slash, PhysicalCardIds.Count: 0,
                ProgramUseCommitted: true, TargetSeats.Count: 2, CurrentSlashFirePolicy: null } ||
            !use.Enhancements.HasFlag(CurrentCardEnhancement.ExtraTarget) ||
            use.CardAttack is not { CardId: null, PhysicalCardIds.Count: 0, ProgramSkillCardUseFrameId: { } parentId } attack ||
            attack.SourceSeat != use.SourceSeat || use.Action is not
                { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0,
                    TargetSeats.Count: 2, EffectiveSuit: Suit.None, EffectiveRank: 0 } action ||
            action.ActorSeat != use.SourceSeat || action.ProviderSeat != use.SourceSeat ||
            action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            use.EnhancementOwnerSeat != use.SourceSeat) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || parent.Id != parentId ||
            parent.OwnerSeat != use.SourceSeat || parent.InstructionIndex < 1 ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash)
            return false;
        var plan = ProgramInstructionResolver.Default.Resolve(parent, program);
        if (parent.InstructionIndex > plan.Instructions.Count) return false;
        var paused = plan.GetPausedInstruction(parent.InstructionIndex).Effect;
        return paused is { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash } &&
            (!paused.UseCardActionWindows || IsHappyLuBuTrueVirtualProducer(use, parent, paused)) &&
            paused.TargetRestriction is (SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget or SkillProgramCardTargetRestriction.NormalSlashTarget) &&
            action.ConversionChain.SequenceEqual([new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId)]);
    }

    private void RecordCurrentEnhancementSlashRedirect(ProgramSkillFrame redirector, long useId, int previousSeat, int newSeat)
    {
        if (LifecycleCardUse(useId) is not { } use || !HasCurrentEnhancementVirtualProducer(use)) return;
        var action = use.Action!;
        var program = _contentRegistry.GetSkill(redirector.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(redirector, program);
        if (redirector.InstructionIndex < 1 || redirector.InstructionIndex > plan.Instructions.Count)
            throw new InvalidOperationException("An enhanced virtual Slash redirect lost its paused instruction.");
        var effect = plan.GetPausedInstruction(redirector.InstructionIndex).Effect;
        if (redirector.GameplayHash != program.GameplayHash || effect.Op != SkillProgramEffectOp.RedirectCurrentAttack ||
            newSeat != ResolveProgramEffectTarget(redirector, effect.Target) ||
            redirector.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } windowContext ||
            context.ParentCardUseFrameId != useId || context.CardActionId != action.ActionId || context.ActorSeat != use.SourceSeat ||
            windowContext.SourceSeat != use.SourceSeat || windowContext.TargetSeat != previousSeat || redirector.OwnerSeat != previousSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == windowContext.ParentFrameId) is not { } window ||
            window.ParentFrameId != useId || window.Action.ActionId != action.ActionId || window.Continuation != ProgramCardContinuation.SlashTargetRedirecting ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(redirector, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            use.TargetIndex is not (0 or 1) || use.TargetSeats[use.TargetIndex] != newSeat || use.CardAttack!.TargetSeat != newSeat ||
            CompleteProgramEventHistory().OfType<CurrentEnhancementSlashRedirectedEvent>().Any(e => e.ProgramFrameId == redirector.Id &&
                e.CardUseFrameId == useId && e.InstructionIndex == redirector.InstructionIndex))
            throw new InvalidOperationException("An enhanced virtual Slash redirect lost its exact native source and target cursor.");
        var after = use.TargetSeats.ToArray(); var before = after.ToArray(); before[use.TargetIndex] = previousSeat;
        AdvanceEventRulesAndQueueFact(new CurrentEnhancementSlashRedirectedEvent(redirector.Id, useId, action.ActionId,
            new(redirector.SkillId, GetProgramBindingId(redirector), redirector.OwnerSeat, redirector.SkillInstanceId),
            redirector.GameplayHash, redirector.InstructionIndex, use.TargetIndex, previousSeat, newSeat,
            Array.AsReadOnly(before), Array.AsReadOnly(after), window.Id));
    }

    private bool IsCurrentEnhancementSlashRedirectFact(CardUseFrame use, CurrentEnhancementSlashRedirectedEvent fact,
        IReadOnlyList<int> before, IReadOnlyList<IGameEvent> history)
    {
        if (fact.CardUseFrameId != use.Id || fact.ActionId != use.Action?.ActionId || fact.WindowFrameId <= 0 ||
            fact.Source.OwnerSeat != fact.PreviousTargetSeat || fact.TargetIndex is not (0 or 1) || before.Count != 2 ||
            before[fact.TargetIndex] != fact.PreviousTargetSeat || !fact.BeforeTargets.SequenceEqual(before) ||
            !IsValidPlayerSeat(fact.NewTargetSeat) || fact.NewTargetSeat == use.SourceSeat || fact.NewTargetSeat == fact.PreviousTargetSeat ||
            !fact.AfterTargets.SequenceEqual(before.Select((seat, index) => index == fact.TargetIndex ? fact.NewTargetSeat : seat)) ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != fact.GameplayHash ||
            ProgramInstructionResolver.Default.Find(program, ProgramInstructionSourceKind.Trigger, fact.Source.BindingId) is not { } plan ||
            plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || fact.InstructionIndex < 1 ||
            fact.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(fact.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack)
            return false;
        return history.OfType<CurrentEnhancementSlashRedirectedEvent>().Where(e => e.ProgramFrameId == fact.ProgramFrameId &&
                e.CardUseFrameId == use.Id && e.InstructionIndex == fact.InstructionIndex).ToArray() is [var issued] &&
            issued.WindowFrameId == fact.WindowFrameId && issued.ActionId == fact.ActionId && issued.Source == fact.Source &&
            issued.GameplayHash == fact.GameplayHash && issued.TargetIndex == fact.TargetIndex &&
            issued.PreviousTargetSeat == fact.PreviousTargetSeat && issued.NewTargetSeat == fact.NewTargetSeat &&
            issued.BeforeTargets.SequenceEqual(fact.BeforeTargets) && issued.AfterTargets.SequenceEqual(fact.AfterTargets) &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.OwnerSeat == fact.Source.OwnerSeat &&
                e.SkillId == fact.Source.SkillId && e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting) == 1;
    }
}
