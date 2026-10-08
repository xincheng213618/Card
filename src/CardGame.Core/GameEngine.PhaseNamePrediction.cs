using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private readonly List<PhaseNamePredictionPolicy> _phaseNamePredictions = [];
    private bool? _tracksPhaseNamePredictions;
    private bool TracksPhaseNamePredictions => _tracksPhaseNamePredictions ??=
        _contentRegistry.Skills.Values.Any(s => s.Program?.Triggers.Any(t =>
            t.Effects.Any(e => e.Op == SkillProgramEffectOp.BeginPhaseNamePrediction)) == true);
    // An inserted phase advances the global serial. Its suspended ending parent
    // retains the original identity; never rewind the serial and reuse an ID.
    private int CurrentPredictionPhaseInstanceId => _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
        .LastOrDefault(f => f.Window == SkillProgramTriggerWindow.PlayEnding && f.OwnerSeat == _currentSeat &&
            f.PhaseNamePredictionPhase is { } key && key.ActualTurnNumber == _turnNumber && key.TurnOwnerSeat == _currentSeat)
        ?.PhaseNamePredictionPhase?.PhaseInstanceId ?? _cardUseDebitPhaseInstanceId;
    private const string PhasePredictionCostReason = "program.phase-name-prediction.cost";
    private const string PhasePredictionClaimReason = "program.phase-name-prediction.claim";
    private static CardKind PredictionName(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
    private bool PredictionConsumed(PhaseNamePredictionPolicy p) => CompleteProgramEventHistory().Any(e =>
        e is PhaseNamePredictionSettledEvent s && s.OriginalFrameId == p.Origin.FrameId ||
        e is PhaseNamePredictionExpiredEvent x && x.OriginalFrameId == p.Origin.FrameId);
    private static bool IsPhaseNamePredictionResolver(ProgramTriggerCandidate c, SkillProgram p) =>
        p.Triggers.Single(t => t.Id == c.BindingId).Effects is [{ Op: SkillProgramEffectOp.SettlePhaseNamePrediction }];
    private PhaseNamePredictionMaterial[] PredictionMaterials(long frameId, int seat) =>
        BuildOwnedCardPaymentChoices(frameId, seat, seat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Where(c => c.Cards.Count == 1).Select(c => c.Cards[0]).Distinct()
            .Select(id => new PhaseNamePredictionMaterial(id, _cardZones.GetLocation(id))).ToArray();
    private SkillProgramStepOutcome BeginPhaseNamePrediction(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.PhaseNamePrediction is not null ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting, SourceSeat: { } target } c ||
            target != _currentSeat || target == f.OwnerSeat || _phase != TurnPhase.Play ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not PlayPhaseStartingBoundaryFrame b || b.Id != c.ParentFrameId ||
            b.ItemIndex < 0 || b.ItemIndex >= b.Items.Count || b.Items[b.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate))
            throw new InvalidOperationException("Phase prediction lost its exact foreign Play-start candidate.");
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var count = CompleteProgramEventHistory().OfType<PhaseNamePredictionStartedEvent>().Count(e =>
            e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId && e.StateId == stateId && e.ActualRoundNumber == _roundNumber);
        var origin = new PhaseNamePredictionStartedEvent(f.Id, source, f.GameplayHash, stateId,
            _roundNumber, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId, target, count);
        var materials = PredictionMaterials(f.Id, f.OwnerSeat);
        ReplaceRuntimeTop(f = f with { PhaseNamePrediction = new() { Origin = origin,
            Stage = PhaseNamePredictionStage.ChoosingCost, RequiredCount = Math.Min(count, materials.Length), Eligible = materials } });
        AdvanceEventRulesAndQueueFact(origin);
        if (f.PhaseNamePrediction.RequiredCount == 0) ContinuePredictionAfterCost(f);
        else PublishPredictionChoice(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> PredictionChoices(ProgramSkillFrame f)
    {
        var r = f.PhaseNamePrediction!;
        if (r.Stage == PhaseNamePredictionStage.ChoosingCost)
            return Array.AsReadOnly(r.Eligible.Where(m => !r.Selected.Contains(m.CardId)).Select(m =>
                FreezeDirectedDistanceChoice(new(new($"phase-prediction.{f.Id}.cost.{m.CardId}"),
                    $"弃置 {GetAttackCard(m.CardId).DisplayName}", [m.CardId], [], new Dictionary<string,string>
                    { ["program-action"]="phase-name-cost", ["card-id"]=m.CardId.ToString(CultureInfo.InvariantCulture) }))).ToArray());
        if (r.Stage == PhaseNamePredictionStage.Revealing)
            return Array.AsReadOnly(r.TargetHand.Select((id, slot) => FreezeDirectedDistanceChoice(new(
                new($"phase-prediction.{f.Id}.slot.{slot}"), $"展示第 {slot + 1} 张手牌", [], [], new Dictionary<string,string>
                { ["program-action"]="phase-name-reveal", ["slot-index"]=slot.ToString(CultureInfo.InvariantCulture) }))).ToArray());
        if (r.Stage == PhaseNamePredictionStage.Guessing)
            return Array.AsReadOnly(new[] { true, false }.Select(guess => FreezeDirectedDistanceChoice(new(
                new($"phase-prediction.{f.Id}.guess.{(guess ? "yes" : "no")}"), guess ? "会使用同名牌" : "不会使用同名牌", [], [],
                new Dictionary<string,string> { ["program-action"]="phase-name-guess", ["guess"]=guess ? "yes" : "no" }))).ToArray());
        return [];
    }
    private void PublishPredictionChoice(ProgramSkillFrame f)
    {
        var r = f.PhaseNamePrediction!; var choices = PredictionChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) throw new InvalidOperationException("An empty prediction choice cannot be published.");
        var text = r.Stage switch { PhaseNamePredictionStage.ChoosingCost => $"弃置 {r.RequiredCount} 张牌（已选 {r.Selected.Count}）。",
            PhaseNamePredictionStage.Revealing => "选择并展示对方的一张手牌。", _ => "秘密猜测其此阶段是否使用同名牌。" };
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, text, choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
          SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolvePhaseNamePredictionChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Prediction has no owning frame.");
        AssertPhaseNamePrediction(f);
        if (f.PhaseNamePrediction is not { } r || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], PredictionChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Prediction choice changed its exact private draft.");
        ClearPendingDecision();
        if (r.Stage == PhaseNamePredictionStage.ChoosingCost)
        {
            var id = choice.Cards.Single(); var selected = r.Selected.Append(id).ToArray();
            ReplaceRuntimeTop(f = f with { PhaseNamePrediction = r = r with { Selected = selected } });
            if (selected.Length < r.RequiredCount) { PublishPredictionChoice(f); return; }
            if (r.Eligible.Where(m => selected.Contains(m.CardId)).Any(m => _cardZones.GetLocation(m.CardId) != m.From))
                throw new InvalidOperationException("A prediction cost moved before its one physical batch.");
            ReplaceRuntimeTop(f = f with { PhaseNamePrediction = r with { Stage = PhaseNamePredictionStage.CostChildren,
                SequenceBefore = _movementSequence, SequenceAfter = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramCardsFromMultipleSources(selected, CardLocation.DiscardPile, new(PhasePredictionCostReason), (_, records) =>
            { var active = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(active with { PhaseNamePrediction = active.PhaseNamePrediction! with { SequenceAfter = _movementSequence } }); });
            f = GetActiveProgramFrame(f.Id); r = f.PhaseNamePrediction!;
            AdvanceEventRulesAndQueueFact(new PhaseNamePredictionCostPaidEvent(f.Id, r.Origin.RequiredDiscardCount, selected.Length, r.SequenceBefore, r.SequenceAfter));
            if (!DrainPredictionChildren(f)) ReturnPhaseNamePredictionMovement(f); return;
        }
        if (r.Stage == PhaseNamePredictionStage.Revealing)
        {
            var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture); var id = r.TargetHand[slot];
            if (_cardZones.GetLocation(id) != CardLocation.Hand(r.Origin.TargetSeat))
                throw new InvalidOperationException("The blind reveal lost its frozen target-hand entity.");
            ReplaceRuntimeTop(f = f with { PhaseNamePrediction = r with { Stage = PhaseNamePredictionStage.Guessing, RevealedCardId = id } });
            AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, Array.AsReadOnly(new[] { ToSnapshot(GetAttackCard(id)) })));
            PublishPredictionChoice(f); return;
        }
        if (r.Stage != PhaseNamePredictionStage.Guessing || r.RevealedCardId is not { } shown)
            throw new InvalidOperationException("Prediction has no exact shown entity.");
        var ending = _contentRegistry.GetSkill(f.SkillId).Program!.Triggers.Single(t => t.Effects is
            [{ Op: SkillProgramEffectOp.SettlePhaseNamePrediction, StateId: var state }] && state == r.Origin.StateId);
        var policy = new PhaseNamePredictionPolicy(r.Origin, shown, PredictionName(GetAttackCard(shown).Kind), choice.Parameters["guess"] == "yes", ending.Id);
        _phaseNamePredictions.Add(policy);
        AdvanceEventRulesAndQueueFact(new PhaseNamePredictionArmedEvent(f.Id, shown, PredictionName(GetAttackCard(shown).Kind)));
        foreach (var use in CompleteProgramEventHistory().OfType<PhaseNamePredictionActualUseEvent>().ToArray())
            ObservePredictionUse(policy, use);
        ReplaceRuntimeTop(f with { PhaseNamePrediction = null }); AdvanceRuntimeProgram(f.Id);
    }
    private void ContinuePredictionAfterCost(ProgramSkillFrame f)
    {
        var r = f.PhaseNamePrediction!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.Origin.TargetSeat].IsAlive ||
            _cardZones.CardsAt(CardLocation.Hand(r.Origin.TargetSeat)).Count == 0)
        { ReplaceRuntimeTop(f with { PhaseNamePrediction = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); return; }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, PhaseNamePrediction = r with
        { Stage = PhaseNamePredictionStage.Revealing, TargetHand = _cardZones.CardsAt(CardLocation.Hand(r.Origin.TargetSeat)).Select(c => c.Id).ToArray() } });
        PublishPredictionChoice(f);
    }
    private void ObservePhaseNamePredictionUse(CardActionContext action, bool qualifiedActualUse = false)
    {
        if (action.Type != CardActionType.Use && !qualifiedActualUse) return;
        RecordPredictionActualUse(action.ActionId, null, action.ActorSeat, action.EffectiveKind);
    }
    private void RecordPredictionNativeCardUse(long frameId, int actorSeat, CardKind kind) =>
        RecordPredictionActualUse(null, frameId, actorSeat, kind);
    private void RecordPredictionActualUse(long? actionId, long? nativeFrameId, int actorSeat, CardKind kind)
    {
        if (!TracksPhaseNamePredictions || _phase != TurnPhase.Play || actorSeat != _currentSeat) return;
        var use = new PhaseNamePredictionActualUseEvent(actionId, nativeFrameId, actorSeat,
            _turnNumber, CurrentPredictionPhaseInstanceId, PredictionName(kind));
        if (CompleteProgramEventHistory().OfType<PhaseNamePredictionActualUseEvent>().Any(e => e == use)) return;
        AdvanceEventRulesAndQueueFact(use);
        foreach (var p in _phaseNamePredictions.Where(p => !PredictionConsumed(p) && p.Origin.ActualTurnNumber == _turnNumber &&
            p.Origin.PhaseInstanceId == use.PhaseInstanceId && p.Origin.TargetSeat == actorSeat).ToArray())
            ObservePredictionUse(p, use);
    }
    private void ObservePredictionUse(PhaseNamePredictionPolicy p, PhaseNamePredictionActualUseEvent use)
    {
        if (use.ActualTurnNumber != p.Origin.ActualTurnNumber || use.PhaseInstanceId != p.Origin.PhaseInstanceId ||
            use.ActorSeat != p.Origin.TargetSeat || use.NameKind != p.NameKind || PredictionConsumed(p) ||
            CompleteProgramEventHistory().OfType<PhaseNamePredictionUseObservedEvent>().Any(e =>
                e.OriginalFrameId == p.Origin.FrameId && e.ActionId == use.ActionId && e.NativeCardUseFrameId == use.NativeCardUseFrameId)) return;
        AdvanceEventRulesAndQueueFact(new PhaseNamePredictionUseObservedEvent(p.Origin.FrameId, use.ActionId, use.ActorSeat,
            use.ActualTurnNumber, use.PhaseInstanceId, use.NameKind, use.NativeCardUseFrameId));
    }
    private void AppendPhaseNamePredictionCandidates(List<ProgramTriggerCandidate> candidates)
    {
        foreach (var p in _phaseNamePredictions.Where(p => !PredictionConsumed(p) && p.Origin.ActualTurnNumber == _turnNumber &&
            p.Origin.ActualTurnOwnerSeat == _currentSeat && p.Origin.PhaseInstanceId == _cardUseDebitPhaseInstanceId).ToArray())
        {
            if (_winner != Winner.None || !_players[p.Origin.Source.OwnerSeat].IsAlive || !_players[p.Origin.TargetSeat].IsAlive)
            { AdvanceEventRulesAndQueueFact(new PhaseNamePredictionExpiredEvent(p.Origin.FrameId)); continue; }
            var t = _contentRegistry.GetSkill(p.Origin.Source.SkillId).Program!.Triggers.Single(t => t.Id == p.EndingBindingId);
            candidates.Add(new(p.Origin.Source.OwnerSeat, p.Origin.Source.SkillId, t.Id, p.Origin.Source.SkillInstanceId,
                p.Origin.GameplayHash, t.Priority, checked((int)p.Origin.FrameId)));
        }
    }
    private ProgramSkillWindowContext AttachPhaseNamePredictionContext(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.PlayEnding) return context;
        var p = _phaseNamePredictions.SingleOrDefault(p => p.Origin.FrameId == c.OccurrenceIndex && p.EndingBindingId == c.BindingId &&
            p.Origin.Source.SkillId == c.SkillId && p.Origin.Source.SkillInstanceId == c.SkillInstanceId && p.Origin.Source.OwnerSeat == c.OwnerSeat);
        return p is null ? context : context with { PhaseNamePrediction = p };
    }
    private bool HasIssuedPhaseNamePrediction(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.PhaseNamePrediction is not { } p || !_phaseNamePredictions.Contains(p) || PredictionConsumed(p) ||
            context.Window != SkillProgramTriggerWindow.PlayEnding || _phase != TurnPhase.Play ||
            p.Origin.ActualTurnNumber != _turnNumber || p.Origin.ActualTurnOwnerSeat != _currentSeat ||
            p.Origin.PhaseInstanceId != CurrentPredictionPhaseInstanceId || p.Origin.FrameId != c.OccurrenceIndex ||
            p.Origin.Source.OwnerSeat != c.OwnerSeat || p.Origin.Source.SkillId != c.SkillId || p.Origin.Source.SkillInstanceId != c.SkillInstanceId ||
            p.EndingBindingId != c.BindingId || p.Origin.GameplayHash != c.GameplayHash ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame b ||
            b.Window != context.Window || b.OwnerSeat != _currentSeat ||
            b.PhaseNamePredictionPhase != new PhaseNamePredictionPhaseKey(p.Origin.ActualTurnNumber, p.Origin.ActualTurnOwnerSeat, p.Origin.PhaseInstanceId) ||
            b.CandidateIndex < 0 || b.CandidateIndex >= b.Candidates.Count || b.Candidates[b.CandidateIndex] != c)
            return false;
        return true;
    }
    private SkillProgramStepOutcome SettlePhaseNamePrediction(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id); var c = f.WindowContext!;
        var candidate = ((ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(b => b.Id == c.ParentFrameId)).Candidates
            .Single(x => MountObserverCandidateMatches(f, x));
        if (f.InstructionIndex != 1 || !HasIssuedPhaseNamePrediction(candidate, c) || c.PhaseNamePrediction is not { } p || p.Origin.StateId != stateId)
            throw new InvalidOperationException("Prediction lost its exact issued phase-end item.");
        // Earlier frozen ending candidates may have killed the original target.
        // Retire this issued promise before creating any damage or claim receipt.
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[p.Origin.TargetSeat].IsAlive)
        {
            AdvanceEventRulesAndQueueFact(new PhaseNamePredictionExpiredEvent(p.Origin.FrameId));
            return SkillProgramStepOutcome.Continue;
        }
        var actual = CompleteProgramEventHistory().OfType<PhaseNamePredictionUseObservedEvent>().Any(e => e.OriginalFrameId == p.Origin.FrameId);
        var correct = p.Guess == actual;
        ReplaceRuntimeTop(f = f with { PhaseNamePrediction = new() { Origin = p.Origin, IssuedPolicy = p, Stage = PhaseNamePredictionStage.DamageIssued } });
        AdvanceEventRulesAndQueueFact(new PhaseNamePredictionSettledEvent(f.Id, p.Origin.FrameId, p.Guess, actual, correct, p.CardId, p.Origin.TargetSeat));
        if (correct) return BeginProgramSkillDamage(f, p.Origin.TargetSeat, 1);
        var from = _cardZones.GetLocation(p.CardId);
        if (from != CardLocation.DiscardPile && from != CardLocation.Processing &&
            from != CardLocation.Hand(p.Origin.TargetSeat) && from != CardLocation.Equipment(p.Origin.TargetSeat))
        { ReplaceRuntimeTop(f with { PhaseNamePrediction = null }); return SkillProgramStepOutcome.Continue; }
        ReplaceRuntimeTop(f = f with { PhaseNamePrediction = f.PhaseNamePrediction! with { Stage = PhaseNamePredictionStage.ClaimChildren,
            SequenceBefore = _movementSequence, SequenceAfter = _movementSequence,
            Selected = [p.CardId], Eligible = [new(p.CardId, from)] }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([p.CardId], CardLocation.Hand(f.OwnerSeat), new(PhasePredictionClaimReason), (_, records) =>
        { var active = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(active with { PhaseNamePrediction = active.PhaseNamePrediction! with { SequenceAfter = _movementSequence } }); });
        if (!DrainPredictionChildren(GetActiveProgramFrame(f.Id))) ReturnPhaseNamePredictionMovement(GetActiveProgramFrame(f.Id));
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainPredictionChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool ReturnPhaseNamePredictionMovement(ProgramSkillFrame f)
    {
        if (f.PhaseNamePrediction is not { Stage: PhaseNamePredictionStage.CostChildren or PhaseNamePredictionStage.ClaimChildren } r) return false;
        AssertPhaseNamePrediction(f);
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("Prediction lost its typed movement return.");
        if (DrainPredictionChildren(f)) return true;
        if (r.Stage == PhaseNamePredictionStage.CostChildren) ContinuePredictionAfterCost(f);
        else { ReplaceRuntimeTop(f with { PhaseNamePrediction = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
        return true;
    }
    private bool ResumePhaseNamePrediction(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PhaseNamePrediction: { } r } f || f.Id != id) return false;
        AssertPhaseNamePrediction(f);
        if (r.Stage is PhaseNamePredictionStage.CostChildren or PhaseNamePredictionStage.ClaimChildren) return ReturnPhaseNamePredictionMovement(f);
        if (r.Stage == PhaseNamePredictionStage.DamageIssued)
        {
            if (f.AttackAttempt is not null) return false;
            ReplaceRuntimeTop(f with { PhaseNamePrediction = null }); AdvanceRuntimeProgram(id); return true;
        }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
        { ClearPendingDecision(); ReplaceRuntimeTop(f with { PhaseNamePrediction = null }); AdvanceRuntimeProgram(id); return true; }
        if (_pendingDecision is null) PublishPredictionChoice(f); return true;
    }
    private bool CanContinuePhaseNamePrediction(ProgramSkillFrame f) => f.PhaseNamePrediction is not null ||
        f.WindowContext?.PhaseNamePrediction is { } p && _phaseNamePredictions.Contains(p);
    private bool IsPhaseNamePredictionMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        e is not null && PhaseNamePredictionComposition.IsOperation(e.Op) && f.PendingMovementContinuation == pending &&
        f.PhaseNamePrediction is { Stage: PhaseNamePredictionStage.CostChildren or PhaseNamePredictionStage.ClaimChildren };
    private void AssertPhaseNamePrediction(ProgramSkillFrame f)
    {
        if (f.PhaseNamePrediction is not { } r) return;
        if (f.InstructionIndex != 1 || r.Origin.Source.OwnerSeat != f.OwnerSeat || r.Origin.Source.SkillId != f.SkillId ||
            r.Origin.Source.SkillInstanceId != f.SkillInstanceId || r.Origin.GameplayHash != f.GameplayHash ||
            r.Origin.ActualTurnNumber != _turnNumber || r.Origin.ActualTurnOwnerSeat != _currentSeat ||
            r.Origin.PhaseInstanceId != CurrentPredictionPhaseInstanceId ||
            CompleteProgramEventHistory().OfType<PhaseNamePredictionStartedEvent>().Count(e => e == r.Origin) != 1 ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Any(id => !r.Eligible.Any(m => m.CardId == id)))
            throw new InvalidOperationException("Prediction receipt lost its original source, phase or immutable materials.");
        if (r.IssuedPolicy is { } p && (!_phaseNamePredictions.Contains(p) || f.WindowContext?.PhaseNamePrediction != p ||
            CompleteProgramEventHistory().OfType<PhaseNamePredictionSettledEvent>().Count(e => e.FrameId == f.Id && e.OriginalFrameId == p.Origin.FrameId) != 1))
            throw new InvalidOperationException("Prediction settlement lost its one issued policy.");
        if (r.Stage is PhaseNamePredictionStage.CostChildren or PhaseNamePredictionStage.ClaimChildren)
        {
            var reason = r.Stage == PhaseNamePredictionStage.CostChildren ? PhasePredictionCostReason : PhasePredictionClaimReason;
            var destination = r.Stage == PhaseNamePredictionStage.CostChildren ? CardLocation.DiscardPile : CardLocation.Hand(f.OwnerSeat);
            if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != f.OwnerSeat ||
                r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore || r.SequenceAfter > _movementSequence ||
                r.Selected.Any(id => _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.CardId == id &&
                    m.From == r.Eligible.Single(x => x.CardId == id).From && (m.To == destination ||
                        r.Stage == PhaseNamePredictionStage.CostChildren && m.To == CardLocation.OutsideGame && GetAttackCard(id).IsGeneralWeapon) && m.Reason.Value == reason) != 1))
                throw new InvalidOperationException("Prediction movement lost its one exact paid interval.");
        }
    }
    private sealed partial class ProgramSkillHost : IPhaseNamePredictionHost
    {
        public SkillProgramStepOutcome BeginPhaseNamePrediction(ProgramSkillFrame f, string state) => engine.BeginPhaseNamePrediction(f, state);
        public SkillProgramStepOutcome SettlePhaseNamePrediction(ProgramSkillFrame f, string state) => engine.SettlePhaseNamePrediction(f, state);
        public bool CanContinuePhaseNamePrediction(ProgramSkillFrame f) => engine.CanContinuePhaseNamePrediction(f);
    }
}
