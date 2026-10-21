namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void QueueSoleTargetRedirect(CardUseFrame use, SoleTargetProtectionReceipt receipt)
    {
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == receipt.WindowFrameId);
        var targetIndex = Array.IndexOf(use.TargetSeats.ToArray(), receipt.Source.OwnerSeat);
        if (window.Continuation != ProgramCardContinuation.Slash || window.SoleTargetRedirectPending is not null ||
            window.SoleTargetRedirectReturn is not null || SoleProtectionMainTargets(use) is not [var target] || target != receipt.Source.OwnerSeat ||
            use.CardAttack is null || targetIndex < 0)
            throw new InvalidOperationException("A protected Slash lost its exact new-target redirection boundary.");
        ReplaceRuntimeFrame(window.Id, window with
        { SoleTargetRedirectPending = new(use.Id, use.Action!.ActionId, receipt.Source, target, targetIndex) });
    }

    private bool TryBeginSoleTargetRedirect(ProgramCardTriggerWindowFrame frame)
    {
        if (frame.SoleTargetRedirectPending is not { } request) return false;
        if ((!HasSoleTargetProtectionCapability && !TargetWindowProtocols.AllowsRedirectSource(request.ProtectionSource)) || frame.Continuation != ProgramCardContinuation.Slash ||
            frame.SoleTargetRedirectReturn is not null || LifecycleCardUse(request.CardUseFrameId) is not { Action: { } action } use ||
            frame.ParentFrameId != use.Id || frame.Action.ActionId != request.ActionId || action.ActionId != request.ActionId ||
            use.CardAttack is not { } resumeAttack || request.TargetIndex < 0 || request.TargetIndex >= use.TargetSeats.Count ||
            use.TargetSeats[request.TargetIndex] != request.TargetSeat ||
            !CompleteProgramEventHistory().OfType<SoleTargetReplacedEvent>().Any(e => e.Source == request.ProtectionSource &&
                e.NewTargetSeat == request.TargetSeat && IsSoleTargetReplacementFact(use, e, e.BeforeTargets)))
            throw new InvalidOperationException("A protected Slash cannot reopen redirection without its exact issued replacement.");
        var saved = frame with { SoleTargetRedirectPending = null };
        var resumeIndex = use.TargetIndex;
        var targetAttack = use.PreparedTargetAttacks is { } prepared && request.TargetIndex < prepared.Count
            ? prepared[request.TargetIndex] with { Active = resumeAttack.Active }
            : resumeAttack with { TargetSeat = request.TargetSeat,
                IgnoresArmor = HasCardArmorBypass(_players[use.SourceSeat], _players[request.TargetSeat], use.CardKind) };
        UpdateLifecycleCardUse(use.Id, current => current with { TargetIndex = request.TargetIndex, CardAttack = targetAttack });
        var candidates = SoleTargetRedirectCandidates(frame, use, [request.TargetSeat]);
        AdvanceEventRulesAndQueueFact(new SoleTargetRedirectWindowIssuedEvent(frame.Id, request, saved.CandidateIndex));
        ReplaceRuntimeTop(frame with { Action = action, Continuation = ProgramCardContinuation.SlashTargetRedirecting,
            Candidates = candidates, CandidateIndex = 0, Activated = false, SoleTargetRedirectPending = null,
            SoleTargetRedirectReturn = new(saved, request, resumeIndex, resumeAttack.TargetSeat, resumeAttack.IgnoresArmor) });
        return true;
    }

    private IReadOnlyList<ProgramCardTriggerCandidate> SoleTargetRedirectCandidates(ProgramCardTriggerWindowFrame frame,
        CardUseFrame use, IReadOnlyList<int> targets) => Array.AsReadOnly(CollectSharedCardActionCandidates(use.Action!,
            SkillProgramTriggerWindow.SlashTargetRedirecting, targets, null, ProgramCardContinuation.SlashTargetRedirecting)
        .OrderByDescending(c => c.Priority).ThenBy(c => RelativeTableOrder(0, c.OwnerSeat)).ThenBy(c => c.SkillId, StringComparer.Ordinal)
        .ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).ThenBy(c => c.TriggerId, StringComparer.Ordinal)
        .Select(c => c with { FrozenContext = c.FrozenContext! with { ParentFrameId = frame.Id,
            CardUse = c.FrozenContext.CardUse! with { ParentCardUseFrameId = use.Id } } }).ToArray());

    private bool TryReturnSoleTargetRedirect(ProgramCardTriggerWindowFrame frame)
    {
        if (frame.SoleTargetRedirectReturn is not { } returned) return false;
        if (!IsExactSoleTargetRedirectReturn(frame) || frame.CandidateIndex != frame.Candidates.Count ||
            LifecycleCardUse(frame.ParentFrameId) is not { Action: { } action } use)
            throw new InvalidOperationException("A protected Slash redirection lost its saved finalized cursor.");
        if (returned.ResumeTargetIndex != returned.Request.TargetIndex)
        {
            UpdateLifecycleCardUse(use.Id, current => current with { TargetIndex = returned.ResumeTargetIndex,
                CardAttack = current.CardAttack! with { TargetSeat = returned.ResumeTargetSeat, IgnoresArmor = returned.ResumeIgnoresArmor } });
            use = LifecycleCardUse(use.Id)!;
        }
        ReplaceRuntimeTop(returned.FinalizedCursor with { Action = action });
        RefreshCurrentUseAddedTargetCandidates(use, SoleProtectionMainTargets(use));
        RefreshSoleProtectionObservers(use);
        return true;
    }

    private bool IsExactSoleTargetRedirectReturn(ProgramCardTriggerWindowFrame frame)
    {
        if (frame.SoleTargetRedirectReturn is not { FinalizedCursor: var saved, Request: var request } ||
            frame.Continuation != ProgramCardContinuation.SlashTargetRedirecting || frame.SoleTargetRedirectPending is not null ||
            saved.Id != frame.Id || saved.ParentFrameId != frame.ParentFrameId || saved.Continuation != ProgramCardContinuation.Slash ||
            saved.SoleTargetRedirectPending is not null || saved.SoleTargetRedirectReturn is not null ||
            frame.SoleTargetRedirectReturn.TargetEpisodeCandidateStartIndex < 0 ||
            frame.SoleTargetRedirectReturn.TargetEpisodeCandidateStartIndex > frame.Candidates.Count ||
            saved.CandidateIndex < 1 || saved.CandidateIndex > saved.Candidates.Count ||
            frame.AttackOwnerFrameId != frame.ParentFrameId || request.CardUseFrameId != frame.ParentFrameId ||
            request.ActionId != frame.Action.ActionId || LifecycleCardUse(frame.ParentFrameId) is not { Action: { } action } use ||
            !IsSlashCard(use.CardKind) || !action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            !frame.Action.TargetSeats.SequenceEqual(use.TargetSeats) || request.TargetIndex < 0 || request.TargetIndex >= saved.Action.TargetSeats.Count ||
            use.TargetIndex != request.TargetIndex || use.CardAttack?.TargetSeat != use.TargetSeats[request.TargetIndex] ||
            saved.Action.TargetSeats[request.TargetIndex] != request.TargetSeat ||
            frame.SoleTargetRedirectReturn.ResumeTargetIndex < 0 || frame.SoleTargetRedirectReturn.ResumeTargetIndex >= saved.Action.TargetSeats.Count ||
            saved.Action.TargetSeats[frame.SoleTargetRedirectReturn.ResumeTargetIndex] != frame.SoleTargetRedirectReturn.ResumeTargetSeat) return false;
        var previous = saved.Candidates[saved.CandidateIndex - 1];
        return previous.OwnerSeat == request.ProtectionSource.OwnerSeat && previous.SkillId == request.ProtectionSource.SkillId &&
            previous.TriggerId == request.ProtectionSource.BindingId && previous.SkillInstanceId == request.ProtectionSource.SkillInstanceId &&
            CompleteProgramEventHistory().OfType<SoleTargetRedirectWindowIssuedEvent>().Count(e => e.WindowFrameId == frame.Id &&
                e.Request == request && e.FinalizedCandidateIndex == saved.CandidateIndex) == 1 &&
            CompleteProgramEventHistory().OfType<SoleTargetReplacedEvent>().Any(e => e.Source == request.ProtectionSource &&
                e.NewTargetSeat == request.TargetSeat && IsSoleTargetReplacementFact(use, e, e.BeforeTargets));
    }

    private void RecordSoleTargetAfterProtectionRedirect(ProgramSkillFrame redirector, long useId, int previousSeat, int newSeat)
    {
        if (redirector.WindowContext is not { ParentFrameId: var windowId } context ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == windowId) is not
                { SoleTargetRedirectReturn: not null } window) return;
        var use = LifecycleCardUse(useId)!; var action = use.Action!;
        var source = new CardConversionSource(redirector.SkillId, GetProgramBindingId(redirector), redirector.OwnerSeat, redirector.SkillInstanceId);
        var program = _contentRegistry.GetSkill(redirector.SkillId).Program!;
        var instruction = ProgramInstructionResolver.Default.Resolve(redirector, program).GetPausedInstruction(redirector.InstructionIndex).Effect;
        // The native redirect has already updated TargetSeats and the active attack.
        // Its previous action is the immutable before-target snapshot for this fact.
        if (window.Continuation != ProgramCardContinuation.SlashTargetRedirecting || window.ParentFrameId != useId ||
            context.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || context.CardUse?.CardActionId != action.ActionId ||
            context.CardUse.ParentCardUseFrameId != use.Id || redirector.OwnerSeat != previousSeat ||
            instruction.Op != SkillProgramEffectOp.RedirectCurrentAttack || newSeat != ResolveProgramEffectTarget(redirector, instruction.Target) ||
            use.TargetIndex != window.SoleTargetRedirectReturn!.Request.TargetIndex ||
            use.TargetIndex < 0 || use.TargetIndex >= action.TargetSeats.Count || action.TargetSeats[use.TargetIndex] != previousSeat ||
            use.TargetSeats.Count != action.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != newSeat ||
            !action.TargetSeats.Select((seat, i) => i == use.TargetIndex ? newSeat : seat).SequenceEqual(use.TargetSeats) ||
            window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(redirector, ToSharedCandidate(window.Candidates[window.CandidateIndex])))
            throw new InvalidOperationException("A protected Slash redirect lost its exact source, target cursor or real instruction.");
        AdvanceEventRulesAndQueueFact(new SoleTargetAfterProtectionRedirectedEvent(window.Id, redirector.Id, use.Id, action.ActionId,
            source, redirector.GameplayHash, redirector.InstructionIndex, use.TargetIndex, previousSeat, newSeat,
            Array.AsReadOnly(action.TargetSeats.ToArray()), Array.AsReadOnly(use.TargetSeats.ToArray())));
        var updatedAction = CloneDesignatedExtraTargetAction(action, use.TargetSeats);
        var prepared = use.PreparedTargetAttacks is null ? null : Array.AsReadOnly(use.PreparedTargetAttacks.Select((a, i) =>
            i != use.TargetIndex ? a : a with { TargetSeat = newSeat, IgnoresArmor = use.CardAttack!.IgnoresArmor }).ToArray());
        UpdateProgramRoleCardUse(use with { Action = updatedAction, PreparedTargetAttacks = prepared }, updatedAction);
        window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == windowId);
        var added = SoleTargetRedirectCandidates(window, LifecycleCardUse(useId)!, [newSeat]).Where(c =>
            c.OwnerSeat == newSeat && c.OpponentSeat == newSeat &&
            _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Single(t => t.Id == c.TriggerId).OwnerRelation == SkillProgramCardActionOwnerRelation.Target).ToArray();
        // A new becoming-target episode owns fresh contexts. Retire old target
        // candidates even when that same owner is later reached again; their
        // original facts and paid program belong to the earlier episode.
        ReplaceRuntimeFrame(window.Id, window with
        {
            Candidates = Array.AsReadOnly(window.Candidates.Concat(added).ToArray()),
            SoleTargetRedirectReturn = window.SoleTargetRedirectReturn! with { TargetEpisodeCandidateStartIndex = window.Candidates.Count }
        });
    }

    private bool IsSoleTargetAfterProtectionRedirectFact(CardUseFrame use, SoleTargetAfterProtectionRedirectedEvent fact,
        IReadOnlyList<int> before)
    {
        if (fact.CardUseFrameId != use.Id || fact.ActionId != use.Action?.ActionId || fact.TargetIndex < 0 || fact.TargetIndex >= before.Count ||
            !fact.BeforeTargets.SequenceEqual(before) || before[fact.TargetIndex] != fact.PreviousTargetSeat ||
            fact.Source.OwnerSeat != fact.PreviousTargetSeat || fact.AfterTargets.Count != before.Count || fact.AfterTargets[fact.TargetIndex] != fact.NewTargetSeat ||
            !before.Select((seat, i) => i == fact.TargetIndex ? fact.NewTargetSeat : seat).SequenceEqual(fact.AfterTargets) ||
            fact.NewTargetSeat == fact.PreviousTargetSeat || fact.NewTargetSeat == use.SourceSeat || !IsValidPlayerSeat(fact.NewTargetSeat) ||
            _contentRegistry.GetSkill(fact.Source.SkillId).Program is not { } program || program.GameplayHash != fact.GameplayHash ||
            ProgramInstructionResolver.Default.Find(program, ProgramInstructionSourceKind.Trigger, fact.Source.BindingId) is not { } plan ||
            plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || fact.InstructionIndex < 1 ||
            fact.InstructionIndex > plan.Instructions.Count || plan.GetPausedInstruction(fact.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack)
            return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<SoleTargetAfterProtectionRedirectedEvent>().Where(e => e.WindowFrameId == fact.WindowFrameId &&
                e.ProgramFrameId == fact.ProgramFrameId && e.InstructionIndex == fact.InstructionIndex).ToArray() is [var issued] &&
            issued.CardUseFrameId == fact.CardUseFrameId && issued.ActionId == fact.ActionId && issued.Source == fact.Source &&
            issued.GameplayHash == fact.GameplayHash && issued.TargetIndex == fact.TargetIndex &&
            issued.PreviousTargetSeat == fact.PreviousTargetSeat && issued.NewTargetSeat == fact.NewTargetSeat &&
            issued.BeforeTargets.SequenceEqual(fact.BeforeTargets) && issued.AfterTargets.SequenceEqual(fact.AfterTargets) &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.SkillId == fact.Source.SkillId &&
                e.OwnerSeat == fact.Source.OwnerSeat && e.BindingId == fact.Source.BindingId && e.SkillInstanceId == fact.Source.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.SlashTargetRedirecting) == 1 &&
            history.OfType<SoleTargetRedirectWindowIssuedEvent>().Any(e => e.WindowFrameId == fact.WindowFrameId && e.Request.CardUseFrameId == use.Id &&
                e.Request.ActionId == fact.ActionId && e.Request.TargetIndex == fact.TargetIndex && history.OfType<SoleTargetReplacedEvent>().Any(r => r.Source == e.Request.ProtectionSource &&
                    r.NewTargetSeat == e.Request.TargetSeat && IsSoleTargetReplacementFact(use, r, r.BeforeTargets)));
    }
}
