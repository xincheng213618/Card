namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasYintianCapability => _contentRegistry?.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveYintianFirstRecovery) == true;
    private bool YintianActualTurnOpen => _setupComplete && _turnNumber > 0 &&
        _turnNumber == _turnProgression.TurnNumber && !ProgramEventHistory<ActualTurnCursorReturnedEvent>()
            .Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _turnProgression.OwnerSeat);
    private static bool IsYintianRecoveryTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveYintianFirstRecovery }];

    // Capture even when nobody currently has Yintian. Acquiring it later cannot
    // turn the second positive recovery into this actual turn's first recovery.
    private void CaptureYintianRecoveryQualification(HpChangeContext change)
    {
        if (!HasYintianCapability || !YintianActualTurnOpen || change.Kind != HpChangeKind.Recovery ||
            change.HpAfter <= change.HpBefore || change.Amount != change.HpAfter - change.HpBefore ||
            !_pendingHpChanges.Contains(change)) return;
        if (ProgramEventHistory<YintianFirstRecoveryEvent>().Any(e => e.ActualTurn == _turnNumber &&
                e.TurnOwner == _turnProgression.OwnerSeat)) return;
        var first = new YintianFirstRecoveryEvent(change.Id, change.ParentFrameId, _turnNumber,
            _turnProgression.OwnerSeat, change.SourceSeat, change.TargetSeat, change.Amount,
            change.HpBefore, change.HpAfter);
        AdvanceEventRulesAndQueueFact(first);
        foreach (var owner in _players.Where(p => p.IsAlive)
                     .OrderBy(p => (p.Seat - first.TurnOwner + _players.Count) % _players.Count))
        {
            var sources = GetSkillBindingShard(owner).ProgramInstances.SelectMany(instance =>
                instance.Program.Triggers.Where(IsYintianRecoveryTrigger).Select(trigger =>
                    new ProgramTriggerCandidate(owner.Seat, instance.SkillId, trigger.Id,
                        instance.SkillInstanceId, instance.Program.GameplayHash, trigger.Priority)))
                .Where(c => HasRuntimeSkillInstance(owner, c.SkillId, c.SkillInstanceId))
                .OrderByDescending(c => c.Priority).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).ToArray();
            if (sources is not [var candidate, ..]) continue;
            AdvanceEventRulesAndQueueFact(new YintianRecoveryQualifiedEvent(change.Id,
                new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId),
                candidate.GameplayHash, first.ActualTurn, first.TurnOwner));
        }
    }

    private YintianFirstRecoveryEvent? YintianFirstRecovery(HpChangeContext change) =>
        ProgramEventHistory<YintianFirstRecoveryEvent>().SingleOrDefault(e => e.ChangeId == change.Id &&
            e.NativeParentId == change.ParentFrameId && e.SourceSeat == change.SourceSeat &&
            e.TargetSeat == change.TargetSeat && e.Amount == change.Amount && e.HpBefore == change.HpBefore &&
            e.HpAfter == change.HpAfter && change.Kind == HpChangeKind.Recovery && e.Amount > 0 &&
            e.HpAfter - e.HpBefore == e.Amount);
    private YintianRecoveryQualifiedEvent? YintianQualification(ProgramTriggerCandidate candidate, long changeId) =>
        ProgramEventHistory<YintianRecoveryQualifiedEvent>().SingleOrDefault(e => e.ChangeId == changeId &&
            e.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId) &&
            e.GameplayHash == candidate.GameplayHash);

    private IReadOnlyList<ProgramTriggerCandidate> CollectYintianRecoveryCandidates(HpChangeContext change)
    {
        if (!HasYintianCapability || YintianFirstRecovery(change) is not { } first ||
            first.ActualTurn != _turnNumber || first.TurnOwner != _turnProgression.OwnerSeat) return [];
        return Array.AsReadOnly(ProgramEventHistory<YintianRecoveryQualifiedEvent>()
            .Where(e => e.ChangeId == change.Id && e.ActualTurn == first.ActualTurn && e.TurnOwner == first.TurnOwner &&
                IsValidPlayerSeat(e.Source.OwnerSeat) &&
                _contentRegistry.Skills.GetValueOrDefault(e.Source.SkillId)?.Program is { } program &&
                program.GameplayHash == e.GameplayHash)
            .Select(e => new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId,
                e.Source.SkillInstanceId, e.GameplayHash,
                _contentRegistry.GetSkill(e.Source.SkillId).Program!.Triggers.Single(t => t.Id == e.Source.BindingId).Priority))
            .Where(c => IsYintianRecoveryTrigger(GetProgramTrigger(c)))
            .OrderBy(c => (c.OwnerSeat - first.TurnOwner + _players.Count) % _players.Count).ToArray());
    }

    private ProgramSkillWindowContext CreateYintianRecoveryContext(HpChangeContext change, ProgramTriggerCandidate candidate)
    {
        if (YintianFirstRecovery(change) is not { } first || YintianQualification(candidate, change.Id) is not { } q ||
            q.ActualTurn != first.ActualTurn || q.TurnOwner != first.TurnOwner)
            throw new InvalidOperationException("Yintian requires its exact producer-frozen first positive recovery.");
        return new(SkillProgramTriggerWindow.AfterHpRecovered, change.Id, candidate.OwnerSeat,
            SourceSeat: change.SourceSeat, TargetSeat: change.TargetSeat, Amount: change.Amount,
            OccurrenceIndex: candidate.OccurrenceIndex, HpChange: change,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with
            { HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter, HealthChangeKind = change.Kind });
    }

    private bool CanRunYintianRecovery(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (!IsYintianRecoveryTrigger(GetProgramTrigger(candidate)) || candidate.OccurrenceIndex != 0 ||
            context is not { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery } change } ||
            context.ParentFrameId != change.Id || context.OwnerSeat != candidate.OwnerSeat || context.SourceSeat != change.SourceSeat ||
            context.TargetSeat != change.TargetSeat || context.Amount != change.Amount || context.OccurrenceIndex != 0 ||
            YintianFirstRecovery(change) is not { } first || YintianQualification(candidate, change.Id) is not { } q ||
            first.ActualTurn != q.ActualTurn || first.TurnOwner != q.TurnOwner ||
            first.ActualTurn != _turnNumber || first.TurnOwner != _turnProgression.OwnerSeat ||
            _resolutionStack.SingleOrDefault(f => f.Id == change.Id) is not HpChangedTriggerWindowFrame window ||
            window.Change != change || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            window.Candidates[window.CandidateIndex] != candidate || window.Contexts[window.CandidateIndex].HpChange != change) return false;
        return !ProgramEventHistory<YintianChargeIssuedEvent>().Any(e => e.RecoveryChangeId == change.Id && e.Source.OwnerSeat == candidate.OwnerSeat) &&
            !ProgramEventHistory<YintianChargeRetainedEvent>().Any(e => e.RecoveryChangeId == change.Id && e.Source.OwnerSeat == candidate.OwnerSeat);
    }

    private bool IsIssuedYintianRecoveryCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        IsYintianRecoveryTrigger(GetProgramTrigger(candidate)) && CanRunYintianRecovery(candidate, context);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28113)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28113)]
    private bool ContinueIssuedYintianRecovery(ProgramSkillFrame frame)
    {
        if (frame.Yintian is not null) { AssertYintianRecovery(frame); return true; }
        return frame.TriggerId is { } id && frame.WindowContext is { } context &&
            IsIssuedYintianRecoveryCandidate(new(frame.OwnerSeat, frame.SkillId, id, frame.SkillInstanceId,
                frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex), context);
    }

    [ProgramCapability]
    private sealed class YintianCapability(GameEngine engine) : IYintianHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame frame) => engine.ResolveYintianFirstRecovery(frame); }

    private SkillProgramStepOutcome ResolveYintianFirstRecovery(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.Yintian is not null || frame.TriggerId is null || frame.InstructionIndex != 1 || frame.WindowContext is not { } context)
            throw new InvalidOperationException("Yintian requires its new exact first-recovery instruction.");
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId,
            frame.SkillInstanceId, frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex);
        if (!CanRunYintianRecovery(candidate, context))
            throw new InvalidOperationException("Yintian lost its original global first-recovery qualification.");
        var first = YintianFirstRecovery(context.HpChange!)!;
        var source = new CardConversionSource(frame.SkillId, frame.TriggerId, frame.OwnerSeat, frame.SkillInstanceId);
        var existing = AvailableYintianCharge(frame.OwnerSeat);
        var receipt = new YintianRecoveryReceipt(source, frame.GameplayHash, first.ChangeId,
            first.ActualTurn, first.TurnOwner, existing?.GrantId ?? frame.Id, existing is null);
        ReplaceRuntimeTop(frame with { Yintian = receipt });
        if (existing is null)
            AdvanceEventRulesAndQueueFact(new YintianChargeIssuedEvent(frame.Id, source, frame.GameplayHash,
                first.ChangeId, first.ActualTurn, first.TurnOwner));
        else
            AdvanceEventRulesAndQueueFact(new YintianChargeRetainedEvent(frame.Id, source, frame.GameplayHash,
                first.ChangeId, first.ActualTurn, first.TurnOwner, existing.GrantId));
        return SkillProgramStepOutcome.Continue;
    }

    private bool ValidYintianCharge(YintianChargeIssuedEvent grant) =>
        grant.GrantId > 0 && IsValidPlayerSeat(grant.Source.OwnerSeat) &&
        _contentRegistry.Skills.GetValueOrDefault(grant.Source.SkillId)?.Program is { } program &&
        program.GameplayHash == grant.GameplayHash && program.Triggers.Any(t => t.Id == grant.Source.BindingId && IsYintianRecoveryTrigger(t)) &&
        ProgramEventHistory<YintianRecoveryQualifiedEvent>().Count(q => q.ChangeId == grant.RecoveryChangeId &&
            q.Source == grant.Source && q.GameplayHash == grant.GameplayHash && q.ActualTurn == grant.ActualTurn && q.TurnOwner == grant.TurnOwner) == 1 &&
        ProgramEventHistory<YintianFirstRecoveryEvent>().Count(f => f.ChangeId == grant.RecoveryChangeId &&
            f.ActualTurn == grant.ActualTurn && f.TurnOwner == grant.TurnOwner && f.Amount > 0 && f.HpAfter - f.HpBefore == f.Amount) == 1;
    private YintianChargeIssuedEvent? AvailableYintianCharge(int owner)
    {
        var grants = ProgramEventHistory<YintianChargeIssuedEvent>().Where(g => g.Source.OwnerSeat == owner &&
            !ProgramEventHistory<YintianChargeConsumedEvent>().Any(c => c.GrantId == g.GrantId)).ToArray();
        if (grants.Length > 1 || grants.Any(g => !ValidYintianCharge(g)))
            throw new InvalidOperationException("Yintian must retain at most one exact unconsumed charge per owner.");
        return grants.SingleOrDefault();
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28113)]
    private void AssertYintianRecovery(ProgramSkillFrame frame)
    {
        if (frame.Yintian is not { } r) return;
        if (frame.InstructionIndex != 1 || frame.TriggerId is null ||
            r.Source != new CardConversionSource(frame.SkillId, frame.TriggerId, frame.OwnerSeat, frame.SkillInstanceId) ||
            r.GameplayHash != frame.GameplayHash || frame.WindowContext?.HpChange?.Id != r.ChangeId ||
            (r.Issued ? ProgramEventHistory<YintianChargeIssuedEvent>().Count(e => e.GrantId == frame.Id && e.GrantId == r.ChargeId &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.RecoveryChangeId == r.ChangeId && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner) != 1 :
                ProgramEventHistory<YintianChargeRetainedEvent>().Count(e => e.FrameId == frame.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                    e.RecoveryChangeId == r.ChangeId && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner && e.ExistingGrantId == r.ChargeId) != 1))
            throw new InvalidOperationException("Yintian lost its immutable exact first-recovery resolution.");
    }
}
