using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string CheZhouAction = "anniversary-che-zhou", CheZhouObtainReason = "skill-program.anniversary:pigua.obtain";
    [ProgramCapability] private sealed class AnniversaryCheZhouHost(GameEngine e) : IAnniversaryCheZhouHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginAnniversaryCheZhouPigua(f); }
    private static CardConversionSource CheZhouSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 46400)]
    private bool CanOfferAnniversaryCheZhou(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c) =>
        candidate.SkillId != AnniversaryCheZhouComposition.Pigua || c is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 1,
            SourceSeat: { } source, TargetSeat: { } target, DamageFrameId: { } damageId } && source == candidate.OwnerSeat && source != target &&
        IsValidPlayerSeat(target) && _players[target].IsAlive && _roundNumber > 0 && GetHand(_players[target]).Count > 0 &&
        _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } window &&
        window.ParentFrameId == damageId && window.SourceSeat == source && window.TargetSeat == target && window.TriggerWindow == c.Window &&
        _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is { } actual && actual.SourceSeat == source &&
        actual.TargetSeat == target && actual.Amount == c.Amount && DamageAcquisitionHasExactOwningAttack(actual, window);
    private bool ExactCheZhouDamage(ProgramSkillFrame f)
    {
        if (f.SkillId != AnniversaryCheZhouComposition.Pigua || f.TriggerId != AnniversaryCheZhouComposition.Binding || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 1, SourceSeat: { } source,
                TargetSeat: { } target, DamageFrameId: { } damageId } c || source != f.OwnerSeat || target == source || !IsValidPlayerSeat(target) ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } window ||
            window.ParentFrameId != damageId || window.SourceSeat != source || window.TargetSeat != target || window.TriggerWindow != c.Window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == damageId) is not { } actual || actual.SourceSeat != source ||
            actual.TargetSeat != target || actual.Amount != c.Amount || !DamageAcquisitionHasExactOwningAttack(actual, window)) return false;
        return ProgramEventHistory<DamageRequestedEvent>().Count(e => e.ResolutionId == damageId && !e.SourceLess && e.SourceSeat == source &&
            e.TargetSeat == target && e.Amount == c.Amount && e.Nature == actual.Nature) == 1;
    }
    private SkillProgramStepOutcome BeginAnniversaryCheZhouPigua(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryCheZhou is not null || !ExactCheZhouDamage(f) || f.WindowContext is not { TargetSeat: { } victim } c ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Pigua requires its original actual damage source, victim and greater-than-one damage.");
        var r = new AnniversaryCheZhouPiguaReceipt(CheZhouSource(f), f.GameplayHash, c.ParentFrameId, c.DamageFrameId!.Value,
            _turnNumber, _turnProgression.OwnerSeat, victim, _roundNumber, AnniversaryCheZhouStage.Selecting);
        ReplaceRuntimeTop(f = f with { AnniversaryCheZhou = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouPiguaStartedEvent(f.Id, r.Source, r.Hash, r.WindowId, r.DamageId,
            r.Turn, r.TurnSeat, r.Victim, r.Round));
        PublishCheZhou(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> CheZhouChoices(ProgramSkillFrame f)
    {
        if (f.AnniversaryCheZhou is not { Stage: AnniversaryCheZhouStage.Selecting } r || !_players[r.Victim].IsAlive) return [];
        var hand = GetHand(_players[r.Victim]);
        var choices = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.Victim, [CardZoneKind.Hand], OwnedCardMoveIntent.Obtain)
            .Where(c => !r.Selected.Contains(hand[int.Parse(c.Parameters["slot-index"], CultureInfo.InvariantCulture)].Id))
            .Select(c => new PromptChoice(new($"che-zhou.{f.Id}.pick-{r.Selected.Count}.slot-{c.Parameters["slot-index"]}"), c.Description, [], [],
                new Dictionary<string, string> { ["program-action"] = CheZhouAction, ["verb"] = "card", ["slot-index"] = c.Parameters["slot-index"],
                    ["selected-count"] = r.Selected.Count.ToString(CultureInfo.InvariantCulture) })).ToList();
        if (r.Selected.Count > 0)
            choices.Add(new(new($"che-zhou.{f.Id}.finish-{r.Selected.Count}"), $"获得已选的 {r.Selected.Count} 张手牌", [], [],
                new Dictionary<string, string> { ["program-action"] = CheZhouAction, ["verb"] = "finish", ["selected-count"] = r.Selected.Count.ToString(CultureInfo.InvariantCulture) }));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishCheZhou(ProgramSkillFrame f)
    {
        var r = f.AnniversaryCheZhou!;
        if (r.Selected.Count >= r.Round || CheZhouChoices(f).All(c => c.Parameters["verb"] == "finish"))
        { if (r.Selected.Count > 0) IssueCheZhouObtain(f); else FinishCheZhou(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var choices = CheZhouChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"披挂：选择至多 {r.Round} 张暗手牌（已选 {r.Selected.Count} 张）。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.Victim, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryCheZhouPigua)] private PromptChoice CheZhouAi(PendingDecision p) => p.Choices.First();
    [ProgramAnswer(CheZhouAction)] private void AnswerCheZhou(PromptChoice answer)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryCheZhou: { Stage: AnniversaryCheZhouStage.Selecting } r } f ||
            !ValidCheZhou(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([answer], CheZhouChoices(f).Where(c => c.Id == answer.Id).ToArray()))
            throw new InvalidOperationException("Pigua requires its published opaque Hand slot and unpaid original frame.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { FinishProgramSkill(f, false); return; }
        if (answer.Parameters["verb"] == "finish") { IssueCheZhouObtain(f); return; }
        var card = GetHand(_players[r.Victim])[int.Parse(answer.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(f = f with { AnniversaryCheZhou = r with { Selected = r.Selected.Append(card.Id).ToArray() } });
        AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouPiguaSelectedEvent(f.Id, f.AnniversaryCheZhou!.Selected.Count));
        PublishCheZhou(f);
    }
    private void IssueCheZhouObtain(ProgramSkillFrame f)
    {
        var r = f.AnniversaryCheZhou!;
        if (r.Stage != AnniversaryCheZhouStage.Selecting || r.Selected.Count is < 1 || r.Selected.Count > r.Round ||
            r.Selected.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(r.Victim)))
            throw new InvalidOperationException("Pigua cannot replace or pay its selected native Hand entities twice.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryCheZhou = r with { Stage = AnniversaryCheZhouStage.MovementChildren, Before = before, After = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(r.Selected, CardLocation.Hand(f.OwnerSeat), new(CheZhouObtainReason), (batch, moves) =>
        {
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { AnniversaryCheZhou = active.AnniversaryCheZhou! with { BatchId = batch, Moves = moves, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouPiguaPaidEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnSeat,
                r.Victim, moves.Count, batch, before, _movementSequence));
        }, disclosure: CardMovementDisclosure.FaceDown);
        ContinueCheZhou(f.Id); AdvanceRulesAndPublishState();
    }
    private void FinishCheZhou(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { AnniversaryCheZhou = f.AnniversaryCheZhou! with { Stage = AnniversaryCheZhouStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new AnniversaryCheZhouPiguaCompletedEvent(f.Id));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 46400)] private bool ContinueCheZhou(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryCheZhou: { Stage: not AnniversaryCheZhouStage.Complete } r } f || f.Id != id) return false;
        if (!ValidCheZhou(f)) throw new InvalidOperationException("Pigua lost its native damage, original Hand selection or paid movement receipt.");
        if (r.Stage == AnniversaryCheZhouStage.Selecting) { if (_pendingDecision is null) PublishCheZhou(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        FinishCheZhou(GetActiveProgramFrame(id)); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 46400)] private bool ReturnCheZhouMovement(ProgramSkillFrame f) => f.AnniversaryCheZhou is not null && ContinueCheZhou(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 46400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 46400)]
    private bool ContinuePaidCheZhou(ProgramSkillFrame f) => f.AnniversaryCheZhou is { Stage: AnniversaryCheZhouStage.MovementChildren } && ValidCheZhou(f);
}
