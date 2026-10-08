using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private string SameNameHandDiscardReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.same-name-hand.discard";
    private int[] SameNameHandTargets(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner && GetHand(p).Count > 0).Select(p => p.Seat).ToArray();
    private SameNameHandMaterial[] SameNameHandMaterials(int seat, CardKind name) => GetHand(_players[seat])
        .Where(c => ProgramBasicCardName(c.Kind) == name && !IsSelfHandCategoryDiscardForbidden(seat, c, CardLocation.Hand(seat), OwnedCardMoveIntent.Discard))
        .Select(c => new SameNameHandMaterial(c.Id, c.Kind, CardLocation.Hand(seat))).ToArray();

    private bool SameNameHandActionParent(int owner, ProgramSkillWindowContext context, out ProgramCardTriggerWindowFrame window)
    {
        window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId)!;
        if (window is null || context.OwnerSeat != owner || context.CardUse is not { } use || use.ActorSeat != owner ||
            (window.CardSupplyCompletion?.ProviderSeat ?? window.ResponseCompletion?.CompletionActorSeat ?? window.Action.ActorSeat) != owner ||
            use.CardActionId != window.Action.ActionId || use.EffectiveKind != window.Action.EffectiveKind ||
            use.ParentCardUseFrameId != window.ParentFrameId || context.Window != GetCardActionWindow(window)) return false;
        if (context.Window == SkillProgramTriggerWindow.CardResponseCompleted)
            return window.ResponseCompletion is { CostsDrained: true } && ValidResponseCompletionWindow(window);
        if (context.Window == SkillProgramTriggerWindow.CardSupplyCompleted)
            return window.CardSupplyCompletion is { CostsDrained: true } && ValidCardSupplyCompletionWindow(window);
        return context.Window == SkillProgramTriggerWindow.CardUseCompleted && window.CardSupplyCompletion is null && window.ResponseCompletion is null && window.CompletedResponseReturn is null &&
            window.Action.Type == CardActionType.Use && LifecycleCardUse(window.ParentFrameId) is { } card && card.Action?.ActionId == window.Action.ActionId &&
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == card.Id && e.CardKind == use.EffectiveKind) == 1;
    }
    private bool CanOfferSameNameHandDiscardOrDamage(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage)) return true;
        return _winner == Winner.None && candidate.OwnerSeat != _currentSeat && _players[candidate.OwnerSeat].IsAlive &&
            HasRuntimeSkillInstance(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId) &&
            SameNameHandActionParent(candidate.OwnerSeat, context, out _) && SameNameHandTargets(candidate.OwnerSeat).Length > 0;
    }

    private SkillProgramStepOutcome BeginSameNameHandDiscardOrDamage(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (f.InstructionIndex != 1 || f.SameNameHandDiscardOrDamage is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            plan.Trigger is not { } trigger || trigger.Effects is not [{ Op: SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage }] ||
            f.WindowContext is not { } context || !SameNameHandActionParent(f.OwnerSeat, context, out var window) ||
            _resolutionStack.Count < 2 || _resolutionStack[^2].Id != window.Id || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])))
            throw new InvalidOperationException("A same-name hand demand requires its exact completed actor candidate.");
        if (!CanOfferSameNameHandDiscardOrDamage(ToSharedCandidate(window.Candidates[window.CandidateIndex]), trigger, context))
            return SkillProgramStepOutcome.Continue;
        var a = window.Action;
        var r = new ProgramSameNameHandReceipt { Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            GameplayHash = f.GameplayHash, ActionId = a.ActionId, CardWindowId = window.Id, OriginalParentFrameId = window.ParentFrameId,
            EffectiveKind = a.EffectiveKind, NormalizedName = ProgramBasicCardName(a.EffectiveKind), ActualTurnNumber = _turnNumber,
            ActualTurnOwnerSeat = _currentSeat, Stage = SameNameHandStage.ChoosingTarget, CandidateSeats = SameNameHandTargets(f.OwnerSeat) };
        ReplaceRuntimeTop(f = f with { SameNameHandDiscardOrDamage = r });
        AdvanceEventRulesAndQueueFact(new SameNameHandStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActionId, r.CardWindowId,
            r.OriginalParentFrameId, r.EffectiveKind, r.NormalizedName, r.ActualTurnNumber, r.ActualTurnOwnerSeat));
        PublishSameNameHandChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private PromptChoice SameNameHandChoice(ProgramSkillFrame f, string step, string label, int? card = null, int? target = null) =>
        new(new($"same-name-hand.{f.Id}.{step}.{card ?? target ?? -1}"), label, card is { } c ? [c] : [], target is { } t ? [t] : [],
            new Dictionary<string, string> { ["program-action"] = "same-name-hand", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["step"] = step });
    private IReadOnlyList<PromptChoice> SameNameHandChoices(ProgramSkillFrame f)
    {
        var r = f.SameNameHandDiscardOrDamage!;
        if (r.Stage == SameNameHandStage.ChoosingTarget)
            return Array.AsReadOnly(r.CandidateSeats.Select(s => SameNameHandChoice(f, "target", $"令{_players[s].Name}弃置同名手牌或受到1点伤害", target: s)).ToArray());
        if (r.Stage != SameNameHandStage.ChoosingPayment) return [];
        return Array.AsReadOnly(r.EligibleMaterials.Select(m => SameNameHandChoice(f, "discard", $"弃置【{GetAdvancedCard(m.CardId).DisplayName}】", card: m.CardId))
            .Append(SameNameHandChoice(f, "damage", "受到1点伤害")).ToArray());
    }
    private void PublishSameNameHandChoice(ProgramSkillFrame f)
    {
        var r = f.SameNameHandDiscardOrDamage!;
        var chooser = r.Stage == SameNameHandStage.ChoosingTarget ? f.OwnerSeat : r.TargetSeat!.Value;
        var choices = SameNameHandChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser,
            r.Stage == SameNameHandStage.ChoosingTarget ? "选择一名有手牌的其他角色。" : $"弃置一张【{CardCatalog.Get(r.NormalizedName).DisplayName}】手牌，或受到1点伤害。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()), Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveSameNameHandDiscardOrDamageChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A same-name demand lost its owning program.");
        AssertSameNameHandDiscardOrDamage(f); var r = f.SameNameHandDiscardOrDamage!;
        if (_pendingDecision is not { } prompt || !IsSameNameHandDiscardOrDamageChoice(f, prompt) ||
            !SameNameHandChoices(f).Any(c => SameNameHandChoicesEqual(c, choice)))
            throw new InvalidOperationException("A same-name demand requires its current private published choice.");
        ClearPendingDecision();
        if (r.Stage == SameNameHandStage.ChoosingTarget)
        {
            if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            { FinishSameNameHand(f); return; }
            var target = choice.Targets.Single();
            if (!SameNameHandTargets(f.OwnerSeat).Contains(target)) throw new InvalidOperationException("The original target must still be another living hand owner.");
            ReplaceRuntimeTop(f = f with { SameNameHandDiscardOrDamage = r with { Stage = SameNameHandStage.ChoosingPayment, TargetSeat = target,
                EligibleMaterials = SameNameHandMaterials(target, r.NormalizedName) } });
            AdvanceEventRulesAndQueueFact(new SameNameHandTargetSelectedEvent(f.Id, target)); PublishSameNameHandChoice(f); return;
        }
        if (r.Stage != SameNameHandStage.ChoosingPayment || r.TargetSeat is not { } seat)
            throw new InvalidOperationException("A same-name demand changed its original payment stage.");
        if (!_players[seat].IsAlive || !_players[f.OwnerSeat].IsAlive) { FinishSameNameHand(f); return; }
        if (choice.Parameters["step"] == "damage")
        {
            ReplaceRuntimeTop(f = f with { SameNameHandDiscardOrDamage = r with { Stage = SameNameHandStage.DamageIssued, DamageIssued = true } });
            AdvanceEventRulesAndQueueFact(new SameNameHandDamageIssuedEvent(f.Id, f.OwnerSeat, seat, 1));
            BeginProgramSkillDamage(f, seat, 1); return;
        }
        var material = r.EligibleMaterials.Single(m => m.CardId == choice.Cards.Single());
        if (!SameNameHandMaterials(seat, r.NormalizedName).Contains(material))
            throw new InvalidOperationException("The same-name payment must remain its exact legal hand entity.");
        ReplaceRuntimeTop(f = f with { SameNameHandDiscardOrDamage = r with { Stage = SameNameHandStage.DiscardChildren,
            PaidMaterial = material, SequenceBefore = _movementSequence, SequenceAfter = _movementSequence }, PendingMovementContinuation = new(seat, 0, null) });
        MoveProgramCardsFromMultipleSources([material.CardId], CardLocation.DiscardPile, new(SameNameHandDiscardReason(f)), (batch, records) =>
        {
            if (records is not [var paid] || paid.CardId != material.CardId || paid.CardKind != material.PrintedKind ||
                paid.From != material.From || paid.To != CardLocation.DiscardPile)
                throw new InvalidOperationException("The same-name demand did not commit exactly its selected hand payment.");
            var active = GetActiveProgramFrame(f.Id); var receipt = active.SameNameHandDiscardOrDamage!;
            ReplaceRuntimeTop(active = active with { SameNameHandDiscardOrDamage = receipt with { SequenceAfter = paid.Sequence, BatchId = batch } });
            AdvanceEventRulesAndQueueFact(new SameNameHandDiscardPaidEvent(active.Id, seat, material.CardId, receipt.SequenceBefore, paid.Sequence, batch));
        });
        AdvanceRuntimeProgram(f.Id);
    }

    private static bool SameNameHandChoicesEqual(PromptChoice a, PromptChoice b) => a.Id == b.Id && a.Cards.SequenceEqual(b.Cards) &&
        a.Targets.SequenceEqual(b.Targets) && a.Parameters.OrderBy(p => p.Key).SequenceEqual(b.Parameters.OrderBy(p => p.Key));
    private bool ResumeSameNameHandDiscardOrDamage(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SameNameHandDiscardOrDamage: { } r } f || f.Id != id) return false;
        AssertSameNameHandDiscardOrDamage(f);
        if (r.Stage is SameNameHandStage.ChoosingTarget or SameNameHandStage.ChoosingPayment)
        { if (_pendingDecision is null) PublishSameNameHandChoice(f); return true; }
        if (r.Stage == SameNameHandStage.DiscardChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        }
        if (r.Stage == SameNameHandStage.DamageIssued && f.AttackAttempt is not null) return false;
        FinishSameNameHand(f); return true;
    }
    private void FinishSameNameHand(ProgramSkillFrame f)
    {
        var r = f.SameNameHandDiscardOrDamage!;
        ReplaceRuntimeTop(f = f with { SameNameHandDiscardOrDamage = r with { Stage = SameNameHandStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new SameNameHandCompletedEvent(f.Id, r.TargetSeat, r.PaidMaterial is not null, r.DamageIssued));
        FinishProgramSkill(f, true);
    }
    private bool ReturnSameNameHandDiscardOrDamageMovement(ProgramSkillFrame f)
    {
        if (f.SameNameHandDiscardOrDamage is not { Stage: SameNameHandStage.DiscardChildren } || f.PendingMovementContinuation is null) return false;
        AssertSameNameHandDiscardOrDamage(f); AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsSameNameHandDiscardOrDamageMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        e is { Op: SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage } && f.SameNameHandDiscardOrDamage is { Stage: SameNameHandStage.DiscardChildren, TargetSeat: { } target } &&
        f.PendingMovementContinuation == pending && pending.SubjectSeat == target && ValidSameNameHandReceipt(f);
    private bool CanContinueSameNameHandDiscardOrDamage(ProgramSkillFrame f) =>
        f.SameNameHandDiscardOrDamage is { Stage: SameNameHandStage.DiscardChildren or SameNameHandStage.DamageIssued or SameNameHandStage.Complete } && ValidSameNameHandReceipt(f);
    private PromptChoice SelectAiSameNameHandDiscardOrDamage(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.SameNameHandDiscardOrDamage!.Stage == SameNameHandStage.ChoosingTarget)
        {
            var snapshot = CreateSnapshot(decision.PlayerSeat); var harm = new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
            return decision.Choices.OrderByDescending(c => _aiBrains[decision.PlayerSeat].ScoreProgramTarget(snapshot, c.Targets.Single(), harm))
                .ThenBy(c => c.Targets.Single()).First();
        }
        var discard = decision.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[decision.PlayerSeat])).ThenBy(c => c.Cards[0]).FirstOrDefault();
        return discard ?? decision.Choices.Single(c => c.Parameters["step"] == "damage");
    }
    private sealed partial class ProgramSkillHost : ISameNameHandDiscardOrDamageProgramHost
    {
        public SkillProgramStepOutcome OfferSameNameHandDiscardOrDamage(ProgramSkillFrame f) => engine.BeginSameNameHandDiscardOrDamage(f);
        public bool CanContinueSameNameHandDiscardOrDamage(ProgramSkillFrame f) => engine.CanContinueSameNameHandDiscardOrDamage(f);
    }
}
