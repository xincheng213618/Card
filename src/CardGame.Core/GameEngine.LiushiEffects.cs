namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsLiushiClearDraw(ProgramSkillFrame frame) => frame.TriggerId is not null &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.DiscardPhaseEnded } &&
        LiushiEffectContract.Clears(GetProgramTrigger(frame));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 1283)]
    private bool CanOfferLiushiClear(SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !LiushiEffectContract.Clears(trigger) ||
        context.Window == SkillProgramTriggerWindow.DiscardPhaseEnded && context.TargetSeat is not null &&
        context.Facts is { TurnOwnerDiscardPhaseHandDiscardCount: > 0, EventTargetMarkerCounts: { } markers } &&
        markers.GetValueOrDefault(PlayerMarkerKind.LiuShi) > 0;

    private void ApplyProgramLiushiEffect(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1] is not DamageTriggerWindowFrame window ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0,
                SourceSeat: { } source, TargetSeat: { } target } context ||
            context.ParentFrameId != window.Id || context.DamageFrameId != window.ParentFrameId ||
            source != f.OwnerSeat || target != window.TargetSeat || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()))
            throw new InvalidOperationException("Liushi lost its exact applied-damage candidate.");
        var attack = GetDamageTriggerAttack(window);
        if (attack.IsChainPropagation) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == attack.ResolutionId);
        if (attack.IsSourceLess || !attack.DamageWasApplied || attack.DamageAmount != context.Amount ||
            use?.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
            action.ActorSeat != source || attack.TargetSeat != target || attack.ConversionSource is not { } conversion ||
            !action.ConversionChain.Contains(conversion) || conversion.SkillId != f.SkillId || conversion.SkillInstanceId != f.SkillInstanceId ||
            conversion.OwnerSeat != source || !HasRuntimeSkillInstance(_players[source], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Liushi requires its own exact native converted Slash damage.");
        if (!_players[target].IsAlive) return;
        if (ProgramEventHistory<ProgramLiushiEffectAppliedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Liushi damage was already paid.");
        MutateParticipantMarker(f, target, PlayerMarkerKind.LiuShi, 1);
        AdvanceEventRulesAndQueueFact(new ProgramLiushiEffectAppliedEvent(f.Id, window.Id, window.ParentFrameId,
            use.Id, action.ActionId, source, target, f.SkillId, f.SkillInstanceId,
            _players[target].Markers.GetValueOrDefault(PlayerMarkerKind.LiuShi)));
    }

    private void CaptureLiushiClearDrawPayment(ProgramSkillFrame frame, int requested, long before, long after,
        int actual, long? attemptId, bool replaced)
    {
        if (!IsLiushiClearDraw(frame)) return;
        var f = GetActiveProgramFrame(frame.Id);
        if (f.LiushiDrawPayment is not null || f.InstructionIndex != 1 ||
            f.WindowContext?.TargetSeat is not { } target || requested < 1 ||
            f.WindowContext.Facts?.TurnOwnerDiscardPhaseHandDiscardCount != requested)
            throw new InvalidOperationException("Liushi clearing lost its real positive discard draw payment.");
        var payment = new ProgramLiushiDrawPayment(f.Id, f.SkillId, f.SkillInstanceId, f.GameplayHash,
            f.OwnerSeat, f.WindowContext.ParentFrameId, target, f.InstructionIndex, requested, actual,
            before, after, attemptId, replaced);
        ReplaceRuntimeTop(f with { LiushiDrawPayment = payment });
        AdvanceEventRulesAndQueueFact(new ProgramLiushiDrawPaidEvent(payment));
    }

    private bool HasPaidLiushiClear(ProgramSkillFrame f)
    {
        if (f.LiushiDrawPayment is not { } p) return false;
        if (!IsLiushiClearDraw(f) || p.FrameId != f.Id || p.SkillId != f.SkillId ||
            p.SkillInstanceId != f.SkillInstanceId || p.GameplayHash != f.GameplayHash || p.OwnerSeat != f.OwnerSeat ||
            f.WindowContext is not { } c || p.WindowId != c.ParentFrameId || p.TargetSeat != c.TargetSeat ||
            p.RequestedCount < 1 || p.RequestedCount != c.Facts?.TurnOwnerDiscardPhaseHandDiscardCount ||
            p.InstructionIndex != 1 || f.InstructionIndex is < 1 or > 2 || p.ActualCount < 0 || p.ActualCount > p.RequestedCount ||
            p.SequenceBefore > p.SequenceAfter ||
            ProgramEventHistory<ProgramLiushiDrawPaidEvent>().Count(e => e.Payment == p) != 1)
            throw new InvalidOperationException("Liushi clearing lost its issued owning draw receipt.");
        var parent = _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == p.WindowId);
        if (parent is null || parent.Window != SkillProgramTriggerWindow.DiscardPhaseEnded || parent.OwnerSeat != p.TargetSeat ||
            parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]))
            throw new InvalidOperationException("Liushi clearing lost its exact discard candidate parent.");
        var moves = _cardMovements.Where(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter &&
            m.Reason == new CardMoveReason($"skill-program.{f.SkillId}.{SkillProgramEffectOp.Draw}")).ToArray();
        if (moves.Length != p.ActualCount || moves.Select(m => m.CardId).Distinct().Count() != moves.Length ||
            moves.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat)))
            throw new InvalidOperationException("Liushi clearing draw materials changed after payment.");
        if (p.AttemptId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
            e.AttemptFrameId == attempt && e.ParentFrameId == f.Id && e.RecipientSeat == f.OwnerSeat &&
            e.RequestedCount == p.RequestedCount && e.Replaced == p.Replaced) != 1)
            throw new InvalidOperationException("Liushi clearing lost its exact native draw decision.");
        return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 1283)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 1283)]
    private bool ContinuePaidLiushiClear(ProgramSkillFrame f) => HasPaidLiushiClear(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 1283)]
    private void AssertLiushiClearPayment(ProgramSkillFrame f)
    { if (f.LiushiDrawPayment is not null) _ = HasPaidLiushiClear(f); }

    private void ClearProgramLiushiEffect(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (!HasPaidLiushiClear(f) || f.InstructionIndex != 2 || f.NativeDraw is not null ||
            ProgramEventHistory<ProgramLiushiEffectClearedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Liushi cannot clear before its paid draw children return or clear twice.");
        var p = f.LiushiDrawPayment!;
        var target = _players[p.TargetSeat];
        var count = target.Markers.GetValueOrDefault(PlayerMarkerKind.LiuShi);
        var sources = target.MarkerSourceCounts.Where(s => s.Key.Marker == PlayerMarkerKind.LiuShi).OrderBy(s => s.Key.SkillOwnerSeat).ToArray();
        if (sources.Sum(s => s.Value) != count) throw new InvalidOperationException("Liushi marker attribution is inconsistent.");
        foreach (var source in sources)
        {
            target.MarkerSourceCounts.Remove(source.Key);
            count -= source.Value;
            if (count == 0) target.Markers.Remove(PlayerMarkerKind.LiuShi); else target.Markers[PlayerMarkerKind.LiuShi] = count;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, p.TargetSeat, PlayerMarkerKind.LiuShi,
                -source.Value, count, source.Key.SkillOwnerSeat, "program.liushi.clear"));
        }
        AdvanceEventRulesAndQueueFact(new ProgramLiushiEffectClearedEvent(f.Id, p.WindowId, f.OwnerSeat, p.TargetSeat,
            sources.Sum(s => s.Value)));
    }

    [ProgramCapability]
    private sealed class LiushiEffectCapability(GameEngine engine) : ILiushiEffectHost
    {
        public void ApplyLiushiEffect(ProgramSkillFrame f) => engine.ApplyProgramLiushiEffect(f);
        public void ClearLiushiEffect(ProgramSkillFrame f) => engine.ClearProgramLiushiEffect(f);
    }
}
