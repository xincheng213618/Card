namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasSlashTargetBenefitCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferSlashTargetBenefit);
    private static bool IsSlashTargetBenefitWindow(SkillProgramTriggerWindow w) =>
        w is SkillProgramTriggerWindow.ActualSlashTargetBenefit or SkillProgramTriggerWindow.SlashDodgeCancelledBenefit;
    private ActualUseTargetIdentity? FreezeSlashTargetBenefitUse(CardAttackHandle attack, int? designatedTarget = null)
    {
        var target = designatedTarget ?? attack.TargetSeat;
        if (!HasSlashTargetBenefitCapability || LifecycleCardUse(attack.ResolutionId) is not { } use ||
            !IsSlashCard(use.CardKind) || !use.TargetSeats.Contains(target) ||
            attack.IsSourceLess || attack.CardUserSeat != use.SourceSeat || attack.SourceSeat != use.SourceSeat ||
            !IsValidPlayerSeat(use.SourceSeat) || !IsValidPlayerSeat(target)) return null;
        if (use.Action is { Type: CardActionType.Use } action && action.ActorSeat == use.SourceSeat && action.EffectiveKind == use.CardKind)
            return new(use.Id, action.ActionId, action.ActorSeat, action.ProviderSeat, action.EffectiveKind,
                target, _turnNumber, _currentSeat, null);
        return use.Action is null && SlashBenefitLegacyProducer(use, target) is { } parent
            ? new(use.Id, null, use.SourceSeat, use.SourceSeat, use.CardKind, target, _turnNumber, _currentSeat, parent) : null;
    }
    private bool MatchesSlashTargetBenefitUse(ActualUseTargetIdentity identity)
    {
        if (LifecycleCardUse(identity.CardUseFrameId) is not { } use || use.CardKind != identity.EffectiveKind ||
            !IsSlashCard(use.CardKind) ||
            identity.ActualTurnNumber != _turnNumber || identity.ActualTurnOwnerSeat != _currentSeat) return false;
        if (!use.TargetSeats.Contains(identity.TargetSeat) && !CompleteProgramEventHistory().OfType<SlashTargetBenefitOfferedEvent>().Any(e =>
            e.CardUseFrameId == use.Id && e.ActorSeat == identity.ActorSeat && e.TargetSeat == identity.TargetSeat &&
            (use.SlashTargetBenefits ?? []).Any(r => r.Use == identity && r.OfferWindowId == e.WindowId && r.OfferCandidateIndex == e.CandidateIndex))) return false;
        if (identity.ActionId is null)
            return identity.LegacyProducerProgramId is { } producer && use.Action is null && use.SourceSeat == identity.ActorSeat &&
                SlashBenefitLegacyProducer(use, identity.TargetSeat, issued: true) == producer;
        if (identity.LegacyProducerProgramId is not null || use.Action is not { Type: CardActionType.Use } action ||
            action.ActionId != identity.ActionId || action.ActorSeat != use.SourceSeat || action.ProviderSeat != identity.ProviderSeat ||
            action.EffectiveKind != identity.EffectiveKind || !(use.PhysicalCardIds ?? []).SequenceEqual(action.PhysicalCards.Select(c => c.CardId))) return false;
        if (use.SourceSeat == identity.ActorSeat) return true;
        // This is a read-only causal chain, never a new actor/source or fake Action.
        var history = CompleteProgramEventHistory().ToArray();
        var declaration = history.OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == use.Id && e.CardId == use.CardId).ToArray();
        if (declaration is not [var declared] || !history.OfType<CardActionAcceptedEvent>().Any(e =>
            e.Action.ActionId == action.ActionId && e.Action.Type == CardActionType.Use &&
            e.Action.ProviderSeat == identity.ProviderSeat && e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards))) return false;
        var cursor = declared.SourceSeat; var passedIssuer = cursor == identity.ActorSeat;
        foreach (var change in history.OfType<ProgramCardUseActorReplacedEvent>().Where(e => e.CardUseFrameId == use.Id))
        {
            if (change.PreviousActorSeat != cursor || change.OwnerSeat != cursor || change.ProviderSeat != identity.ProviderSeat ||
                !IsValidPlayerSeat(change.ActorSeat) || change.ActorSeat == cursor || !history.OfType<ProgramBindingStartedEvent>().Any(e =>
                    e.FrameId == change.FrameId && e.SkillId == change.SkillId && e.OwnerSeat == change.OwnerSeat &&
                    e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized)) return false;
            cursor = change.ActorSeat; passedIssuer |= cursor == identity.ActorSeat;
        }
        return passedIssuer && cursor == use.SourceSeat;
    }
    private long? SlashBenefitLegacyProducer(CardUseFrame use, int target, bool issued = false)
    {
        if (use.CardAttack is not { ProgramSkillCardUseFrameId: { } producer } attack ||
            !MatchesSameTypeAidLegacyProducer(use, producer)) return null;
        var declaration = CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Single(e => e.ResolutionId == use.Id);
        // This new root needs all finalized targets, not only the currently active
        // damage target. Added targets retain the mature public same-use fact.
        if (declaration.TargetSeats.Contains(target) || attack.TargetSeat == target ||
            CompleteProgramEventHistory().OfType<SameTypeAidTargetAddedEvent>().Any(e => e.RecipientSeat == target && IsSameTypeAidTargetFact(use, e)) ||
            issued && CompleteProgramEventHistory().OfType<SlashTargetBenefitOfferedEvent>().Any(e =>
                e.CardUseFrameId == use.Id && e.ActorSeat == use.SourceSeat && e.TargetSeat == target)) return producer;
        return null;
    }
    private SlashTargetBenefitReceipt? SlashTargetBenefitCurrent(SlashTargetBenefitReceipt identity) =>
        LifecycleCardUse(identity.Use.CardUseFrameId)?.SlashTargetBenefits?.SingleOrDefault(r =>
            r.OfferWindowId == identity.OfferWindowId && r.OfferCandidateIndex == identity.OfferCandidateIndex);
    private void ReplaceSlashTargetBenefit(SlashTargetBenefitReceipt receipt) =>
        UpdateLifecycleCardUse(receipt.Use.CardUseFrameId, u => u with { SlashTargetBenefits =
            Array.AsReadOnly((u.SlashTargetBenefits ?? []).Select(r => r.OfferWindowId == receipt.OfferWindowId &&
                r.OfferCandidateIndex == receipt.OfferCandidateIndex ? receipt : r).ToArray()) });

    private bool TryBeginSlashTargetBenefits(CardAttackHandle attack, bool legacy = false, bool afterActualTargets = false)
    {
        if (_winner != Winner.None || FreezeSlashTargetBenefitUse(attack) is not { } identity ||
            !_players[identity.ActorSeat].IsAlive || !_players[identity.TargetSeat].IsAlive) return false;
        var use = LifecycleCardUse(identity.CardUseFrameId)!;
        var facts = use.Action is { } a ? CaptureProgramTriggerFacts(_players[identity.ActorSeat], a) : CaptureProgramTriggerFacts(_players[identity.ActorSeat]);
        var eligible = CollectEligibleProgramTriggerCandidates(_players[identity.ActorSeat], SkillProgramTriggerWindow.ActualSlashTargetBenefit, facts)
            .Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.OfferSlashTargetBenefit }]).ToArray();
        var entries = new List<(ProgramTriggerCandidate Candidate, ActualUseTargetIdentity Identity)>();
        // The finalized list is captured before the FIRST target effect. Each target's
        // optional benefit completes here, rather than waiting for prior damage/death.
        // Any later actual addition is admitted once by its original unvisited key.
        foreach (var target in use.TargetSeats.Distinct())
        {
            if (!_players[target].IsAlive || FreezeSlashTargetBenefitUse(attack, target) is not { } targetIdentity) continue;
            foreach (var c in eligible)
                if (!(use.SlashTargetBenefits ?? []).Any(r => r.Use.ActorSeat == c.OwnerSeat && r.Use.TargetSeat == target &&
                    r.Source.SkillId == c.SkillId && r.Source.BindingId == c.BindingId)) entries.Add((c, targetIdentity));
        }
        if (entries.Count == 0) return false;
        var id = ++_resolutionSequence; var contexts = new List<ProgramSkillWindowContext>(); var receipts = new List<SlashTargetBenefitReceipt>();
        for (var i = 0; i < entries.Count; i++)
        {
            var (c, targetIdentity) = entries[i]; var trigger = GetProgramTrigger(c);
            var receipt = new SlashTargetBenefitReceipt(id, i, targetIdentity, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId),
                c.GameplayHash, trigger.Effects[0].StateId!, SlashTargetBenefitStage.Offered);
            receipts.Add(receipt);
            contexts.Add(new(SkillProgramTriggerWindow.ActualSlashTargetBenefit, id, c.OwnerSeat,
                SourceSeat: targetIdentity.ActorSeat, TargetSeat: targetIdentity.TargetSeat, OccurrenceIndex: c.OccurrenceIndex) { SlashTargetBenefit = receipt });
            AdvanceEventRulesAndQueueFact(new SlashTargetBenefitOfferedEvent(use.Id, id, i, targetIdentity.ActorSeat, targetIdentity.TargetSeat, receipt.Source, receipt.GameplayHash));
        }
        UpdateLifecycleCardUse(use.Id, u => u with { SlashTargetBenefits = Array.AsReadOnly((u.SlashTargetBenefits ?? []).Concat(receipts).ToArray()) });
        var returnKind = afterActualTargets ? legacy ? SlashTargetBenefitReturn.AfterActualTargetsLegacy : SlashTargetBenefitReturn.AfterActualTargetsSlash :
            legacy ? SlashTargetBenefitReturn.LegacyVirtualSlash : SlashTargetBenefitReturn.FinalizedSlash;
        PushRuntimeFrame(new SlashTargetBenefitWindowFrame(id, use.Id, returnKind,
            Array.AsReadOnly(entries.Select(e => e.Candidate).ToArray()), Array.AsReadOnly(contexts.ToArray())));
        AdvanceRuntimeTop<SlashTargetBenefitWindowFrame>(); return true;
    }
    private bool TryBeginDodgeCancelledSlashBenefits(CardAttackHandle attack)
    {
        var use = LifecycleCardUse(attack.ResolutionId);
        if (use is null || !HasSlashTargetBenefitCapability || attack.SuccessfulDodgeResponses < attack.RequiredDodgeResponses ||
            IsSlashDodgeCancellationPrevented(attack)) return false;
        var offered = (use.SlashTargetBenefits ?? []).Where(r => r.Stage == SlashTargetBenefitStage.Paid && r.Use.TargetSeat == attack.TargetSeat).ToArray();
        if (offered.Length == 0) return false;
        var id = ++_resolutionSequence; var candidates = new List<ProgramTriggerCandidate>(); var contexts = new List<ProgramSkillWindowContext>();
        foreach (var r in offered)
        {
            if (!ValidSlashTargetBenefitReceipt(r) || !MatchesSlashTargetBenefitUse(r.Use)) throw new InvalidOperationException("A Dodge cancellation lost its issued same-use benefit.");
            var qualified = r with { Stage = SlashTargetBenefitStage.CancellationQualified };
            ReplaceSlashTargetBenefit(qualified);
            AdvanceEventRulesAndQueueFact(new SlashTargetBenefitCancellationEvent(use.Id, r.OfferWindowId, r.OfferCandidateIndex, r.Use.ActorSeat, r.Use.TargetSeat));
            var candidate = new ProgramTriggerCandidate(r.Source.OwnerSeat, r.Source.SkillId, r.SettlementBinding,
                r.Source.SkillInstanceId!, r.GameplayHash, 0);
            candidates.Add(candidate);
            contexts.Add(new(SkillProgramTriggerWindow.SlashDodgeCancelledBenefit, id, candidate.OwnerSeat,
                SourceSeat: r.Use.ActorSeat, TargetSeat: r.Use.TargetSeat) { SlashTargetBenefit = qualified });
        }
        PushRuntimeFrame(new SlashTargetBenefitWindowFrame(id, use.Id, SlashTargetBenefitReturn.DodgeCancelled,
            Array.AsReadOnly(candidates.ToArray()), Array.AsReadOnly(contexts.ToArray())));
        AdvanceRuntimeTop<SlashTargetBenefitWindowFrame>(); return true;
    }
    private bool CanRunSlashTargetBenefitCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!IsSlashTargetBenefitWindow(context.Window) || context.SlashTargetBenefit is not { } identity ||
            SlashTargetBenefitCurrent(identity) is not { } receipt || receipt.Source.SkillId != c.SkillId ||
            receipt.Source.OwnerSeat != c.OwnerSeat || receipt.Source.SkillInstanceId != c.SkillInstanceId || receipt.GameplayHash != c.GameplayHash ||
            !MatchesSlashTargetBenefitUse(receipt.Use) || _contentRegistry.GetSkill(c.SkillId).Program is not { } definition || definition.GameplayHash != c.GameplayHash ||
            ProgramInstructionResolver.Default.FindTrigger(definition, c.BindingId) is not { } trigger || trigger.Window != context.Window) return false;
        if (context.Window == SkillProgramTriggerWindow.ActualSlashTargetBenefit)
            return receipt.Stage == SlashTargetBenefitStage.Offered && c.BindingId == receipt.Source.BindingId &&
                _winner == Winner.None && _players[c.OwnerSeat].IsAlive && _players[receipt.Use.TargetSeat].IsAlive &&
                LifecycleCardUse(receipt.Use.CardUseFrameId)!.TargetSeats.Contains(receipt.Use.TargetSeat) &&
                HasRuntimeSkillInstance(_players[c.OwnerSeat], c.SkillId, c.SkillInstanceId);
        return receipt.Stage == SlashTargetBenefitStage.CancellationQualified && receipt.SettlementBinding == c.BindingId && ValidSlashTargetBenefitReceipt(receipt);
    }
    private void ContinueSlashTargetBenefitWindowCore()
    {
        while (_resolutionStack.LastOrDefault() is SlashTargetBenefitWindowFrame w)
        {
            if (w.CandidateIndex >= w.Candidates.Count)
            {
                PopResolutionFrame(w.Id, ResolutionFrameKind.SlashTargetBenefitWindow);
                var attack = ActiveCardAttack;
                if (attack is null || attack.ResolutionId != w.ParentFrameId) throw new InvalidOperationException("A Slash benefit return lost its exact attack.");
                if (_winner != Winner.None) { SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); CompleteAttack(attack); }
                else if (w.ReturnKind == SlashTargetBenefitReturn.DodgeCancelled) ContinueSlashAfterDodgeCancellation(attack);
                else if (w.ReturnKind is SlashTargetBenefitReturn.AfterActualTargetsSlash or SlashTargetBenefitReturn.AfterActualTargetsLegacy)
                    ContinueSlashAfterTargetBenefitAdditions(attack, w.ReturnKind == SlashTargetBenefitReturn.AfterActualTargetsLegacy);
                else if (w.ReturnKind == SlashTargetBenefitReturn.LegacyVirtualSlash)
                {
                    if (!TryBeginActualUseTargetPrograms(attack, ActualUseTargetReturnKind.LegacyVirtualSlash)) ContinueSlashAfterResponsePrograms(attack);
                }
                else ContinueSlashAfterFinalizedTargets(attack);
                return;
            }
            var c = w.Candidates[w.CandidateIndex]; var context = w.Contexts[w.CandidateIndex];
            if (!CanRunSlashTargetBenefitCandidate(c, context))
            {
                var receipt = SlashTargetBenefitCurrent(context.SlashTargetBenefit!);
                if (receipt is not null) ReplaceSlashTargetBenefit(receipt with { Stage = receipt.Stage == SlashTargetBenefitStage.CancellationQualified
                    ? SlashTargetBenefitStage.Cancelled : SlashTargetBenefitStage.Declined });
                ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); continue;
            }
            ReplaceRuntimeTop(w with { Step = ResolutionFrameStep.AwaitingResponse });
            BeginProgramBinding(c, context); return;
        }
    }
    private void ContinueSlashAfterTargetBenefitAdditions(CardAttackHandle attack, bool legacy)
    {
        // New gains can themselves add another genuine target. Refill only this
        // capability's unvisited benefit keys before entering the original effect.
        if (TryBeginSlashTargetBenefits(attack, legacy, afterActualTargets: true)) return;
        if (TryBeginUnannouncedUniqueHpTargets(attack, legacy)) return;
        if (legacy)
        {
            if (IsCardEffectIneffective(attack.ResolutionId, attack.TargetSeat))
            { SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect); CompleteAttack(attack); }
            else ContinueSlashAfterResponsePrograms(attack);
        }
        else if (!TryBeginProgramCardUseBeforeTargetEffects(attack)) ContinueSlashAfterProgramTargetEffects(attack);
    }
    private void CompleteSlashTargetBenefitBinding(ProgramSkillFrame f)
    {
        if (_resolutionStack.LastOrDefault() is not SlashTargetBenefitWindowFrame w || w.Id != f.WindowContext?.ParentFrameId ||
            w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]))
            throw new InvalidOperationException("A Slash benefit child lost its exact candidate return.");
        var receipt = SlashTargetBenefitCurrent(w.Contexts[w.CandidateIndex].SlashTargetBenefit!);
        if (receipt?.Stage == SlashTargetBenefitStage.Offered) ReplaceSlashTargetBenefit(receipt with { Stage = SlashTargetBenefitStage.Declined });
        if (receipt?.Stage == SlashTargetBenefitStage.CancellationQualified) ReplaceSlashTargetBenefit(receipt with { Stage = SlashTargetBenefitStage.Cancelled });
        ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1, Step = ResolutionFrameStep.ResolvingEffect });
        AdvanceRuntimeTop<SlashTargetBenefitWindowFrame>();
    }
}
