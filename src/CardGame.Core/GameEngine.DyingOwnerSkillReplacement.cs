namespace CardGame.Core;

/// <summary>The already-paid owner HP loss and its one remaining unconditional draw.</summary>
public sealed record ProgramReplacedPaidHpContinuation(
    long ProgramFrameId, int OwnerSeat, string SkillId, string BindingId,
    string SkillInstanceId, string GameplayHash, int PaidInstructionIndex, int DrawAmount,
    long DyingFrameId, long EntryWindowFrameId, long ReplacementFrameId,
    string ReplacementSkillId, string ReplacementBindingId, string ReplacementSkillInstanceId,
    string ReplacementGameplayHash, int ReplacementInstructionIndex, string GrantedSkillId,
    int ActualTurnNumber, int ActualTurnOwnerSeat);

public sealed record ProgramReplacedPaidHpContinuationIssuedEvent(
    ProgramReplacedPaidHpContinuation Continuation) : IGameEvent;

internal static class DyingOwnerSkillReplacementContract
{
    internal static bool HasReplacement(IReadOnlyList<SkillProgramEffect> effects) =>
        effects.Any(effect => effect.Op == SkillProgramEffectOp.LoseOwnerSkillsAndGrant);

    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        bool optional, SkillUsageScope? scope, int? limit)
    {
        if (!HasReplacement(effects) || window != SkillProgramTriggerWindow.DyingEntering) return;
        if (subject != SkillProgramTriggerSubject.Owner || optional || scope != SkillUsageScope.Game || limit != 1)
            throw new InvalidOperationException($"Invalid skill program at {path}: dying-entry owner skill replacement requires a mandatory owner game-once binding.");
    }

    internal static bool IsPaidOwnerDrawTail(IReadOnlyList<SkillProgramEffect> effects) =>
        effects.Count == 2 &&
        effects[0] is { Op: SkillProgramEffectOp.LoseHp, Target: SkillProgramEffectTarget.Owner,
            Condition.Kind: SkillProgramConditionKind.Always, CompiledInstruction: LoseHpProgramInstruction { Amount: 1 } } &&
        effects[1] is { Op: SkillProgramEffectOp.Draw, Condition.Kind: SkillProgramConditionKind.Always,
            CompiledInstruction: DrawProgramInstruction { Target: SkillProgramEffectTarget.Owner,
                Amount: FixedProgramAmount { Value: > 0 }, TargetReference: null } };
}

