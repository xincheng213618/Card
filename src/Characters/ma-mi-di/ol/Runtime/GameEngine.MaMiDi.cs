using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MaMiDiDiscardReason = "skill-program.ma-mi-di.bingjie.discard";
    private bool HasMaMiDiCapability => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrowZhengdingMatchingResponse) == true;
    private static CardConversionSource MaMiDiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ma_mi_di_ol_MaMiDi.MaMiDiSource(f);
    [ProgramCapability] private sealed class MaMiDiCapability(GameEngine engine) : IMaMiDiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginMaMiDi(f, op); }
    private bool ExactMaMiDiPlay(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c) =>
        c.Window == SkillProgramTriggerWindow.PlayPhaseStarting && c.OwnerSeat == candidate.OwnerSeat && c.SourceSeat == candidate.OwnerSeat && c.TargetSeat == candidate.OwnerSeat && candidate.OwnerSeat == _currentSeat &&
        _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } w && w.OwnerSeat == candidate.OwnerSeat &&
        w.ItemIndex >= 0 && w.ItemIndex < w.Items.Count && w.Items[w.ItemIndex].Candidate == candidate;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27070)]
    private bool CanOfferMaMiDi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect] || !MaMiDiComposition.Handles(effect.Op)) return true;
        if (effect.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp) return ExactMaMiDiPlay(candidate, context) && _players[candidate.OwnerSeat].MaxHp > 0;
        if (effect.Op == SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards) return IsIssuedBingjieCandidate(candidate, context);
        return TryGetMaMiDiResponse(candidate, context, out _, out _);
    }
    private SkillProgramStepOutcome BeginMaMiDi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MaMiDi is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } c || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
            throw new InvalidOperationException("Ma Mi Di requires its unique current native instruction.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        if (!CanOfferMaMiDi(candidate, GetProgramTrigger(f), c)) throw new InvalidOperationException("Ma Mi Di lost its exact unpaid candidate or paid original-use entitlement.");
        MaMiDiReceipt r;
        if (op == SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards)
        {
            var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == c.ParentFrameId);
            var use = LifecycleCardUse(window.ParentFrameId)!; var issued = use.MaMiDiBingjie!;
            var targets = (window.Action.EffectiveKind == CardKind.BorrowedSword ? window.Action.TargetSeats.Where((_, i) => i % 2 == 0) : window.Action.EffectiveDesignatedTargetSeats)
                .Where(s => s != f.OwnerSeat && IsValidPlayerSeat(s)).Distinct().ToArray();
            r = new(MaMiDiSource(f), f.GameplayHash, op, 1, c.ParentFrameId, _turnNumber, _currentSeat, MaMiDiStage.TargetChoice, use.Id, issued.ActionId)
            { Grants = issued.Grants.Where(g => g.Source.SkillId == f.SkillId && g.Source.SkillInstanceId == f.SkillInstanceId).ToArray(), Targets = targets };
            // This is the actual designation trigger, after earlier native role/target adjustments.
            // Collection only reserved possible grants; it never entitled the previous actor.
            UpdateLifecycleCardUse(use.Id, u => u with { MaMiDiBingjie = issued with { Issued = true } });
            foreach (var grant in r.Grants) AdvanceEventRulesAndQueueFact(new BingjieUseBoundEvent(use.Id, issued.ActionId, f.OwnerSeat, grant.FrameId, r.Turn, r.TurnOwner));
        }
        else
        {
            MaMiDiResponseLinkedEvent? response = null;
            if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) throw new InvalidOperationException("An unpaid maximum-HP instruction requires its live original source.");
            if (op == SkillProgramEffectOp.GrowZhengdingMatchingResponse && !TryGetMaMiDiResponse(candidate, c, out _, out response)) throw new InvalidOperationException("Zhengding lost its frozen real response.");
            var owner = _players[f.OwnerSeat]; var after = op == SkillProgramEffectOp.PayBingjieTurnMaximumHp ? owner.MaxHp - 1 : checked(owner.MaxHp + 1);
            r = new(MaMiDiSource(f), f.GameplayHash, op, 1, c.ParentFrameId, _turnNumber, _currentSeat, MaMiDiStage.MaximumChildren,
                ActionId: response?.ResponseActionId, MaximumBefore: owner.MaxHp, MaximumAfter: after, HpBefore: owner.Hp, HpAfter: Math.Min(owner.Hp, after), Response: response);
        }
        ReplaceRuntimeTop(f = f with { MaMiDi = r });
        AdvanceEventRulesAndQueueFact(new MaMiDiStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.WindowId, r.Turn, r.TurnOwner, r.UseId, r.ActionId, r.Grants.Count, r.Targets.Count));
        for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new MaMiDiFrozenTargetEvent(f.Id, i, r.Targets[i]));
        for (var i = 0; i < r.Grants.Count; i++) AdvanceEventRulesAndQueueFact(new MaMiDiFrozenGrantEvent(f.Id, i, r.Grants[i].FrameId));
        if (r.Stage == MaMiDiStage.MaximumChildren)
        {
            if (op == SkillProgramEffectOp.PayBingjieTurnMaximumHp && r.MaximumAfter == 0)
            {
                // Same native zero-maximum-HP death protocol as Yisuan. The old
                // shared clamp remains untouched for every historical operation.
                var owner = _players[f.OwnerSeat]; owner.MaxHp = 0; owner.Hp = r.HpAfter;
                AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat, -1, 0, f.SkillId) { HpBeforeClamp = r.HpBefore });
            }
            else ChangeProgramMaximumHp(f, op == SkillProgramEffectOp.PayBingjieTurnMaximumHp ? -1 : 1);
            AdvanceEventRulesAndQueueFact(new MaMiDiMaximumIssuedEvent(f.Id, op, f.OwnerSeat, r.MaximumBefore, r.MaximumAfter, r.HpBefore, r.HpAfter));
            if (op == SkillProgramEffectOp.PayBingjieTurnMaximumHp && r.MaximumAfter == 0)
            { BeginPlayerDeath(f.Id, _players[f.OwnerSeat], null, null, null, causingProgramSkillFrameId: f.Id); return SkillProgramStepOutcome.AwaitChild; }
        }
        ContinueMaMiDi(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private (BingjieGrantedEvent Grant, int Target) MaMiDiCurrentTarget(MaMiDiReceipt r) => (r.Grants[r.Cursor / r.Targets.Count], r.Targets[r.Cursor % r.Targets.Count]);
    private IReadOnlyList<PromptChoice> MaMiDiChoices(ProgramSkillFrame f)
    {
        var r = f.MaMiDi!;
        if (r.Stage != MaMiDiStage.TargetChoice || r.Targets.Count == 0 || r.Cursor >= r.Grants.Count * r.Targets.Count || _winner != Winner.None) return [];
        var target = MaMiDiCurrentTarget(r).Target; if (!_players[target].IsAlive) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, target, target, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => PromptChoiceProjection.Freeze(c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "ma-mi-di", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "discard", ["cursor"] = r.Cursor.ToString(CultureInfo.InvariantCulture) } })).ToArray());
    }
    private void PublishMaMiDi(ProgramSkillFrame f, IReadOnlyList<PromptChoice> choices)
    {
        var target = MaMiDiCurrentTarget(f.MaMiDi!).Target; var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, target, "秉节：由你弃置一张手牌或装备牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = target, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[target].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ma-mi-di")]
    private void AnswerMaMiDi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MaMiDi: { Stage: MaMiDiStage.TargetChoice } r } f ||
            _pendingDecision?.PlayerSeat != MaMiDiCurrentTarget(r).Target || !MaMiDiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Bingjie requires the actual target's current private physical discard.");
        AssertMaMiDi(f); ClearPendingDecision(); var pair = MaMiDiCurrentTarget(r); var id = choice.Cards.Single(); var from = _cardZones.GetLocation(id); var card = GetAdvancedCard(id);
        var grain = from == CardLocation.Equipment(pair.Target) && card.Kind == CardKind.WoodenOx ? GetWoodenOxGrain(_players[pair.Target]).Select(c => c.Id).ToArray() : [];
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MaMiDi = r with { Stage = MaMiDiStage.TargetChildren }, PendingMovementContinuation = new(pair.Target, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, pair.Target, card, from, new(MaMiDiDiscardReason));
        var after = _movementSequence; var moved = _cardMovements.Single(m => m.Sequence > before && m.Sequence <= after && m.CardId == id && m.Reason.Value == MaMiDiDiscardReason);
        var step = new MaMiDiDiscardStep(r.Cursor, pair.Grant.FrameId, pair.Target, id, card.Kind, from, moved.To, before, after) { WoodenOxGrain = grain };
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MaMiDi = f.MaMiDi! with { Steps = r.Steps.Append(step).ToArray() } });
        AdvanceEventRulesAndQueueFact(new MaMiDiDiscardIssuedEvent(f.Id, step.Index, step.GrantFrameId, step.TargetSeat, step.CardId, step.Kind, step.From, step.To, step.Before, step.After)); ContinueMaMiDi(f.Id);
    }
    private bool DrainMaMiDi(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMaMiDi(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { MaMiDi: { } r } f && f.Id == id)
        {
            AssertMaMiDi(f);
            if (r.Stage != MaMiDiStage.TargetChoice)
            {
                if (DrainMaMiDi(f)) return;
                f = GetActiveProgramFrame(id); r = f.MaMiDi!;
                if (r.Stage == MaMiDiStage.MaximumChildren)
                {
                    if (r.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
                        AdvanceEventRulesAndQueueFact(new BingjieGrantedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.TurnOwner, r.WindowId));
                    FinishMaMiDi(f); return;
                }
                ReplaceRuntimeTop(f = f with { MaMiDi = r with { Stage = MaMiDiStage.TargetChoice, Cursor = r.Cursor + 1 }, PendingMovementContinuation = null }); r = f.MaMiDi!;
            }
            if (_winner != Winner.None || r.Cursor >= r.Targets.Count * r.Grants.Count) { FinishMaMiDi(f); return; }
            if (_pendingDecision is not null) return; var choices = MaMiDiChoices(f);
            if (choices.Count > 0) { PublishMaMiDi(f, choices); return; }
            var pair = MaMiDiCurrentTarget(r); AdvanceEventRulesAndQueueFact(new MaMiDiTargetSkippedEvent(f.Id, r.Cursor, pair.Grant.FrameId, pair.Target));
            ReplaceRuntimeTop(f with { MaMiDi = r with { Cursor = r.Cursor + 1 } });
        }
    }
    private void FinishMaMiDi(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new MaMiDiCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { MaMiDi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27070)] private bool ResumeMaMiDi(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MaMiDi: not null } f || f.Id != id) return false; ContinueMaMiDi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27070)] private bool ReturnMaMiDiMovement(ProgramSkillFrame f)
    { if (f.MaMiDi is null) return false; ContinueMaMiDi(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27070)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27070)]
    private bool ContinuePaidMaMiDi(ProgramSkillFrame f)
    {
        if (f.MaMiDi is not null) { AssertMaMiDi(f); return true; }
        // The already-paid turn entitlement is also the authority for entering
        // this new frame after source loss, before its first instruction owns
        // the per-target payment receipt. Unpaid maximum-HP entries never use it.
        return f.InstructionIndex is 0 or 1 && f.TriggerId == "issued-turn-target-discards" && f.WindowContext is { } c &&
            GetProgramTrigger(f).Effects is [{ Op: SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards }] &&
            IsIssuedBingjieCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards)]
    private PromptChoice AiMaMiDi(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderBy(c => c.Cards is [var id] ? GetKeepValue(GetAdvancedCard(id), _players[p.PlayerSeat]) : 0).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
}
