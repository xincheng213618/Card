namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5ShensuId = "shanhe:s5-shensu-14144";
    private bool TracksShanheS5PhaseSkips => _contentRegistry?.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.PrepareShanheS5Shensu) == true;

    private void RecordShanheS5ActualPhaseSkip(int owner, SkillProgramTurnPhase phase,
        long extraDrawFrameId = 0, long? costProgramFrameId = null)
    {
        if (!TracksShanheS5PhaseSkips) return;
        if (_turnNumber <= 0 || owner != _currentSeat || !IsValidPlayerSeat(owner) ||
            phase is not (SkillProgramTurnPhase.Judgment or SkillProgramTurnPhase.Draw or
                SkillProgramTurnPhase.Play or SkillProgramTurnPhase.Discard) ||
            extraDrawFrameId < 0 || extraDrawFrameId != 0 && phase != SkillProgramTurnPhase.Draw)
            throw new InvalidOperationException("S5 Shensu phase counting requires an exact actual-turn phase occurrence.");
        if (ProgramEventHistory<ShanheS5SkippedPhaseRecordedEvent>().Any(e => e.OwnerSeat == owner &&
            e.ActualTurnNumber == _turnNumber && e.Phase == phase && e.ExtraDrawFrameId == extraDrawFrameId)) return;
        AdvanceEventRulesAndQueueFact(new ShanheS5SkippedPhaseRecordedEvent(owner, _turnNumber,
            phase, extraDrawFrameId, costProgramFrameId));
    }

    // Called by the already validated shared SkipTurnPhases operation, before
    // either its normal flag path or its extra-Draw substitution early return.
    private void RecordShanheS5PhaseSkipCost(ProgramSkillFrame f,
        IReadOnlyList<SkillProgramTurnPhase> phases, ProgramLifecycleTriggerWindowFrame parent)
    {
        if (!TracksShanheS5PhaseSkips) return;
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || f.OwnerSeat != _currentSeat ||
            f.WindowContext?.ParentFrameId != parent.Id || _resolutionStack.Count < 2 ||
            _resolutionStack[^2].Id != parent.Id || phases.Count == 0)
            throw new InvalidOperationException("S5 Shensu cannot count an uncommitted or foreign phase cost.");
        var extra = parent.ResumeDrawPhaseObligationFrameId is { } drawId &&
            _resolutionStack.OfType<DrawPhaseObligationFrame>().SingleOrDefault(d => d.Id == drawId) is { IsExtra: true } draw
            ? draw.Id : 0;
        foreach (var phase in phases)
        {
            var occurrence = phase == SkillProgramTurnPhase.Draw ? extra : 0;
            if (ProgramEventHistory<ShanheS5PhaseSkipCostEvent>().Any(e => e.FrameId == f.Id &&
                e.InstructionIndex == f.InstructionIndex && e.Phase == phase))
                throw new InvalidOperationException("A phase cost cannot pay or count twice on the same native instruction.");
            AdvanceEventRulesAndQueueFact(new ShanheS5PhaseSkipCostEvent(f.Id, f.InstructionIndex,
                f.OwnerSeat, _turnNumber, phase, occurrence));
            RecordShanheS5ActualPhaseSkip(f.OwnerSeat, phase, occurrence, f.Id);
        }
    }

    [ProgramPipelineHook(ProgramPipelinePhase.ActualPhaseSkipped, 59200)]
    private bool ObserveShanheS5NativePhaseSkip(ProgramActualPhaseSkipContext context)
    {
        if (!TracksShanheS5PhaseSkips) return false;
        if (context.ActualTurn != _turnNumber)
            throw new InvalidOperationException("A skipped phase crossed its original actual turn.");
        RecordShanheS5ActualPhaseSkip(context.OwnerSeat, context.Phase);
        return false;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 59200)]
    private void ObserveShanheS5ExtraDrawSkip(IGameEvent payload)
    {
        if (!TracksShanheS5PhaseSkips || payload is not DrawPhaseSkippedEvent e) return;
        if (e.ActualTurnNumber != _turnNumber || e.OwnerSeat != _currentSeat ||
            _resolutionStack.OfType<DrawPhaseObligationFrame>().SingleOrDefault(d => d.Id == e.FrameId) is not
                { Skipped: true } native || native.OwnerSeat != e.OwnerSeat || native.IsExtra != e.IsExtra)
            throw new InvalidOperationException("S5 Shensu lost a genuine skipped normal or extra Draw occurrence.");
        RecordShanheS5ActualPhaseSkip(e.OwnerSeat, SkillProgramTurnPhase.Draw, e.IsExtra ? e.FrameId : 0);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 59200,
        SkillProgramEffectOp.PrepareShanheS5Shensu)]
    private bool CanOfferShanheS5Shensu(ProgramTriggerLegalityContext gate) =>
        gate.SkillId != ShanheS5ShensuId || gate.OwnerSeat == _currentSeat && _players[gate.OwnerSeat].IsAlive &&
        _players.Any(p => CanUseVirtualSlashTarget(_players[gate.OwnerSeat], p,
            physicalSuit: Suit.None, physicalCardIds: [], ignoreDistance: true, countsTowardLimit: false));

    private void PrepareShanheS5Shensu(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShanheS5Shensu is not null || f.SkillId != ShanheS5ShensuId ||
            f.WindowContext is not { } context || f.OwnerSeat != _currentSeat ||
            f.SelectedTargetSeats is not [var target] || !_players[target].IsAlive || target == f.OwnerSeat ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Window != context.Window ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("S5 Shensu requires its original current phase candidate and selected native Slash target.");
        var phases = ShanheS5ShensuContract.Phases(context.Window);
        var costs = ProgramEventHistory<ShanheS5PhaseSkipCostEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (phases.Length == 0 || costs.Length != phases.Length ||
            !costs.Select(e => e.Phase).SequenceEqual(phases) || costs.Any(e => e.OwnerSeat != f.OwnerSeat ||
                e.ActualTurnNumber != _turnNumber || e.InstructionIndex != 2 || e.ExtraDrawFrameId != 0) ||
            f.InstructionIndex != (context.Window == SkillProgramTriggerWindow.DiscardPhaseStarting ? 4 : 3))
            throw new InvalidOperationException("S5 Shensu must freeze X after this exact actual phase cost was committed.");
        var count = ProgramEventHistory<ShanheS5SkippedPhaseRecordedEvent>().Count(e =>
            e.OwnerSeat == f.OwnerSeat && e.ActualTurnNumber == _turnNumber);
        var receipt = new ShanheS5ShensuReceipt(new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, _turnNumber, parent.Id, context.Window, target, count);
        ReplaceRuntimeTop(f with { ShanheS5Shensu = receipt });
        AdvanceEventRulesAndQueueFact(new ShanheS5ShensuPreparedEvent(f.Id, receipt));
    }

    private bool IsShanheS5ShensuFrame(ProgramSkillFrame f) =>
        f.SkillId == ShanheS5ShensuId && f.ShanheS5Shensu is not null;

    private int ShanheS5ShensuBaseDamageBonus(ProgramSkillFrame active)
    {
        if (!IsShanheS5ShensuFrame(active)) return 0;
        var root = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == active.Id)
            ?? throw new InvalidOperationException("S5 Shensu's original issuing frame is missing.");
        AssertShanheS5Shensu(root);
        var receipt = root.ShanheS5Shensu!;
        if (_resolutionStack.Count < 2 || _resolutionStack[^2].Id != root.Id ||
            _resolutionStack[^1] is not CardUseFrame { CardId: 0, CardKind: CardKind.Slash,
                PhysicalCardIds.Count: 0, Action: { PhysicalCards.Count: 0 } action } use ||
            action.ActorSeat != root.OwnerSeat || use.SourceSeat != root.OwnerSeat ||
            action.ConversionChain is not [var source] || source != receipt.Source)
            throw new InvalidOperationException("S5 Shensu base damage belongs only to its exact newly issued native virtual Slash.");
        // This is initial native damage, not an additional modifier. The real
        // incoming prediction, Dodge, later same-use elemental conversion and
        // damage payment all read this one frozen base.
        return receipt.SkippedPhaseCount;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 59200)]
    private void AssertShanheS5Shensu(ProgramSkillFrame f)
    {
        if (f.ShanheS5Shensu is not { } r) return;
        if (f.SkillId != ShanheS5ShensuId || f.GameplayHash != r.GameplayHash ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.ActualTurnNumber != _turnNumber || r.SkippedPhaseCount < 1 ||
            f.WindowContext?.ParentFrameId != r.LifecycleFrameId || f.WindowContext.Window != r.Window ||
            ProgramEventHistory<ShanheS5ShensuPreparedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid.Receipt != r || CompleteProgramEventHistory().TakeWhile(e => e is not ShanheS5ShensuPreparedEvent p || p.FrameId != f.Id)
                .OfType<ShanheS5SkippedPhaseRecordedEvent>().Count(e => e.OwnerSeat == f.OwnerSeat &&
                    e.ActualTurnNumber == r.ActualTurnNumber) != r.SkippedPhaseCount)
            throw new InvalidOperationException("S5 Shensu lost its frozen actual-turn, phase costs or original native Slash invoice.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 59200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 59200)]
    private bool ContinueShanheS5Shensu(ProgramSkillFrame f)
    {
        if (f.ShanheS5Shensu is null) return IsPaidShanheS5TurnoverReturn(f);
        AssertShanheS5Shensu(f); return true;
    }

    private bool IsPaidShanheS5TurnoverReturn(ProgramSkillFrame f)
    {
        if (f.SkillId != ShanheS5ShensuId || f.InstructionIndex != 3 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPhaseStarting } context ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not
                { Window: SkillProgramTriggerWindow.DiscardPhaseStarting } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
            ProgramEventHistory<ShanheS5PhaseSkipCostEvent>().Where(e => e.FrameId == f.Id).ToArray() is not
                [{ InstructionIndex: 2, Phase: SkillProgramTurnPhase.Discard, ExtraDrawFrameId: 0 } cost] ||
            cost.OwnerSeat != f.OwnerSeat || cost.ActualTurnNumber != _turnNumber || f.OwnerSeat != _currentSeat)
            return false;
        // Only the actual already-issued turnover on this original frame can
        // bridge source loss before Prepare freezes X. A mere queued phase
        // flag, a different character's turnover or a fresh trigger cannot.
        return ProgramEventHistory<CharacterStateChangedEvent>().Count(e =>
            e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == f.OwnerSeat &&
            e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver &&
            e.Change.TurnedOver is { } paid && paid.WasFaceDown != paid.IsFaceDown) == 1;
    }

    [ProgramCapability]
    private sealed class ShanheS5ShensuCapability(GameEngine engine) : IShanheS5ShensuHost
    {
        public void PrepareShanheS5Shensu(ProgramSkillFrame f) => engine.PrepareShanheS5Shensu(f);
    }
}