public sealed partial class GameEngine
{
    private (ProgramLifecycleTriggerWindowFrame Entry, DyingFrame Dying)
        RequireDyingOwnerReplacementContext(ProgramSkillFrame frame)
    {
        var trigger = GetProgramTrigger(frame);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering } context ||
            context.OwnerSeat != frame.OwnerSeat || context.TargetSeat != frame.OwnerSeat ||
            trigger.Window != context.Window || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.Optional || trigger.UsageScope != SkillUsageScope.Game || trigger.UsageLimit != 1 ||
            frame.InstructionIndex < 1 || frame.InstructionIndex > trigger.Effects.Count ||
            trigger.Effects[^1] is not { Op: SkillProgramEffectOp.LoseOwnerSkillsAndGrant,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } terminal ||
            terminal.SkillIds.Contains(terminal.SourceBind!, StringComparer.Ordinal) ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            _resolutionStack.Count < 3 || _resolutionStack[^1].Id != frame.Id ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame entry ||
            entry.Id != context.ParentFrameId || entry.Window != context.Window ||
            entry.OwnerSeat != frame.OwnerSeat || entry.Continuation != ProgramLifecycleContinuation.ResumeDyingEntry ||
            entry.CandidateIndex < 0 || entry.CandidateIndex >= entry.Candidates.Count ||
            _resolutionStack[^3] is not DyingFrame dying || entry.ResumeDyingFrameId != dying.Id ||
            ActiveDying?.Id != dying.Id || dying.VictimSeat != frame.OwnerSeat ||
            context.SourceSeat != dying.KillerSeat)
            throw new InvalidOperationException("Dying-entry skill replacement lost its exact owner, instruction or typed dying parent.");
        var candidate = entry.Candidates[entry.CandidateIndex];
        if (candidate.OwnerSeat != frame.OwnerSeat || candidate.SkillId != frame.SkillId ||
            candidate.BindingId != frame.TriggerId || candidate.SkillInstanceId != frame.SkillInstanceId ||
            candidate.GameplayHash != frame.GameplayHash || candidate.OccurrenceIndex != context.OccurrenceIndex ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == frame.SkillId &&
                fact.BindingId == frame.TriggerId && fact.SkillInstanceId == frame.SkillInstanceId &&
                fact.Window == SkillProgramTriggerWindow.DyingEntering) != 1)
            throw new InvalidOperationException("Dying-entry skill replacement lost its frozen candidate or issued binding.");
        return (entry, dying);
    }

    private bool IsDyingOwnerReplacementBinding(ProgramSkillFrame frame) =>
        frame.WindowContext?.Window == SkillProgramTriggerWindow.DyingEntering && frame.TriggerId is not null &&
        DyingOwnerSkillReplacementContract.HasReplacement(GetProgramTrigger(frame).Effects);

    private IReadOnlyList<string> AcquireDyingOwnerReplacementSkills(
        ProgramSkillFrame frame, IReadOnlyList<string> skillIds)
    {
        RequireDyingOwnerReplacementContext(frame);
        var effect = GetProgramTrigger(frame).Effects[frame.InstructionIndex - 1];
        if (effect.Op == SkillProgramEffectOp.GrantSkills
                ? !effect.SkillIds.SequenceEqual(skillIds, StringComparer.Ordinal)
                : effect.Op != SkillProgramEffectOp.LoseOwnerSkillsAndGrant ||
                  frame.InstructionIndex != GetProgramTrigger(frame).Effects.Count ||
                  !skillIds.SequenceEqual([effect.SourceBind!], StringComparer.Ordinal))
            throw new InvalidOperationException("Dying-entry skill acquisition lost its exact active instruction.");
        var owner = _players[frame.OwnerSeat];
        var acquired = new List<string>();
        var sourceId = $"acquired:{frame.SkillId}";
        foreach (var skillId in skillIds.Distinct(StringComparer.Ordinal))
        {
            _ = _contentRegistry.GetSkill(skillId);
            var existing = GetSkillBindingShard(owner).ActiveGrants.Where(grant => grant.SkillId == skillId)
                .OrderBy(grant => grant.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(grant => grant.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            if (existing is null) acquired.Add(skillId);
            var grantId = $"{sourceId}:{skillId}";
            if (owner.SkillGrants.Grants.Any(grant => grant.GrantId == grantId)) continue;
            // The awakening owns an independent permanent source. An already
            // effective skill retains its instance and its existing phase quota.
            owner.SkillGrants.Grant(new SkillGrant(grantId, skillId,
                existing?.SkillInstanceId ?? grantId, sourceId));
        }
        if (acquired.Count == 0) return [];
        foreach (var skillId in acquired) RegisterTaggedConversionSkill(owner, skillId);
        AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(owner.Seat, frame.SkillId,
            Array.AsReadOnly(acquired.ToArray())));
        return Array.AsReadOnly(acquired.ToArray());
    }

    private ProgramReplacedPaidHpContinuation? CaptureDyingOwnerReplacementContinuation(
        ProgramSkillFrame replacement, SkillProgramEffect effect)
    {
        var (entry, dying) = RequireDyingOwnerReplacementContext(replacement);
        var trigger = GetProgramTrigger(replacement);
        if (replacement.InstructionIndex != trigger.Effects.Count || !ReferenceEquals(trigger.Effects[^1], effect))
            throw new InvalidOperationException("Dying-entry skill replacement is not its exact terminal instruction.");
        // Other genuine dying producers may awaken too; only this already-paid
        // program ancestor needs a continuation after its grant is removed.
        if (dying.Continuation != DyingContinuationKind.ProgramSkill || _resolutionStack.Count < 4 ||
            _resolutionStack[^4] is not ProgramSkillFrame paid || paid.Id != dying.ParentFrameId ||
            paid.OwnerSeat != replacement.OwnerSeat || paid.InstructionIndex != 1 ||
            paid.ReexecuteParticipantInstruction || paid.SelectedCardIds.Count != 0 || paid.SelectedTargetSeats.Count != 0 ||
            !effect.SkillIds.Contains(paid.SkillId, StringComparer.Ordinal) ||
            !_players[paid.OwnerSeat].SkillGrants.Grants.Any(grant => grant.IsEnabled &&
                grant.SkillId == paid.SkillId && grant.SkillInstanceId == paid.SkillInstanceId)) return null;
        var program = _contentRegistry.GetSkill(paid.SkillId).Program!;
        if (program.GameplayHash != paid.GameplayHash) throw new InvalidOperationException("The paid HP ancestor definition changed.");
        var plan = ProgramInstructionResolver.Default.Resolve(paid, program);
        if (!DyingOwnerSkillReplacementContract.IsPaidOwnerDrawTail(plan.Instructions)) return null;
        if (paid.ReplacedPaidHpContinuation is not null ||
            CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(fact =>
                fact.FrameId == paid.Id && fact.SkillId == paid.SkillId && fact.TargetSeat == paid.OwnerSeat &&
                fact.Amount == 1 && fact.RemainingHp == 0) != 1 ||
            CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(fact =>
                fact.ResolutionId == dying.Id && fact.VictimSeat == paid.OwnerSeat && fact.KillerSeat is null) != 1)
            throw new InvalidOperationException("The removed owner program has no exact once-paid HP-loss dying fact.");
        return new(paid.Id, paid.OwnerSeat, paid.SkillId, GetProgramBindingId(paid), paid.SkillInstanceId,
            paid.GameplayHash, paid.InstructionIndex, ((FixedProgramAmount)((DrawProgramInstruction)plan.Instructions[1].CompiledInstruction!).Amount).Value,
            dying.Id, entry.Id, replacement.Id, replacement.SkillId, GetProgramBindingId(replacement),
            replacement.SkillInstanceId, replacement.GameplayHash, replacement.InstructionIndex, effect.SourceBind!,
            _turnNumber, _currentSeat);
    }

    private void IssueReplacedPaidHpContinuation(ProgramReplacedPaidHpContinuation continuation)
    {
        var paid = _resolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == continuation.ProgramFrameId);
        if (paid.ReplacedPaidHpContinuation is not null || paid.InstructionIndex != continuation.PaidInstructionIndex ||
            paid.OwnerSeat != continuation.OwnerSeat || paid.SkillId != continuation.SkillId ||
            paid.SkillInstanceId != continuation.SkillInstanceId || paid.GameplayHash != continuation.GameplayHash ||
            HasRuntimeSkillInstance(_players[paid.OwnerSeat], paid.SkillId, paid.SkillInstanceId))
            throw new InvalidOperationException("The replaced paid continuation did not remove its exact original skill instance.");
        ReplaceRuntimeFrame(paid.Id, paid with { ReplacedPaidHpContinuation = continuation });
        AdvanceEventRulesAndQueueFact(new ProgramReplacedPaidHpContinuationIssuedEvent(continuation));
    }

    private bool HasValidReplacedPaidHpContinuation(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.ReplacedPaidHpContinuation is not { } receipt ||
            !DyingOwnerSkillReplacementContract.IsPaidOwnerDrawTail(plan.Instructions) ||
            receipt.ProgramFrameId != frame.Id || receipt.OwnerSeat != frame.OwnerSeat ||
            receipt.SkillId != frame.SkillId || receipt.BindingId != GetProgramBindingId(frame) ||
            receipt.SkillInstanceId != frame.SkillInstanceId || receipt.GameplayHash != frame.GameplayHash ||
            receipt.PaidInstructionIndex != 1 || frame.InstructionIndex is < 1 or > 2 || frame.ReexecuteParticipantInstruction ||
            receipt.DrawAmount != ((FixedProgramAmount)((DrawProgramInstruction)plan.Instructions[1].CompiledInstruction!).Amount).Value ||
            receipt.ActualTurnNumber != _turnNumber || receipt.ActualTurnOwnerSeat != _currentSeat ||
            receipt.DyingFrameId <= frame.Id || receipt.EntryWindowFrameId <= receipt.DyingFrameId ||
            receipt.ReplacementFrameId <= receipt.EntryWindowFrameId) return false;
        var replacementProgram = _contentRegistry.GetSkill(receipt.ReplacementSkillId).Program;
        var trigger = replacementProgram?.Triggers.SingleOrDefault(item => item.Id == receipt.ReplacementBindingId);
        if (replacementProgram?.GameplayHash != receipt.ReplacementGameplayHash ||
            trigger is not { Window: SkillProgramTriggerWindow.DyingEntering, Subject: SkillProgramTriggerSubject.Owner,
                Optional: false, UsageScope: SkillUsageScope.Game, UsageLimit: 1 } ||
            receipt.ReplacementInstructionIndex != trigger.Effects.Count ||
            trigger.Effects[^1] is not { Op: SkillProgramEffectOp.LoseOwnerSkillsAndGrant,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } replacement ||
            replacement.SourceBind != receipt.GrantedSkillId ||
            !replacement.SkillIds.Contains(frame.SkillId, StringComparer.Ordinal) ||
            replacement.SkillIds.Contains(receipt.GrantedSkillId, StringComparer.Ordinal)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var dying = _resolutionStack.OfType<DyingFrame>().SingleOrDefault(item => item.Id == receipt.DyingFrameId);
        if (dying is not null
                ? dying.ParentFrameId != frame.Id || dying.VictimSeat != frame.OwnerSeat ||
                  dying.Continuation != DyingContinuationKind.ProgramSkill || frame.InstructionIndex != 1
                : history.OfType<DyingResolvedEvent>().Count(fact => fact.ResolutionId == receipt.DyingFrameId &&
                    fact.VictimSeat == frame.OwnerSeat) != 1)
            return false;
        var started = frame.TriggerId is null
            ? history.OfType<ProgramSkillStartedEvent>().Count(fact => fact.FrameId == frame.Id &&
                fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == frame.SkillId && fact.ActivationId == frame.ActivationId)
            : history.OfType<ProgramBindingStartedEvent>().Count(fact => fact.FrameId == frame.Id &&
                fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == frame.SkillId && fact.BindingId == frame.TriggerId &&
                fact.SkillInstanceId == frame.SkillInstanceId && fact.Window == frame.WindowContext?.Window);
        return started == 1 && history.OfType<ProgramReplacedPaidHpContinuationIssuedEvent>()
                .Count(fact => fact.Continuation.ProgramFrameId == frame.Id && fact.Continuation == receipt) == 1 &&
            history.OfType<ProgramSkillHpLostEvent>().Count(fact => fact.FrameId == frame.Id && fact.SkillId == frame.SkillId &&
                fact.TargetSeat == frame.OwnerSeat && fact.Amount == 1 && fact.RemainingHp == 0) == 1 &&
            history.OfType<PlayerDyingEvent>().Count(fact => fact.ResolutionId == receipt.DyingFrameId &&
                fact.VictimSeat == frame.OwnerSeat && fact.KillerSeat is null) == 1 &&
            history.OfType<ProgramBindingStartedEvent>().Count(fact => fact.FrameId == receipt.ReplacementFrameId &&
                fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == receipt.ReplacementSkillId &&
                fact.BindingId == receipt.ReplacementBindingId && fact.SkillInstanceId == receipt.ReplacementSkillInstanceId &&
                fact.Window == SkillProgramTriggerWindow.DyingEntering) == 1 &&
            history.OfType<ProgramOwnerSkillsReplacedEvent>().Count(fact => fact.ResolutionId == receipt.ReplacementFrameId &&
                fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == receipt.ReplacementSkillId &&
                fact.GrantedSkillId == receipt.GrantedSkillId && fact.LostSkillIds.SequenceEqual(replacement.SkillIds)) == 1;
    }

    private void AssertReplacedPaidHpContinuation(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.ReplacedPaidHpContinuation is not null && !HasValidReplacedPaidHpContinuation(frame, plan))
            throw new InvalidOperationException("The removed owner's paid HP continuation lost its exact source, cursor or issued facts.");
    }

    private bool CanContinueReplacedPaidHpContinuation(ProgramSkillFrame frame) =>
        _winner == Winner.None && _players[frame.OwnerSeat].IsAlive &&
        HasValidReplacedPaidHpContinuation(frame, ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry.GetSkill(frame.SkillId).Program!));

    private sealed partial class ProgramSkillHost
    {
        public bool CanContinueReplacedPaidHpContinuation(ProgramSkillFrame frame) =>
            engine.CanContinueReplacedPaidHpContinuation(frame);
    }
}
