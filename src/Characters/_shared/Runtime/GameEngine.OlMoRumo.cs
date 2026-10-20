using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OlMoRumoSkill = "ol:mo-rumo";
    private const string OlMoRumoBinding = "no-damage-round-ending";
    private bool HasOlMoRumoCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveOlMoRumoRoundPenalty);
    private static string OlMoRumoInstance(long entryId) => "ol:mo-rumo@" + entryId.ToString(CultureInfo.InvariantCulture);

    // Entry is synchronous. The issuing operation owns all its cost children
    // and must reach its exact already-paid entering stage before this call.
    private void EnterOlMoRumo(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!HasOlMoRumoCapability || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            !(IsPaidQiangangRumoEntry(frame) || IsPaidMoKuangmoEntry(frame) || IsPaidMoDcQingshiEntry(frame)))
            throw new InvalidOperationException("OL Rumo requires the exact paid Qiangang entry, not a generic operation or a live skill label.");
        if (OlMoRumoEntry(frame.OwnerSeat) is not null) return;
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        if (!(IsPaidQiangangRumoOrigin(frame.Id, source, frame.GameplayHash) || IsPaidMoKuangmoOrigin(frame.Id, source, frame.GameplayHash) || IsPaidMoDcQingshiOrigin(frame.Id, source, frame.GameplayHash)))
            throw new InvalidOperationException("OL Rumo lost the original irreversible entry payment.");
        AdvanceEventRulesAndQueueFact(new OlMoRumoEnteredEvent(frame.Id, frame.OwnerSeat, source,
            frame.GameplayHash, _roundNumber, _turnNumber, _turnProgression.OwnerSeat));
    }

    private bool IsOlMoRumoEntered(int ownerSeat) => OlMoRumoEntry(ownerSeat) is not null;
    private PlayerMarkerSnapshot? OlMoRumoMarker(int ownerSeat) => IsOlMoRumoEntered(ownerSeat)
        ? new(PlayerMarkerKind.OlMoRumo, "入魔", 1) : null;

    private IReadOnlyList<PlayerMarkerSnapshot>? AppendOlMoRumoMarker(int ownerSeat,
        IReadOnlyList<PlayerMarkerSnapshot>? ordinary)
    {
        var result = OlMoRumoMarker(ownerSeat) is { } marker
            ? Array.AsReadOnly((ordinary ?? []).Where(item => item.Kind != PlayerMarkerKind.OlMoRumo).Append(marker)
                .OrderBy(item => item.Kind).ToArray()) : ordinary;
        return AppendMoLuBuKuangMarker(ownerSeat, result);
    }

    [ProgramPipelineHook(ProgramPipelinePhase.IncludeTriggerCandidate, 1)]
    private IReadOnlyList<ProgramTriggerCandidate> IncludeOlMoRumoRoundCandidates(CharacterState owner,
        SkillProgramTriggerWindow window, IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        // This is an issued derived state, never a fourth printed skill and
        // never a fresh grant inferred from the current general or its label.
        var remaining = ordinary.Where(candidate => candidate.SkillId != OlMoRumoSkill).ToArray();
        if (!HasOlMoRumoCapability || window != SkillProgramTriggerWindow.RoundEnding || !owner.IsAlive ||
            OlMoRumoEntry(owner.Seat) is not { } entry ||
            _contentRegistry.GetSkill(OlMoRumoSkill).Program is not { } program)
            return remaining.Length == ordinary.Count ? ordinary : Array.AsReadOnly(remaining);
        var candidate = new ProgramTriggerCandidate(owner.Seat, OlMoRumoSkill, OlMoRumoBinding,
            OlMoRumoInstance(entry.EntryId), program.GameplayHash, 0);
        return Array.AsReadOnly(remaining.Append(candidate).ToArray());
    }

    private bool IsIssuedOlMoRumoCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        HasOlMoRumoCapability && IsValidPlayerSeat(candidate.OwnerSeat) && _players[candidate.OwnerSeat].IsAlive &&
        OlMoRumoCandidateSource(candidate) && IsExactRoundEndingCandidate(candidate, context) &&
        !ProgramEventHistory<OlMoRumoRoundStartedEvent>()
            .Any(fact => fact.OwnerSeat == candidate.OwnerSeat && fact.RoundNumber == _roundNumber);

    [ProgramCapability]
    private sealed class OlMoRumoCapability(GameEngine engine) : IOlMoRumoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.ResolveOlMoRumoRoundPenalty(frame); }

    private SkillProgramStepOutcome ResolveOlMoRumoRoundPenalty(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.OlMoRumo is not null) { ContinueOlMoRumo(frame.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (frame.InstructionIndex != 1 || frame.WindowContext is not { } context ||
            !IsIssuedOlMoRumoCandidate(OlMoRumoCandidate(frame), context) ||
            _resolutionStack.FirstOrDefault() is not RoundEndingBoundaryFrame boundary ||
            OlMoRumoEntry(frame.OwnerSeat) is not { } entry)
            throw new InvalidOperationException("OL Rumo lost its exact entered owner and old-round boundary.");
        // Read the entire current round at this effect's real start. Entry does
        // not erase earlier damage, and earlier round-end children can deal it.
        var hadDamage = EventsSinceLastBoundary(item => item is RoundStartedEvent)
            .OfType<DamageAppliedEvent>().Any(item => item.Amount > 0 && !item.SourceLess && item.SourceSeat == frame.OwnerSeat);
        var receipt = new OlMoRumoReceipt(new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId),
            frame.GameplayHash, entry.EntryId, boundary.Id, boundary.RoundNumber, boundary.ActualTurnNumber,
            boundary.OwnerSeat, frame.InstructionIndex, hadDamage)
        { Stage = hadDamage ? OlMoRumoStage.LossChildren : OlMoRumoStage.LossIssuing };
        ReplaceRuntimeTop(frame = frame with { OlMoRumo = receipt });
        AdvanceEventRulesAndQueueFact(new OlMoRumoRoundStartedEvent(frame.Id, entry.EntryId, boundary.Id,
            frame.OwnerSeat, receipt.RoundNumber, receipt.ActualTurn, receipt.TurnOwner, hadDamage));
        ContinueOlMoRumo(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainOlMoRumo(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginAdvancedSkillsChanged(frame.Id) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(frame.Id);

    private void ContinueOlMoRumo(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlMoRumo: { } receipt } frame ||
            frame.Id != frameId || _pendingDecision is not null) return;
        AssertOlMoRumo(frame);
        if (receipt.Stage == OlMoRumoStage.LossIssuing)
        {
            if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None)
            { CompleteOlMoRumo(frame); return; }
            new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, frame.OwnerSeat, 1);
            if (_resolutionStack.LastOrDefault()?.Id != frameId) return;
            frame = GetActiveProgramFrame(frameId);
            if (frame.OlMoRumo!.Stage == OlMoRumoStage.LossIssuing)
                throw new InvalidOperationException("OL Rumo's native HP deduction lost its once-only return.");
        }
        if (DrainOlMoRumo(frame)) return;
        CompleteOlMoRumo(GetActiveProgramFrame(frameId));
    }

    private void CompleteOlMoRumo(ProgramSkillFrame frame)
    {
        AssertOlMoRumo(frame);
        var receipt = frame.OlMoRumo!;
        AdvanceEventRulesAndQueueFact(new OlMoRumoRoundCompletedEvent(frame.Id, receipt.EntryId, receipt.BoundaryId,
            frame.OwnerSeat, receipt.RoundNumber, receipt.HadRoundDamage, receipt.ActualLoss, receipt.AppearancePrevented));
        ReplaceRuntimeTop(frame = frame with { OlMoRumo = null });
        FinishProgramSkill(frame, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 28940)]
    private bool ResumeOlMoRumo(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { OlMoRumo: not null } frame || frame.Id != frameId) return false;
        ContinueOlMoRumo(frameId); return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28940)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28940)]
    private bool ContinueIssuedOlMoRumo(ProgramSkillFrame frame)
    {
        if (frame.OlMoRumo is not null) { AssertOlMoRumo(frame); return true; }
        return frame.InstructionIndex is 0 or 1 && frame.WindowContext is { } context &&
            IsIssuedOlMoRumoCandidate(OlMoRumoCandidate(frame), context);
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 28940)]
    private void ObserveOlMoRumoLoss(IGameEvent payload)
    {
        if (payload is ProgramSkillHpLostEvent fact && _resolutionStack.LastOrDefault() is ProgramSkillFrame
            { OlMoRumo: { Stage: OlMoRumoStage.LossIssuing, HadRoundDamage: false, LossReturned: false } receipt } frame &&
            fact.FrameId == frame.Id && fact.SkillId == OlMoRumoSkill && fact.TargetSeat == frame.OwnerSeat &&
            fact.Amount is >= 0 and <= 1 && fact.RemainingHp >= 0)
        {
            ReplaceRuntimeTop(frame with { OlMoRumo = receipt with { Stage = OlMoRumoStage.LossChildren,
                LossReturned = true, ActualLoss = fact.Amount, HpAfter = fact.RemainingHp } });
            AdvanceEventRulesAndQueueFact(new OlMoRumoHpLossAppliedEvent(frame.Id, frame.OwnerSeat, 1,
                fact.Amount, fact.RemainingHp, false));
        }
        else if (payload is IdentityHpDeductionPreventedEvent prevented &&
            _resolutionStack.LastOrDefault() is IdentityAppearanceFrame appearance && appearance.Id == prevented.AppearanceFrameId &&
            appearance.Receipt is { Entry: IdentityAppearanceEntry.ProgramHpLoss, ParentFrameId: { } parent } native &&
            _resolutionStack.SingleOrDefault(item => item.Id == parent) is ProgramSkillFrame
            { OlMoRumo: { Stage: OlMoRumoStage.LossIssuing, HadRoundDamage: false, LossReturned: false } receipt2 } frame2 &&
            native.OwnerSeat == frame2.OwnerSeat && native.OriginalInstructionIndex == frame2.InstructionIndex &&
            prevented.RequestedDeduction == 1)
        {
            ReplaceRuntimeFrame(frame2.Id, frame2 with { OlMoRumo = receipt2 with { Stage = OlMoRumoStage.LossChildren,
                LossReturned = true, AppearancePrevented = true, HpAfter = _players[frame2.OwnerSeat].Hp } });
            AdvanceEventRulesAndQueueFact(new OlMoRumoHpLossAppliedEvent(frame2.Id, frame2.OwnerSeat, 1, 0,
                _players[frame2.OwnerSeat].Hp, true));
        }
    }
}
