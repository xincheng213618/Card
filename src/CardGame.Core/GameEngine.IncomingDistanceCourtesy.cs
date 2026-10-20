using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class IncomingDistanceCourtesyCapability(GameEngine engine) : IIncomingDistanceCourtesyHost
    {
        public SkillProgramStepOutcome BeginIncomingDistanceCourtesy(ProgramSkillFrame f, IncomingDistanceCourtesyKind kind) =>
            engine.BeginIncomingDistanceCourtesy(f, kind);
    }
    private static string IncomingDistanceCourtesyReason(ProgramSkillFrame f, int step) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_IncomingDistanceCourtesy.IncomingDistanceCourtesyReason(f,step);
    private bool ExactIncomingDistanceEndingParent(ProgramSkillFrame f) =>
        f.TriggerId is not null && f.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } c &&
        c.OwnerSeat == f.OwnerSeat && c.SourceSeat == _currentSeat && c.TargetSeat == _currentSeat &&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == c.ParentFrameId) is { } p &&
        p.OwnerSeat == _currentSeat && p.TurnNumber == _turnNumber && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count &&
        p.Items[p.ItemIndex].Candidate is { } candidate && MountObserverCandidateMatches(f, candidate) &&
        c.OccurrenceIndex == candidate.OccurrenceIndex;
    private Card[] IncomingDistanceCosts(int owner) => GetHand(_players[owner]).Concat(GetEquipment(_players[owner]))
        .Where(card => !IsSelfHandCategoryDiscardForbidden(owner, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard) &&
            !IsForeignEquipmentDiscardPrevented(owner, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40)]
    private bool CanOfferIncomingDistanceCourtesy(ProgramTriggerCandidate candidate, SkillProgramTrigger t, ProgramSkillWindowContext c)
    {
        if (t.Effects is not [var effect] || !IncomingDistanceCourtesyContract.IsOperation(effect.Op)) return true;
        if (c.Window != SkillProgramTriggerWindow.TurnEnding || c.SourceSeat != _currentSeat || c.TargetSeat != _currentSeat ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || !_players[candidate.OwnerSeat].IsAlive || !_players[_currentSeat].IsAlive ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(p => p.Id == c.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != _currentSeat || parent.TurnNumber != _turnNumber) return false;
        return effect.Op == SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions
            ? candidate.OwnerSeat == _currentSeat && IncomingDistanceCosts(candidate.OwnerSeat).Length > 0 &&
                _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat && GetCombatDistance(p.Seat, candidate.OwnerSeat) == 1)
            : candidate.OwnerSeat != _currentSeat && !IsWithinAttackRange(_currentSeat, candidate.OwnerSeat);
    }
    private SkillProgramStepOutcome BeginIncomingDistanceCourtesy(ProgramSkillFrame supplied, IncomingDistanceCourtesyKind kind)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.IncomingDistanceCourtesy is not null || f.InstructionIndex != 1 || !ExactIncomingDistanceEndingParent(f) ||
            !CanOfferIncomingDistanceCourtesy(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), f.WindowContext!) ||
            GetPausedPrivateOfferEffect(f)?.Op != (kind == IncomingDistanceCourtesyKind.FactionDistance
                ? SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions : SkillProgramEffectOp.ChooseEndingCourtesyBenefits))
            throw new InvalidOperationException("Incoming distance requires its exact eligible actual Ending instruction.");
        var r = new IncomingDistanceCourtesyReceipt { WindowFrameId = f.WindowContext!.ParentFrameId,
            Source = CreateProgramTurnEffectSource(f), GameplayHash = f.GameplayHash, ActualTurnNumber = _turnNumber,
            ActualTurnOwnerSeat = _currentSeat, Kind = kind, Stage = IncomingDistanceCourtesyStage.Choosing };
        ReplaceRuntimeTop(f = f with { IncomingDistanceCourtesy = r });
        AdvanceEventRulesAndQueueFact(new IncomingDistanceCourtesyOfferedEvent(f.Id, r));
        PublishIncomingDistanceCourtesyChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> IncomingDistanceCourtesyChoices(ProgramSkillFrame f)
    {
        var r = f.IncomingDistanceCourtesy!;
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string key, string text, IReadOnlyList<int>? cards = null) => new(new($"incoming-distance.{f.Id}.{key}"), text,
            cards ?? [], [], new Dictionary<string, string> { ["program-action"] = "incoming-distance-courtesy", ["selection"] = key,
                ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        if (r.Kind == IncomingDistanceCourtesyKind.FactionDistance)
        {
            choices.AddRange(IncomingDistanceCosts(f.OwnerSeat).Select(card => Choice($"pay.{card.Id}", $"弃置【{card.DisplayName}】，增加其他角色计算与你的距离", [card.Id])));
            choices.Add(Choice("decline", "不执行"));
        }
        else
        {
            var available = new List<EndingCourtesyBenefit> { EndingCourtesyBenefit.OwnerDrawOne, EndingCourtesyBenefit.TurnOwnerDrawTwo };
            if (_players[r.ActualTurnOwnerSeat].Hp < _players[r.ActualTurnOwnerSeat].MaxHp) available.Add(EndingCourtesyBenefit.TurnOwnerRecoverOne);
            string Name(EndingCourtesyBenefit b) => b switch { EndingCourtesyBenefit.OwnerDrawOne => "你摸一张牌",
                EndingCourtesyBenefit.TurnOwnerDrawTwo => $"{_players[r.ActualTurnOwnerSeat].Name} 摸两张牌", _ => $"{_players[r.ActualTurnOwnerSeat].Name} 回复1点体力" };
            foreach (var first in available)
            {
                choices.Add(Choice(((int)first).ToString(CultureInfo.InvariantCulture), Name(first) + "；其他角色计算与你的距离-1"));
                foreach (var second in available.Where(b => b > first))
                    choices.Add(Choice($"{(int)first},{(int)second}", Name(first) + "，然后" + Name(second) + "；其他角色计算与你的距离-2"));
            }
        }
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishIncomingDistanceCourtesyChoice(ProgramSkillFrame f)
    {
        var choices = IncomingDistanceCourtesyChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse,
            IncomingDistanceCourtesy = f.IncomingDistanceCourtesy! with { Choices = choices } });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择。",
            Array.AsReadOnly(choices.SelectMany(c => c.Cards).Distinct().ToArray()), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = f.IncomingDistanceCourtesy!.Kind == IncomingDistanceCourtesyKind.FactionDistance,
            Choices = f.IncomingDistanceCourtesy.Choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("incoming-distance-courtesy")]
    private void ResolveIncomingDistanceCourtesyChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Incoming distance lost its owning program.");
        RequireIncomingDistanceCourtesyReceipt(f);
        var r = f.IncomingDistanceCourtesy!;
        if (r.Stage != IncomingDistanceCourtesyStage.Choosing || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], IncomingDistanceCourtesyChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Incoming distance selected a stale choice or an already paid instruction.");
        ClearPendingDecision();
        if (choice.Parameters["selection"] == "decline") { CompleteIncomingDistanceCourtesy(f); return; }
        r = r with { Stage = IncomingDistanceCourtesyStage.MovementChildren, Choices = [],
            Benefits = r.Kind == IncomingDistanceCourtesyKind.Courtesy ? choice.Parameters["selection"].Split(',')
                .Select(s => (EndingCourtesyBenefit)int.Parse(s, CultureInfo.InvariantCulture)).ToArray() : [] };
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, IncomingDistanceCourtesy = r });
        AdvanceEventRulesAndQueueFact(new IncomingDistanceCourtesySelectedEvent(f.Id, r));
        if (r.Kind == IncomingDistanceCourtesyKind.FactionDistance)
        {
            var card = GetAttackCard(choice.Cards.Single()); var from = _cardZones.GetLocation(card.Id);
            var to = NormalizeDiouCarrierDestination(card, from, card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile);
            var movement = new IncomingDistanceCourtesyMovement(0, f.OwnerSeat, 1, 1, _movementSequence, _movementSequence, card.Id, from, to);
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            var logicalDiscard = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, f.OwnerSeat,
                [card.Id], new(IncomingDistanceCourtesyReason(f, 0)), f.Id);
            MoveProgramCardsFromMultipleSources([card.Id], to, new(IncomingDistanceCourtesyReason(f, 0)), (_, moves) =>
                CommitIncomingDistanceCourtesyMovement(f.Id, movement with { After = moves.Max(m => m.Sequence) }));
            CompleteNativeLogicalDiscardInvocation(logicalDiscard);
            AdvanceRuntimeProgram(f.Id); return;
        }
        IssueNextEndingCourtesyBenefit(f);
    }
    private void CommitIncomingDistanceCourtesyMovement(long id, IncomingDistanceCourtesyMovement movement)
    {
        var f = GetActiveProgramFrame(id); var r = f.IncomingDistanceCourtesy!;
        ReplaceRuntimeTop(f with { IncomingDistanceCourtesy = r with { Movements = r.Movements.Append(movement).ToArray() } });
        AdvanceEventRulesAndQueueFact(new IncomingDistanceCourtesyMovementIssuedEvent(id, movement));
    }
    private void IssueNextEndingCourtesyBenefit(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.IncomingDistanceCourtesy!;
        if (r.NextBenefitIndex >= r.Benefits.Count || _winner != Winner.None) { CompleteIncomingDistanceCourtesy(f); return; }
        var b = r.Benefits[r.NextBenefitIndex]; var target = b == EndingCourtesyBenefit.OwnerDrawOne ? f.OwnerSeat : r.ActualTurnOwnerSeat;
        var requested = b == EndingCourtesyBenefit.TurnOwnerDrawTwo ? 2 : 1;
        var before = _movementSequence; var actual = 0;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(target, 0, null) });
        if (_players[target].IsAlive)
        {
            if (b == EndingCourtesyBenefit.TurnOwnerRecoverOne)
                new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, target, 1, null, null);
            else actual = DrawCards(_players[target], requested, true, new(IncomingDistanceCourtesyReason(f, r.NextBenefitIndex))).Count;
        }
        CommitIncomingDistanceCourtesyMovement(f.Id, new(r.NextBenefitIndex, target, requested, actual, before, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainIncomingDistanceCourtesyChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Movement, 21000)]
    private bool ReturnIncomingDistanceCourtesyMovement(ProgramSkillFrame f)
    { if (f.IncomingDistanceCourtesy is null) return false; AdvanceRuntimeProgram(f.Id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21000)]
    private bool ResumeIncomingDistanceCourtesy(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { IncomingDistanceCourtesy: { } r } f || f.Id != id) return false;
        RequireIncomingDistanceCourtesyReceipt(f);
        if (r.Stage == IncomingDistanceCourtesyStage.Choosing)
        { if (_pendingDecision is null) PublishIncomingDistanceCourtesyChoice(f); return true; }
        if (DrainIncomingDistanceCourtesyChildren(f)) return true;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        if (_winner != Winner.None) { CompleteIncomingDistanceCourtesy(f); return true; }
        var amount = r.Kind == IncomingDistanceCourtesyKind.FactionDistance ? GetLivingFactionCount() : -1;
        AdvanceEventRulesAndQueueFact(new IncomingDistanceChangedEvent(f.Id, r.NextBenefitIndex, r.Source, r.GameplayHash,
            r.ActualTurnNumber, r.ActualTurnOwnerSeat, amount));
        if (r.Kind == IncomingDistanceCourtesyKind.FactionDistance) { CompleteIncomingDistanceCourtesy(f); return true; }
        ReplaceRuntimeTop(f = f with { IncomingDistanceCourtesy = r with { NextBenefitIndex = r.NextBenefitIndex + 1 } });
        IssueNextEndingCourtesyBenefit(f); return true;
    }
    private void CompleteIncomingDistanceCourtesy(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new IncomingDistanceCourtesyCompletedEvent(f.Id));
        ReplaceRuntimeTop(f = f with { IncomingDistanceCourtesy = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramDistanceRule(ProgramDistanceStage.IncomingContributions, 10)]
    private IEnumerable<RuleQueryContribution> IncomingDistanceCourtesyContributions(int target) =>
        !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions) &&
        !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ChooseEndingCourtesyBenefits) ? [] :
            ProgramEventHistory<IncomingDistanceChangedEvent>().Where(e => e.Source.OwnerSeat == target)
                .Select(e => (RuleQueryContribution)new FiniteRuleQueryContribution($"incoming-distance:{e.ProgramFrameId}:{e.StepIndex}", SkillRuleOperation.Add, e.Amount));
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 420)]
    private bool IsIncomingDistanceCourtesyMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.IncomingDistanceCourtesy is { Stage: IncomingDistanceCourtesyStage.MovementChildren } r && e is not null &&
        IncomingDistanceCourtesyContract.IsOperation(e.Op) && r.Movements.LastOrDefault() is { } m &&
        p.SubjectSeat == m.SubjectSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 460)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 450)]
    private bool CanContinueIncomingDistanceCourtesy(ProgramSkillFrame f)
    {
        if (f.IncomingDistanceCourtesy is not { Stage: IncomingDistanceCourtesyStage.MovementChildren }) return false;
        RequireIncomingDistanceCourtesyReceipt(f); return true;
    }
    private void RequireIncomingDistanceCourtesyReceipt(ProgramSkillFrame f)
    {
        var r = f.IncomingDistanceCourtesy ?? throw new InvalidOperationException("Incoming distance lost its typed receipt.");
        if (!Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Stage) || !ExactIncomingDistanceEndingParent(f) || f.InstructionIndex != 1 || r.WindowFrameId != f.WindowContext!.ParentFrameId ||
            r.Source != CreateProgramTurnEffectSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            GetPausedPrivateOfferEffect(f)?.Op != (r.Kind == IncomingDistanceCourtesyKind.FactionDistance
                ? SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions : SkillProgramEffectOp.ChooseEndingCourtesyBenefits) ||
            (r.Kind == IncomingDistanceCourtesyKind.FactionDistance ? r.Source.OwnerSeat != _currentSeat : r.Source.OwnerSeat == _currentSeat) ||
            CompleteProgramEventHistory().OfType<IncomingDistanceCourtesyOfferedEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Receipt.WindowFrameId == r.WindowFrameId && e.Receipt.Source == r.Source && e.Receipt.GameplayHash == r.GameplayHash && e.Receipt.Kind == r.Kind) != 1)
            throw new InvalidOperationException("Incoming distance lost its actual Ending parent, source, kind or instruction.");
        var selected = CompleteProgramEventHistory().OfType<IncomingDistanceCourtesySelectedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.Stage == IncomingDistanceCourtesyStage.Choosing)
        {
            if (selected.Length != 0 || r.Movements.Count != 0 || r.Benefits.Count != 0 || r.NextBenefitIndex != 0 || f.PendingMovementContinuation is not null)
                throw new InvalidOperationException("Unpaid incoming distance contains an issued benefit or movement.");
            return;
        }
        if (selected is not [var s] || s.Receipt.Source != r.Source || s.Receipt.Kind != r.Kind || !s.Receipt.Benefits.SequenceEqual(r.Benefits) ||
            r.Benefits.Count != r.Benefits.Distinct().Count() || r.Benefits.Any(b => !Enum.IsDefined(b)) ||
            (r.Kind == IncomingDistanceCourtesyKind.FactionDistance ? r.Benefits.Count != 0 || r.NextBenefitIndex != 0 || r.Movements.Count != 1 :
                r.Benefits.Count is < 1 or > 2 || r.NextBenefitIndex < 0 || r.NextBenefitIndex >= r.Benefits.Count || r.Movements.Count != r.NextBenefitIndex + 1) ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != r.Movements[^1].SubjectSeat)
            throw new InvalidOperationException("Incoming distance lost its exact one-cost or distinct ordered benefit selection.");
        foreach (var m in r.Movements)
        {
            if (m.Before < 0 || m.After < m.Before || m.After > _movementSequence || m.Actual < 0 || m.Actual > m.Requested ||
                CompleteProgramEventHistory().OfType<IncomingDistanceCourtesyMovementIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Movement == m) != 1)
                throw new InvalidOperationException("Incoming distance movement lacks its unique producer.");
            var actual = _cardMovements.Where(move => move.Sequence > m.Before && move.Sequence <= m.After && move.Reason.Value == IncomingDistanceCourtesyReason(f, m.StepIndex)).ToArray();
            if (r.Kind == IncomingDistanceCourtesyKind.FactionDistance)
            {
                if (m.StepIndex != 0 || m.SubjectSeat != f.OwnerSeat || m.Requested != 1 || m.Actual != 1 || m.CardId is null ||
                    m.From is not { OwnerSeat: { } owner } from || owner != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                    actual is not [var move] || move.CardId != m.CardId || move.From != m.From || move.To != m.To)
                    throw new InvalidOperationException("Incoming distance cannot reissue or substitute its exact physical discard.");
            }
            else if (m.StepIndex < 0 || m.StepIndex >= r.Benefits.Count ||
                m.SubjectSeat != (r.Benefits[m.StepIndex] == EndingCourtesyBenefit.OwnerDrawOne ? f.OwnerSeat : r.ActualTurnOwnerSeat) ||
                m.Requested != (r.Benefits[m.StepIndex] == EndingCourtesyBenefit.TurnOwnerDrawTwo ? 2 : 1) || m.CardId is not null || m.From is not null || m.To is not null ||
                actual.Length != m.Actual || actual.Any(move => move.From != CardLocation.DrawPile || move.To != CardLocation.Hand(m.SubjectSeat)))
                throw new InvalidOperationException("Courtesy benefits lost their exact participant, count or private draw movement.");
        }
        var changes = CompleteProgramEventHistory().OfType<IncomingDistanceChangedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (changes.Length != r.NextBenefitIndex || changes.Where((e, i) => e.StepIndex != i || e.Source != r.Source || e.Amount != -1).Any())
            throw new InvalidOperationException("Courtesy returned to an already settled or missing distance step.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 480)]
    private void AssertRegisteredIncomingDistanceCourtesyReceipt(ProgramSkillFrame frame)
    {
        if (frame.IncomingDistanceCourtesy is not null) RequireIncomingDistanceCourtesyReceipt(frame);
    }
    [ProgramAiChoice(
        SkillProgramEffectOp.AdjustIncomingDistanceByLivingFactions,
        SkillProgramEffectOp.ChooseEndingCourtesyBenefits)]
    private PromptChoice SelectAiIncomingDistanceCourtesy(PendingDecision p, ProgramSkillFrame f) =>
        f.IncomingDistanceCourtesy!.Kind == IncomingDistanceCourtesyKind.FactionDistance
            ? p.Choices.Where(c => c.Cards.Count == 1).OrderBy(c => GetKeepValue(GetAttackCard(c.Cards[0]), _players[f.OwnerSeat])).FirstOrDefault() ?? p.Choices[^1]
            : p.Choices.First(c => c.Parameters["selection"] == "0");
}
