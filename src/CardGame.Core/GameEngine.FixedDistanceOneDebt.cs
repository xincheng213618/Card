using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string FixedDistanceDebtReason = "program.fixed-distance-one.ending-discard";
    private IEnumerable<FixedDistanceOneTurnGrant> FixedDistanceOneGrants() =>
        CompleteProgramEventHistory().OfType<FixedDistanceOneTurnGrantedEvent>().Select(e => e.Grant);
    private bool FixedDistanceDebtConsumed(FixedDistanceOneTurnGrant g) => CompleteProgramEventHistory()
        .OfType<FixedDistanceOneDebtConsumedEvent>().Any(e => e.Grant == g);
    private bool ValidFixedDistanceOneGrant(FixedDistanceOneTurnGrant g) => g.ProgramFrameId > 0 && g.InstructionIndex == 1 &&
        IsValidPlayerSeat(g.TargetSeat) && IsValidPlayerSeat(g.ActualTurnOwnerSeat) && g.Source.OwnerSeat == g.ActualTurnOwnerSeat &&
        g.TargetSeat != g.Source.OwnerSeat && FixedDistanceOneGrants().Count(other => other.ProgramFrameId == g.ProgramFrameId) == 1 &&
        _contentRegistry.GetSkill(g.Source.SkillId).Program is { } program && program.GameplayHash == g.GameplayHash &&
        program.Activations.Any(a => a.Id == g.Source.BindingId && a.Effects is [{ Op: SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy }]) &&
        CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == g.ProgramFrameId &&
            e.OwnerSeat == g.Source.OwnerSeat && e.SkillId == g.Source.SkillId && e.ActivationId == g.Source.BindingId) == 1 &&
        program.Triggers.Any(t => t.Id == g.EndingBindingId && t.TurnOwnerScope == SkillProgramTurnOwnerScope.IssuedFixedDistanceEnding &&
            t.Effects is [{ Op: SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt }]) &&
        CompleteProgramEventHistory().OfType<TurnStartedEvent>().Any(e => e.TurnNumber == g.ActualTurnNumber && e.ActorSeat == g.ActualTurnOwnerSeat);
    private void GrantFixedDistanceOneTurnPolicy(ProgramSkillFrame supplied, int target)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var activation = _contentRegistry.GetSkill(f.SkillId).Program!.Activations.Single(a => a.Id == f.ActivationId);
        if (f.InstructionIndex != 1 || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || _winner != Winner.None ||
            !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive || target == f.OwnerSeat ||
            f.SelectedTargetSeats is not [var selected] || selected != target || f.SelectedCardIds.Count != 0 ||
            activation.Effects is not [{ Op: SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy }] ||
            FixedDistanceOneGrants().Any(g => g.ProgramFrameId == f.Id))
            throw new InvalidOperationException("Fixed distance requires its exact zero-cost actual Play activation.");
        var ending = _contentRegistry.GetSkill(f.SkillId).Program!.Triggers.Single(t =>
            t.Effects is [{ Op: SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt }]);
        AdvanceEventRulesAndQueueFact(new FixedDistanceOneTurnGrantedEvent(new(f.Id, f.InstructionIndex,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            _turnNumber, _currentSeat, target, ending.Id)));
    }
    private RuleQueryEvaluation ApplyFixedDistanceOne(CharacterState source, CharacterState target, RuleQueryEvaluation original)
    {
        if (source.Seat == target.Seat || !_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy) ||
            EventsSinceLastBoundary(e => e is TurnStartedEvent).OfType<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == _currentSeat)) return original;
        var grants = FixedDistanceOneGrants().Where(g => g.ActualTurnNumber == _turnNumber && g.ActualTurnOwnerSeat == _currentSeat &&
            g.Source.OwnerSeat == source.Seat && g.TargetSeat == target.Seat && ValidFixedDistanceOneGrant(g)).ToArray();
        if (grants.Length == 0) return original;
        return RuleQueryService.Evaluate(SkillRuleQuery.OutgoingDistance, new(1, int.MaxValue),
            [new RuleQueryBaseTerm("distance:before-fixed-one", ConvertRuleValue(original))],
            grants.Select(g => (RuleQueryContribution)new FiniteRuleQueryContribution($"fixed-distance-one:{g.ProgramFrameId}", SkillRuleOperation.Set, 1)).ToArray());
    }
    private void ConsumeFixedDistanceDebt(FixedDistanceOneTurnGrant g, bool required)
    { if (!FixedDistanceDebtConsumed(g)) AdvanceEventRulesAndQueueFact(new FixedDistanceOneDebtConsumedEvent(g, required)); }
    private void ExpireFixedDistanceOneGrants(int turn, int seat)
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy)) return;
        foreach (var g in FixedDistanceOneGrants().Where(g => g.ActualTurnNumber == turn && g.ActualTurnOwnerSeat == seat && !FixedDistanceDebtConsumed(g)).ToArray())
            ConsumeFixedDistanceDebt(g, false);
    }
    private bool FixedDistanceTargetDamagedThisTurn(FixedDistanceOneTurnGrant g) => EventsSinceLastBoundary(e => e is TurnStartedEvent)
        .OfType<DamageAppliedEvent>().Any(e => e.Amount > 0 && !e.SourceLess && e.SourceSeat == g.Source.OwnerSeat && e.TargetSeat == g.TargetSeat);
    private void AppendFixedDistanceOneEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy)) return;
        foreach (var g in FixedDistanceOneGrants().Where(g => g.ActualTurnNumber == _turnNumber && g.ActualTurnOwnerSeat == _currentSeat && !FixedDistanceDebtConsumed(g)).ToArray())
        {
            // Freeze the entire current real turn's applied damage at entry to Ending, including damage before issuance.
            if (!ValidFixedDistanceOneGrant(g) || _winner != Winner.None || !_players[g.Source.OwnerSeat].IsAlive || FixedDistanceTargetDamagedThisTurn(g))
            { ConsumeFixedDistanceDebt(g, false); continue; }
            var trigger = _contentRegistry.GetSkill(g.Source.SkillId).Program!.Triggers.Single(t => t.Id == g.EndingBindingId);
            var candidate = new ProgramTriggerCandidate(g.Source.OwnerSeat, g.Source.SkillId, trigger.Id, g.Source.SkillInstanceId,
                g.GameplayHash, trigger.Priority, items.Count);
            items.Add(new(TurnEndingBoundaryItemKind.Program, candidate.Priority, $"fixed-distance-debt:{g.ProgramFrameId}", candidate,
                CaptureProgramTriggerFacts(_players[g.Source.OwnerSeat])) { FixedDistanceDebt = g });
        }
    }
    private bool MatchesFixedDistanceDebt(ProgramTriggerCandidate c, ProgramSkillWindowContext context, bool begun)
    {
        if (context.FixedDistanceDebt is not { } g || !ValidFixedDistanceOneGrant(g) || context.Window != SkillProgramTriggerWindow.TurnEnding ||
            context.SourceSeat != _currentSeat || context.TargetSeat != _currentSeat || g.ActualTurnNumber != _turnNumber || g.ActualTurnOwnerSeat != _currentSeat ||
            c.OwnerSeat != g.Source.OwnerSeat || c.SkillId != g.Source.SkillId || c.SkillInstanceId != g.Source.SkillInstanceId || c.BindingId != g.EndingBindingId ||
            c.GameplayHash != g.GameplayHash || FixedDistanceDebtConsumed(g) != begun ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } boundary ||
            boundary.OwnerSeat != _currentSeat || boundary.TurnNumber != _turnNumber || boundary.ItemIndex < 0 || boundary.ItemIndex >= boundary.Items.Count ||
            boundary.Items[boundary.ItemIndex].FixedDistanceDebt != g || boundary.Items[boundary.ItemIndex].Candidate is not { } original ||
            original != c || context.OccurrenceIndex != c.OccurrenceIndex) return false;
        return !begun || CompleteProgramEventHistory().OfType<FixedDistanceOneDebtConsumedEvent>().Count(e => e.Grant == g && e.RequiredDiscard) == 1;
    }
    private bool MatchesFixedDistanceDebt(ProgramSkillFrame f) => f.WindowContext is { } context &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } boundary &&
        boundary.ItemIndex >= 0 && boundary.ItemIndex < boundary.Items.Count && boundary.Items[boundary.ItemIndex].Candidate is { } c &&
        MountObserverCandidateMatches(f, c) && MatchesFixedDistanceDebt(c, context, true);
    private bool HasIssuedFixedDistanceDebtCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => MatchesFixedDistanceDebt(c, context, false);
    private bool CanContinueIssuedFixedDistanceEnding(ProgramSkillFrame f) => MatchesFixedDistanceDebt(f);
    private void ConsumeFixedDistanceDebtAtBegin(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.FixedDistanceDebt is not { } g) return;
        if (!MatchesFixedDistanceDebt(c, context, false)) throw new InvalidOperationException("The exact issued distance debt changed before beginning.");
        ConsumeFixedDistanceDebt(g, true);
    }
    private void ConsumeSkippedFixedDistanceDebt(TurnEndingBoundaryFrame f)
    { if (f.Items[f.ItemIndex].FixedDistanceDebt is { } g) ConsumeFixedDistanceDebt(g, false); }
    private IReadOnlyList<PromptChoice> FixedDistanceDebtChoices(ProgramSkillFrame f) => Array.AsReadOnly(
        BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => FreezeDirectedDistanceChoice(c with { Parameters = new Dictionary<string,string>(c.Parameters) { ["program-action"] = "fixed-distance-ending-debt" } })).ToArray());
    private static PromptChoice FreezeDirectedDistanceChoice(PromptChoice c) => c with
    { Cards = Array.AsReadOnly(c.Cards.ToArray()), Targets = Array.AsReadOnly(c.Targets.ToArray()),
      Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(c.Parameters)) };
    private SkillProgramStepOutcome SettleFixedDistanceOneEndingDebt(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MatchesFixedDistanceDebt(f) || f.InstructionIndex != 1) throw new InvalidOperationException("Distance debt lost its exact issued Ending item.");
        if (f.FixedDistanceDebtPayment is null)
            ReplaceRuntimeTop(f = f with { FixedDistanceDebtPayment = new(f.WindowContext!.FixedDistanceDebt!, f.InstructionIndex) });
        if (f.FixedDistanceDebtPayment.CardId is not null) throw new InvalidOperationException("An issued physical discard cannot be requested twice.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || FixedDistanceDebtChoices(f).Count == 0)
        { ReplaceRuntimeTop(f with { FixedDistanceDebtPayment = null }); return SkillProgramStepOutcome.Continue; }
        PublishFixedDistanceDebt(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishFixedDistanceDebt(ProgramSkillFrame f)
    {
        var choices = FixedDistanceDebtChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "奋迅：本回合未对原目标造成伤害，弃置一张牌。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveFixedDistanceDebtChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Distance debt has no owning program.");
        AssertFixedDistanceDebtPayment(f);
        if (f.FixedDistanceDebtPayment is not { CardId: null } r || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], FixedDistanceDebtChoices(f).Where(c => c.Id == choice.Id).ToArray()) ||
            choice.Cards is not [var id] ||
            _cardZones.GetLocation(id) is var from && (from.OwnerSeat is not { } owner || owner != f.OwnerSeat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException("The debt lost its exact owned physical selection.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
        { ReplaceRuntimeTop(f with { FixedDistanceDebtPayment = null }); AdvanceRuntimeProgram(f.Id); return; }
        ReplaceRuntimeTop(f = f with { FixedDistanceDebtPayment = r with { CardId = id, From = from, SequenceBefore = _movementSequence, SequenceAfter = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([id], CardLocation.DiscardPile, new(FixedDistanceDebtReason), (_, records) =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { FixedDistanceDebtPayment = current.FixedDistanceDebtPayment! with { SequenceAfter = records.Single().Sequence } });
        });
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { FixedDistanceDebtPayment = f.FixedDistanceDebtPayment! with { SequenceAfter = _movementSequence } });
        r = f.FixedDistanceDebtPayment!;
        AdvanceEventRulesAndQueueFact(new FixedDistanceOneDebtPaidEvent(f.Id, r.Grant, id, from, r.SequenceBefore, r.SequenceAfter));
        if (!DrainFixedDistanceDebtChildren(f)) ReturnFixedDistanceOneDebtMovement(f);
    }
    private bool ResumeFixedDistanceOneDebt(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FixedDistanceDebtPayment: not null } f || f.Id != id) return false;
        AssertFixedDistanceDebtPayment(f);
        if (f.PendingMovementContinuation is not null) { if (!DrainFixedDistanceDebtChildren(f)) ReturnFixedDistanceOneDebtMovement(f); return true; }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || FixedDistanceDebtChoices(f).Count == 0)
        { ClearPendingDecision(); ReplaceRuntimeTop(f with { FixedDistanceDebtPayment = null }); AdvanceRuntimeProgram(f.Id); return true; }
        if (_pendingDecision is null) PublishFixedDistanceDebt(f); return true;
    }
    private bool ReturnFixedDistanceOneDebtMovement(ProgramSkillFrame f)
    {
        if (f.FixedDistanceDebtPayment is null) return false;
        if (f.PendingMovementContinuation is not { } pending || !IsFixedDistanceDebtMovement(f,
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect, pending))
            throw new InvalidOperationException("The debt cannot consume another physical payment's return.");
        if (DrainFixedDistanceDebtChildren(f)) return true;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null, FixedDistanceDebtPayment = null }); AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool ValidFixedDistanceDebtPayment(ProgramSkillFrame f)
    {
        if (f.FixedDistanceDebtPayment is not { } r || !MatchesFixedDistanceDebt(f) || r.Grant != f.WindowContext!.FixedDistanceDebt ||
            r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1) return false;
        if (r.CardId is null) return r.From is null && r.SequenceBefore == 0 && r.SequenceAfter == 0 && f.PendingMovementContinuation is null &&
            (_resolutionStack.LastOrDefault()?.Id != f.Id || _pendingDecision is null ||
                _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && p.PlayerSeat == f.OwnerSeat &&
                p.TargetSeat == f.OwnerSeat && p.SkillPrompt?.SkillId == f.SkillId && AssistedChoicesEqual(p.Choices, FixedDistanceDebtChoices(f)));
        if (r.From is not { OwnerSeat: var owner } from || owner != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != f.OwnerSeat) return false;
        var paid = CompleteProgramEventHistory().OfType<FixedDistanceOneDebtPaidEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        return paid is [var fact] && fact.Grant == r.Grant && fact.CardId == r.CardId && fact.From == from &&
            fact.SequenceBefore == r.SequenceBefore && fact.SequenceAfter == r.SequenceAfter &&
            _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.CardId == r.CardId && m.From == from &&
                (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && GetAttackCard(m.CardId).IsGeneralWeapon) && m.Reason.Value == FixedDistanceDebtReason) == 1;
    }
    private void AssertFixedDistanceDebtPayment(ProgramSkillFrame f)
    { if (f.FixedDistanceDebtPayment is not null && !ValidFixedDistanceDebtPayment(f)) throw new InvalidOperationException("An issued distance debt lost its exact source, item or paid interval."); }
    private bool IsFixedDistanceDebtMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        e?.Op == SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt && pending == f.PendingMovementContinuation && ValidFixedDistanceDebtPayment(f);
    private bool DrainFixedDistanceDebtChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void AssertFixedDistanceOnePolicies()
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy)) return;
        if (FixedDistanceOneGrants().Any(g => !ValidFixedDistanceOneGrant(g)) || CompleteProgramEventHistory().OfType<FixedDistanceOneDebtConsumedEvent>()
            .GroupBy(e => e.Grant.ProgramFrameId).Any(g => g.Count() != 1)) throw new InvalidOperationException("A fixed-distance issuance or Ending consumption duplicated its original identity.");
    }
    private PromptChoice SelectAiFixedDistanceDebt(PendingDecision d)
    {
        var snapshot = CreateSnapshot(d.PlayerSeat);
        // The exposed own-card choice is sufficient; no hidden zones or live actor state are scored.
        return d.Choices.OrderBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
}
