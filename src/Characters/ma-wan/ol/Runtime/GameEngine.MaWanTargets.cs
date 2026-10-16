namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueMaWanHunjiangUse(long id, CardActionContext? action)
    {
        if (!HasMaWanCapability || action is not { Type: CardActionType.Use } || !IsSlashCard(action.EffectiveKind) ||
            action.ActorSeat != _currentSeat || _phase != TurnPhase.Play) return;
        var grants = ProgramEventHistory<MaWanHunjiangEntitledEvent>().Where(e => e.Source.OwnerSeat == action.ActorSeat &&
            e.ActualTurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId).ToArray();
        if (grants.Length == 0) return;
        UpdateLifecycleCardUse(id, use => use with { MaWanHunjiang = new(id, action.ActionId, action.ActorSeat, _turnNumber, _cardUseDebitPhaseInstanceId)
        { Grants = grants, OriginalTargets = use.TargetSeats } });
        foreach (var grant in grants) AdvanceEventRulesAndQueueFact(new MaWanHunjiangUseIssuedEvent(id, action.ActionId, grant.FrameId,
            grant.Source, grant.TargetSeat, grant.ActualTurnNumber, grant.PhaseInstanceId));
    }

    private void CollectIssuedMaWanHunjiangCandidates(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window != SkillProgramTriggerWindow.CardUseTargetsFinalized || _resolutionStack.LastOrDefault() is not
            CardUseFrame { MaWanHunjiang: { Offered: false } receipt } use || use.Action?.ActionId != action.ActionId ||
            action.ActorSeat != receipt.ActorSeat || !_players[action.ActorSeat].IsAlive || _winner != Winner.None) return;
        foreach (var grant in receipt.Grants.DistinctBy(g => (g.Source.SkillId, g.Source.SkillInstanceId)))
        {
            if (_contentRegistry.GetSkill(grant.Source.SkillId).Program is not { } program || program.GameplayHash != grant.GameplayHash) continue;
            var trigger = program.Triggers.Single(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets));
            if (result.Any(c => c.OwnerSeat == action.ActorSeat && c.SkillId == grant.Source.SkillId && c.SkillInstanceId == grant.Source.SkillInstanceId && c.TriggerId == trigger.Id)) continue;
            var context = CreateCardActionProgramContext(action, window, 0, action.ActorSeat, action.ActorSeat, CaptureProgramTriggerFacts(_players[action.ActorSeat], action));
            result.Add(new(action.ActorSeat, action.ActorSeat, grant.Source.SkillId, trigger.Id, program.GameplayHash, grant.Source.SkillInstanceId, trigger.Priority, context));
        }
    }

    private bool HasIssuedMaWanHunjiangCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != "ol:hunjiang" || context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } card } ||
            card.ActorSeat != candidate.OwnerSeat || !_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.ParentFrameId != card.ParentCardUseFrameId || window.Action.ActionId != card.CardActionId ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == card.ParentCardUseFrameId) is not { MaWanHunjiang: { } receipt, Action: { } action } use ||
            action.Type != CardActionType.Use || !IsSlashCard(action.EffectiveKind) || action.ActionId != card.CardActionId ||
            action.ActorSeat != candidate.OwnerSeat || use.SourceSeat != action.ActorSeat || receipt.ActorSeat != action.ActorSeat ||
            receipt.UseId != use.Id || receipt.ActionId != action.ActionId || receipt.ActualTurnNumber != _turnNumber ||
            receipt.PhaseInstanceId != _cardUseDebitPhaseInstanceId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var current = window.Candidates[window.CandidateIndex];
        if (current.OwnerSeat != candidate.OwnerSeat || current.SkillId != candidate.SkillId || current.TriggerId != candidate.BindingId ||
            current.SkillInstanceId != candidate.SkillInstanceId || current.GameplayHash != candidate.GameplayHash || CreateCardActionProgramContext(window, current) != context) return false;
        return _contentRegistry.GetSkill(candidate.SkillId).Program is { } p && p.GameplayHash == candidate.GameplayHash &&
            p.Triggers.Any(t => t.Id == candidate.BindingId && t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets)) &&
            receipt.Grants.Any(g => g.Source.OwnerSeat == candidate.OwnerSeat && g.Source.SkillId == candidate.SkillId && g.Source.SkillInstanceId == candidate.SkillInstanceId &&
                g.GameplayHash == candidate.GameplayHash && ProgramEventHistory<MaWanHunjiangUseIssuedEvent>().Any(e => e.UseId == use.Id && e.ActionId == action.ActionId &&
                    e.GrantFrameId == g.FrameId && e.Source == g.Source && e.TargetSeat == g.TargetSeat && e.ActualTurnNumber == g.ActualTurnNumber && e.PhaseInstanceId == g.PhaseInstanceId));
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26510)]
    private bool CanOfferMaWan(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets) || HasIssuedMaWanHunjiangCandidate(candidate, context) &&
            _resolutionStack.OfType<CardUseFrame>().Single(u => u.Id == context.CardUse!.ParentCardUseFrameId).MaWanHunjiang is { Offered: false };

    private CardUseFrame RequireMaWanUse(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } context || !HasIssuedMaWanHunjiangCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), context))
            throw new InvalidOperationException("Hunjiang lost its exact issued native finalized-use candidate.");
        return _resolutionStack.OfType<CardUseFrame>().Single(u => u.Id == context.CardUse!.ParentCardUseFrameId);
    }

    private int[] MaWanExtraTargets(ProgramSkillFrame f, CardUseFrame use) => use.MaWanHunjiang!.Grants.Where(g =>
        g.Source.SkillId == f.SkillId && g.Source.SkillInstanceId == f.SkillInstanceId && g.GameplayHash == f.GameplayHash &&
        !use.TargetSeats.Contains(g.TargetSeat) && !f.MaWanHunjiang!.SelectedTargets.Contains(g.TargetSeat) &&
        CanBeProgramCardUseRoleTarget(use, _players[g.TargetSeat])).Select(g => g.TargetSeat).Distinct().Order().ToArray();

    private SkillProgramStepOutcome BeginMaWanTargets(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = RequireMaWanUse(f); var receipt = use.MaWanHunjiang!;
        if (f.MaWanHunjiang is not null || receipt.Offered || f.InstructionIndex != 1)
            throw new InvalidOperationException("Hunjiang can offer its issued target extension only once.");
        ReplaceRuntimeTop(f = f with { MaWanHunjiang = new(MaWanSource(f), f.GameplayHash, f.InstructionIndex, receipt.ActualTurnNumber,
            receipt.PhaseInstanceId, MaWanHunjiangStage.AppendTargets, CardUseId: use.Id, ActionId: use.Action!.ActionId) });
        if (MaWanExtraTargets(f, use).Length == 0) { CompleteMaWanTargets(f); return SkillProgramStepOutcome.AwaitChoice; }
        PublishMaWan(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private void CompleteMaWanTargets(ProgramSkillFrame f)
    {
        var r = f.MaWanHunjiang!; var use = RequireMaWanUse(f); var issued = use.MaWanHunjiang!;
        if (r.SelectedTargets.Any(s => use.TargetSeats.Contains(s) || !CanBeProgramCardUseRoleTarget(use, _players[s])))
            throw new InvalidOperationException("Hunjiang's extra target no longer satisfies the current native Slash target rules.");
        var targets = Array.AsReadOnly(use.TargetSeats.Concat(r.SelectedTargets).ToArray());
        var action = CloneRoleAction(use.Action!, use.SourceSeat, targets);
        UpdateProgramRoleCardUse(use with { TargetSeats = targets, Action = action, MaWanHunjiang = issued with { Offered = true, OriginalTargets = use.TargetSeats, AddedTargets = r.SelectedTargets },
            Enhancements = r.SelectedTargets.Count > 0 ? use.Enhancements | CurrentCardEnhancement.ExtraTarget : use.Enhancements,
            EnhancementOwnerSeat = r.SelectedTargets.Count > 0 ? f.OwnerSeat : use.EnhancementOwnerSeat }, action);
        for (var i = 0; i < r.SelectedTargets.Count; i++) AdvanceEventRulesAndQueueFact(new MaWanHunjiangTargetAddedEvent(f.Id, use.Id, action.ActionId, r.Source, r.SelectedTargets[i], i));
        FinishMaWan(GetActiveProgramFrame(f.Id));
    }

    private bool HasMaWanHunjiangTargetTail(CardUseFrame use)
    {
        if (use.MaWanHunjiang is not { Offered: true, AddedTargets.Count: > 0 } r || use.Action is not { Type: CardActionType.Use } action ||
            !IsSlashCard(use.CardKind) || action.EffectiveKind != use.CardKind || action.ActorSeat != use.SourceSeat || r.ActorSeat != use.SourceSeat ||
            r.UseId != use.Id || r.ActionId != action.ActionId || r.AddedTargets.Distinct().Count() != r.AddedTargets.Count ||
            !r.AddedTargets.All(s => action.EffectiveDesignatedTargetSeats.Contains(s) && r.Grants.Any(g => g.TargetSeat == s &&
                ProgramEventHistory<MaWanHunjiangTargetAddedEvent>().Count(e => e.UseId == use.Id && e.ActionId == action.ActionId &&
                    e.Source.SkillId == g.Source.SkillId && e.Source.SkillInstanceId == g.Source.SkillInstanceId && e.Source.OwnerSeat == g.Source.OwnerSeat &&
                    e.Source.BindingId == "issued-phase-slash-targets" && e.TargetSeat == s) == 1))) return false;
        var actual = action.TargetSeats.ToArray();
        foreach (var redirect in ProgramEventHistory<MaWanHunjiangRedirectedEvent>().Where(e => e.UseId == use.Id))
        {
            if (redirect.ActionId != action.ActionId || redirect.TargetIndex < 0 || redirect.TargetIndex >= actual.Length ||
                actual[redirect.TargetIndex] != redirect.OriginalTargetSeat || redirect.Source.OwnerSeat != redirect.OriginalTargetSeat ||
                _contentRegistry.GetSkill(redirect.Source.SkillId).Program is not { } p || p.GameplayHash != redirect.GameplayHash ||
                ProgramInstructionResolver.Default.Find(p, ProgramInstructionSourceKind.Trigger, redirect.Source.BindingId) is not { } plan ||
                plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || redirect.InstructionIndex < 1 || redirect.InstructionIndex > plan.Instructions.Count ||
                plan.GetPausedInstruction(redirect.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == redirect.ProgramFrameId && e.OwnerSeat == redirect.Source.OwnerSeat &&
                    e.SkillId == redirect.Source.SkillId && e.BindingId == redirect.Source.BindingId && e.SkillInstanceId == redirect.Source.SkillInstanceId) != 1) return false;
            actual[redirect.TargetIndex] = redirect.NewTargetSeat;
        }
        return use.TargetSeats.SequenceEqual(actual);
    }

    private void RecordMaWanHunjiangRedirect(ProgramSkillFrame redirector, long useId, int originalSeat, int newSeat)
    {
        if (LifecycleCardUse(useId) is not { MaWanHunjiang: { Offered: true, AddedTargets.Count: > 0 }, Action: { } action } use) return;
        var p = _contentRegistry.GetSkill(redirector.SkillId).Program!;
        if (p.GameplayHash != redirector.GameplayHash || redirector.InstructionIndex < 1 ||
            ProgramInstructionResolver.Default.Resolve(redirector, p).GetPausedInstruction(redirector.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.RedirectCurrentAttack } effect ||
            newSeat != ResolveProgramEffectTarget(redirector, effect.Target) || redirector.OwnerSeat != originalSeat ||
            redirector.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } c ||
            context.ParentCardUseFrameId != use.Id || context.CardActionId != action.ActionId || c.TargetSeat != originalSeat || c.SourceSeat != use.SourceSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } window ||
            window.Continuation != ProgramCardContinuation.SlashTargetRedirecting || window.ParentFrameId != use.Id || window.Action.ActionId != action.ActionId ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(redirector, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != newSeat ||
            ProgramEventHistory<MaWanHunjiangRedirectedEvent>().Any(e => e.ProgramFrameId == redirector.Id && e.UseId == use.Id && e.InstructionIndex == redirector.InstructionIndex))
            throw new InvalidOperationException("Hunjiang's native target redirect lost its exact owning producer and actual target cursor.");
        AdvanceEventRulesAndQueueFact(new MaWanHunjiangRedirectedEvent(redirector.Id, use.Id, action.ActionId,
            new(redirector.SkillId, GetProgramBindingId(redirector), redirector.OwnerSeat, redirector.SkillInstanceId), redirector.GameplayHash,
            redirector.InstructionIndex, use.TargetIndex, originalSeat, newSeat));
    }

    private bool TryGetMaWanVirtualPrimaryReturn(CardUseFrame use, ProgramSkillFrame parent, out IReadOnlyList<int> primary)
    {
        primary = [];
        if (!HasMaWanHunjiangTargetTail(use) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.CardKind != CardKind.Slash ||
            use.CardAttack is not { CardId: null, EffectiveCardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != parent.Id || attack.SourceSeat != use.SourceSeat ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash } action || action.ActorSeat != use.SourceSeat || action.ProviderSeat != action.ActorSeat ||
            action.PhysicalCards.Count != 0 || action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            action.EffectiveSuit != Suit.None || action.EffectiveRank != 0 || parent.InstructionIndex < 1) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != parent.Id || _contentRegistry.GetSkill(parent.SkillId).Program is not { } p || p.GameplayHash != parent.GameplayHash) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { TargetSeats.Count: 1 } confirmed ||
            history.OfType<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { CardId: 0, CardKind: CardKind.Slash } declared || declared.SourceSeat != action.ActorSeat ||
            history.OfType<CardActionAcceptedEvent>().Select(e => e.Action).FirstOrDefault(a => a.ActionId == action.ActionId) is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0 } accepted ||
            accepted.ActorSeat != action.ActorSeat || accepted.ProviderSeat != action.ProviderSeat || !accepted.TargetSeats.SequenceEqual(confirmed.TargetSeats) ||
            !accepted.ConversionChain.SequenceEqual(action.ConversionChain)) return false;
        var original = confirmed.TargetSeats[0]; var op = ProgramInstructionResolver.Default.Resolve(parent, p).GetPausedInstruction(parent.InstructionIndex).Effect;
        var exact = op.Op switch
        {
            SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats.SequenceEqual([action.ActorSeat]) && action.ConversionChain.Count == 0,
            SkillProgramEffectOp.UseVirtualSlash => parent.OwnerSeat == action.ActorSeat &&
                action.ConversionChain.SequenceEqual([new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId)]) &&
                (op.TargetReference is null ? parent.SelectedTargetSeats.SequenceEqual([original]) : op.TargetReference.Kind == ProgramParticipantRef.EventTarget && parent.WindowContext?.TargetSeat == original),
            SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash => parent.OwnerSeat == action.ActorSeat && parent.SelectedTargetSeats.SequenceEqual([original]) &&
                action.ConversionChain.Count == 0 && op.UseCardActionWindows && op.OutputKind == CardKind.Slash && op.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget &&
                (parent.TriggerId is not null || op.Op == SkillProgramEffectOp.OfferUnlimitedVirtualSlash),
            _ => false
        };
        if (!exact) return false;
        primary = Array.AsReadOnly(new[] { use.TargetSeats[0] }); return true;
    }
}
