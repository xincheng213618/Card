namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramCardTriggerWindowFrame DeferFirstSlashDuelCandidates(ProgramCardTriggerWindowFrame frame)
    {
        if (GetCardActionWindow(frame) != SkillProgramTriggerWindow.CardUseTargetsFinalized || frame.CandidateIndex >= frame.Candidates.Count) return frame;
        bool IsBenefit(ProgramCardTriggerCandidate c) => GetProgramTrigger(ToSharedCandidate(c)).Effects
            .Any(e => e.Op == SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit);
        if (!IsBenefit(frame.Candidates[frame.CandidateIndex]) || !frame.Candidates.Skip(frame.CandidateIndex + 1).Any(c => !IsBenefit(c))) return frame;
        // Only this newly opted-in candidate moves. Preserve every historical
        // candidate's relative order, identity and already-completed prefix.
        var next = frame.Candidates.Take(frame.CandidateIndex)
            .Concat(frame.Candidates.Skip(frame.CandidateIndex).Where(c => !IsBenefit(c)))
            .Concat(frame.Candidates.Skip(frame.CandidateIndex).Where(IsBenefit)).ToArray();
        ReplaceRuntimeTop(frame = frame with { Candidates = Array.AsReadOnly(next) }); return frame;
    }
    private long? FirstSlashDuelLegacyProducer(CardUseFrame use)
    {
        if (use.Action is not null || use.CardId != 0 || use.CardKind != CardKind.Slash ||
            use.PhysicalCardIds is not { Count: 0 } || use.TargetSeats.Count == 0 ||
            use.TargetSeats.Distinct().Count() != use.TargetSeats.Count ||
            use.CardAttack is not { IsSourceLess: false, EffectiveCardKind: CardKind.Slash, CardId: null,
                PhysicalCardIds.Count: 0, ProgramSkillCardUseFrameId: { } producer } attack ||
            attack.SourceSeat != use.SourceSeat) return null;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramSkillFrame parent || parent.Id != producer ||
            parent.OwnerSeat != use.SourceSeat || parent.TriggerId is null ||
            parent.SelectedTargetSeats is not [var original] || !IsValidPlayerSeat(original) ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash ||
            parent.WindowContext is not { } context) return null;
        var paused = ProgramInstructionResolver.Default.Resolve(parent, program).GetPausedInstruction(parent.InstructionIndex).Effect;
        if (paused is not { Op: SkillProgramEffectOp.UseVirtualCard, OutputKind: CardKind.Slash, UseCardActionWindows: false } ||
            paused.TargetRestriction is not (SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget or SkillProgramCardTargetRestriction.NormalSlashTarget))
            return null;
        // This capability validates its own genuine legacy producer. The mature
        // same-type aid predicate intentionally supports only its older unlimited
        // distance producer and must not be broadened for a new consumer.
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == 0 &&
                e.CardKind == CardKind.Slash && e.SourceSeat == use.SourceSeat) == 1 &&
            history.OfType<TargetsConfirmedEvent>().Count(e => e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual([original])) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == parent.Id && e.OwnerSeat == parent.OwnerSeat &&
                e.SkillId == parent.SkillId && e.BindingId == parent.TriggerId && e.SkillInstanceId == parent.SkillInstanceId && e.Window == context.Window) == 1
                ? producer : null;
    }
    private bool IsFirstLegacySlashUse(CardUseFrame use, long producer, bool requireSingle = true) => use.Action is null && use.CardId == 0 &&
        use.PhysicalCardIds is { Count: 0 } && use.CardKind == CardKind.Slash && use.TargetSeats.Count > 0 && (!requireSingle || use.TargetSeats.Count == 1) &&
        IsValidPlayerSeat(use.TargetSeats[0]) && HasOngoingActualUseTurn && use.SourceSeat == _currentSeat &&
        FirstSlashDuelLegacyProducer(use) == producer &&
        CompleteProgramEventHistory().OfType<FirstSlashDuelUseRecordedEvent>().Any(e => e.CardUseFrameId == use.Id &&
            e.ActionId is null && e.Ordinal == 1 && e.ActorSeat == use.SourceSeat && e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat);
    private bool TryBeginFirstSlashDuelLegacyWindow(CardAttackHandle attack)
    {
        if (!HasFirstSlashDuelBenefitCapability || LifecycleCardUse(attack.ResolutionId) is not { } use ||
            use.Action is not null || FirstSlashDuelLegacyProducer(use) is not { } producer || !use.TargetSeats.Contains(attack.TargetSeat) ||
            !IsFirstLegacySlashUse(use, producer) || !_players[use.SourceSeat].IsAlive || !_players[attack.TargetSeat].IsAlive) return false;
        var facts = CaptureProgramTriggerFacts(_players[use.SourceSeat]);
        var candidates = CollectEligibleProgramTriggerCandidates(_players[use.SourceSeat], SkillProgramTriggerWindow.CardUseTargetsFinalized, facts)
            .Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit }]).ToArray();
        if (candidates.Length == 0) return false;
        var id = ++_resolutionSequence;
        var contexts = candidates.Select(c => new ProgramSkillWindowContext(SkillProgramTriggerWindow.CardUseTargetsFinalized,
            id, c.OwnerSeat, SourceSeat: use.SourceSeat, TargetSeat: attack.TargetSeat, Facts: facts)).ToArray();
        PushRuntimeFrame(new FirstSlashDuelLegacyWindowFrame(id, use.Id, producer, Array.AsReadOnly(candidates), Array.AsReadOnly(contexts)));
        AdvanceRuntimeTop<FirstSlashDuelLegacyWindowFrame>(); return true;
    }
    private bool CanRunFirstLegacyBenefit(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        var window = _resolutionStack.OfType<FirstSlashDuelLegacyWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId);
        var use = window is null ? null : LifecycleCardUse(window.ParentFrameId);
        return window is not null && use is not null && window.CandidateIndex < window.Candidates.Count &&
            window.Candidates[window.CandidateIndex] == c && window.Contexts[window.CandidateIndex] == context &&
            context.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && context.CardUse is null &&
            c.OwnerSeat == use.SourceSeat && _players[c.OwnerSeat].IsAlive && use.TargetSeats is [var target] && _players[target].IsAlive &&
            context.SourceSeat == use.SourceSeat && context.TargetSeat == target && IsFirstLegacySlashUse(use, window.ProducerFrameId) &&
            GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit }] &&
            !CompleteProgramEventHistory().OfType<FirstSlashDuelBenefitIssuedEvent>().Any(e => e.Receipt.CardUseFrameId == use.Id &&
                e.Receipt.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId));
    }
    private void ContinueFirstSlashDuelLegacyWindow()
    {
        while (_resolutionStack.LastOrDefault() is FirstSlashDuelLegacyWindowFrame w)
        {
            if (w.CandidateIndex >= w.Candidates.Count)
            {
                PopResolutionFrame(w.Id, ResolutionFrameKind.FirstSlashDuelLegacyWindow);
                var attack = ActiveCardAttack ?? throw new InvalidOperationException("The first-benefit legacy return lost its original Slash.");
                if (attack.ResolutionId != w.ParentFrameId || attack.ProgramSkillCardUseFrameId != w.ProducerFrameId)
                    throw new InvalidOperationException("The first-benefit legacy return changed its actual producer.");
                if (!TryBeginSlashTargetPenalties(attack, legacy: true) && !TryBeginSlashTargetBenefits(attack, legacy: true) &&
                    !TryBeginActualUseTargetPrograms(attack, ActualUseTargetReturnKind.LegacyVirtualSlash)) ContinueSlashAfterResponsePrograms(attack);
                return;
            }
            var c = w.Candidates[w.CandidateIndex]; var context = w.Contexts[w.CandidateIndex];
            if (!CanRunProgramTrigger(c, context))
            { ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); continue; }
            ReplaceRuntimeTop(w with { Step = ResolutionFrameStep.AwaitingResponse });
            ExposeProgramTriggerDecision(c, context); return;
        }
    }
    private void CompleteFirstLegacyBenefitCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (_resolutionStack.LastOrDefault() is not FirstSlashDuelLegacyWindowFrame w || w.Id != context.ParentFrameId ||
            w.CandidateIndex >= w.Candidates.Count || w.Candidates[w.CandidateIndex] != c || w.Contexts[w.CandidateIndex] != context)
            throw new InvalidOperationException("The first-benefit legacy child lost its exact actual candidate return.");
        ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceRuntimeTop<FirstSlashDuelLegacyWindowFrame>();
    }
    private void AssertFirstSlashDuelLegacyWindows()
    {
        foreach (var w in _resolutionStack.OfType<FirstSlashDuelLegacyWindowFrame>())
        {
            var use = LifecycleCardUse(w.ParentFrameId);
            if (use is null || !IsFirstLegacySlashUse(use, w.ProducerFrameId, requireSingle: false) ||
                w.Candidates.Count == 0 || w.Contexts.Count != w.Candidates.Count || w.CandidateIndex < 0 || w.CandidateIndex > w.Candidates.Count ||
                w.Candidates.Where((c, i) => c.OwnerSeat != use.SourceSeat || w.Contexts[i].ParentFrameId != w.Id ||
                    w.Contexts[i].Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || w.Contexts[i].CardUse is not null ||
                    GetProgramTrigger(c).Effects is not [{ Op: SkillProgramEffectOp.ChooseFirstSlashDuelDiamondBenefit }]).Any())
                throw new InvalidOperationException("The first-benefit legacy window lost its original Action-null Slash and exact producer.");
        }
    }
    private bool HasFirstSlashDuelLegacyUseObserver(long id) =>
        _resolutionStack.OfType<FirstSlashDuelLegacyWindowFrame>().Any(w => w.ParentFrameId == id &&
            LifecycleCardUse(id) is { } use && IsFirstLegacySlashUse(use, w.ProducerFrameId, requireSingle: false));
}
