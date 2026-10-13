namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A legacy zero-entity producer can acquire native windows through a real
    // participant. Benxi's committed extra target belongs to that use, while
    // its program still receives one primary post-redirection target.
    private bool TryGetCurrentEnhancementVirtualPrimaryReturn(CardUseFrame use,
        ProgramSkillFrame parent, out IReadOnlyList<int> primaryTarget)
    {
        primaryTarget = [];
        if (use is not { CardId: 0, CardKind: CardKind.Slash, PhysicalCardIds.Count: 0,
                ProgramUseCommitted: true, ProgramUseAccepted: true, TargetSeats.Count: 2 } ||
            !HasCurrentEnhancementVirtualProducer(use) ||
            use.CardAttack is not { CardId: null, EffectiveCardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != parent.Id || attack.SourceSeat != use.SourceSeat ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0,
                TargetSeats.Count: 2, EffectiveSuit: Suit.None, EffectiveRank: 0 } action ||
            action.ActorSeat != use.SourceSeat || action.ProviderSeat != use.SourceSeat ||
            action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            parent.OwnerSeat != use.SourceSeat || parent.InstructionIndex < 1 ||
            string.IsNullOrWhiteSpace(parent.SkillInstanceId) || string.IsNullOrWhiteSpace(GetProgramBindingId(parent)) ||
            use.EnhancementOwnerSeat != action.ActorSeat || !use.Enhancements.HasFlag(CurrentCardEnhancement.ExtraTarget)) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == use.Id);
        if (index < 1 || !ReferenceEquals(_resolutionStack[index - 1], parent) ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash)
            return false;
        var issuer = new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId);
        var plan = ProgramInstructionResolver.Default.Resolve(parent, program);
        if (parent.InstructionIndex > plan.Instructions.Count) return false;
        var paused = plan.GetPausedInstruction(parent.InstructionIndex).Effect;
        if (paused is not { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash, UseCardActionWindows: false } ||
            paused.TargetRestriction is not (SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget or SkillProgramCardTargetRestriction.NormalSlashTarget) ||
            !action.ConversionChain.SequenceEqual([issuer])) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var declarations = history.OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == use.Id).ToArray();
        var targets = history.OfType<TargetsConfirmedEvent>().Where(e => e.ResolutionId == use.Id).ToArray();
        var additions = history.OfType<CurrentCardEnhancedEvent>().Where(e => e.CardUseFrameId == use.Id &&
            e.Enhancement == CurrentCardEnhancement.ExtraTarget).ToArray();
        var accepted = history.OfType<CardActionAcceptedEvent>().Where(e => e.Action.ActionId == action.ActionId).ToArray();
        if (declarations is not [{ CardId: 0, CardKind: CardKind.Slash } declared] || declared.SourceSeat != use.SourceSeat ||
            targets is not [{ TargetSeats.Count: 1 } original] || parent.SelectedTargetSeats is not [var selected] ||
            selected != original.TargetSeats[0] ||
            additions is not [{ ExtraTargetSeat: { } extra } addition] || addition.CardActionId != action.ActionId ||
            addition.OwnerSeat != action.ActorSeat || extra == selected || extra == action.ActorSeat || !IsValidPlayerSeat(extra) ||
            accepted is not [var acceptedFact]) return false;
        var finalized = acceptedFact.Action;
        if (finalized.Type != CardActionType.Use || finalized.ActorSeat != action.ActorSeat || finalized.ProviderSeat != action.ProviderSeat ||
            finalized.ParentActionId != action.ParentActionId || finalized.RequesterSeat is not null || finalized.ResponderSeat is not null ||
            finalized.OpponentSeat is not null || finalized.EffectiveKind != CardKind.Slash || finalized.EffectiveSuit != Suit.None ||
            finalized.EffectiveRank != 0 || finalized.PhysicalCards.Count != 0 || !finalized.ConversionChain.SequenceEqual([issuer]) ||
            finalized.TargetSeats.Count != 2 || !action.TargetSeats.SequenceEqual(finalized.TargetSeats) ||
            Array.IndexOf(history, declared) >= Array.IndexOf(history, original) ||
            Array.IndexOf(history, original) >= Array.IndexOf(history, addition) ||
            Array.IndexOf(history, addition) >= Array.IndexOf(history, acceptedFact)) return false;
        if (parent.TriggerId is { } binding)
        {
            if (parent.WindowContext is not { } context ||
                history.OfType<ProgramBindingStartedEvent>().Where(e => e.FrameId == parent.Id).ToArray() is not [var started] ||
                started.SkillId != parent.SkillId || started.BindingId != binding || started.SkillInstanceId != parent.SkillInstanceId ||
                started.OwnerSeat != parent.OwnerSeat || started.Window != context.Window) return false;
        }
        else if (history.OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == parent.Id && e.SkillId == parent.SkillId &&
            e.ActivationId == parent.ActivationId && e.OwnerSeat == parent.OwnerSeat) != 1) return false;
        var additionIndex = Array.IndexOf(history, addition);
        if (history.Take(additionIndex + 1).OfType<CurrentEnhancementSlashRedirectedEvent>().Any(e => e.CardUseFrameId == use.Id))
            return false;
        IReadOnlyList<int> actualTargets = Array.AsReadOnly(new[] { selected, extra });
        var acceptedSeen = false;
        foreach (var fact in history.Skip(additionIndex + 1))
        {
            if (fact is CurrentEnhancementSlashRedirectedEvent redirected && redirected.CardUseFrameId == use.Id)
            {
                if (redirected.TargetIndex != (acceptedSeen ? 1 : 0) ||
                    !IsCurrentEnhancementSlashRedirectFact(use, redirected, actualTargets, history)) return false;
                actualTargets = redirected.AfterTargets;
            }
            else if (ReferenceEquals(fact, acceptedFact))
            {
                if (!actualTargets.SequenceEqual(finalized.TargetSeats)) return false;
                acceptedSeen = true;
            }
        }
        if (!acceptedSeen || !use.TargetSeats.SequenceEqual(actualTargets)) return false;
        // Duplicate seats are legal only when the exact native redirect facts
        // reconstruct them. The producer receives the primary actual cursor.
        primaryTarget = Array.AsReadOnly(new[] { actualTargets[0] });
        return true;
    }
}
