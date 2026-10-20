using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29601, SkillProgramEffectOp.ResolveMouChenGongMingceRelease)]
    private bool CanOfferMouChenGongRelease(ProgramTriggerLegalityContext c) => c.SkillId == MouChenGongComposition.Mingce && c.Owner.IsAlive &&
        c.Context.Window == SkillProgramTriggerWindow.PlayPhaseStarting && c.Context.SourceSeat == c.OwnerSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat &&
        IsRecordedActualDiscardRecoveryPhase(phase) && c.Owner.Markers.GetValueOrDefault(PlayerMarkerKind.MouChenGongCe) > 0 && _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat);
    private bool MouChenGongReleaseParent(ProgramSkillFrame f)
    {
        if (f.SkillId != MouChenGongComposition.Mingce || f.TriggerId != "release-plan" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } c ||
            c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || c.TargetSeat != f.OwnerSeat ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash ||
            _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(f, candidate)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id;
    }
    private bool MouChenGongReleaseRoot(ProgramSkillFrame f) => f.MouChenGongRelease is { } r && MouChenGongReleaseParent(f) &&
        r.Source == MouChenGongSource(f) && r.GameplayHash == f.GameplayHash && r.WindowId == f.WindowContext!.ParentFrameId &&
        r.Phase.ActorSeat == f.OwnerSeat && r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && IsRecordedActualDiscardRecoveryPhase(r.Phase) &&
        ProgramEventHistory<MouChenGongReleaseStartedEvent>().Count(e => e == new MouChenGongReleaseStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.WindowId)) == 1;
    private SkillProgramStepOutcome BeginMouChenGongRelease(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouChenGongReleaseParent(f) || f.MouChenGongRelease is not null || !_players[f.OwnerSeat].IsAlive ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat ||
            !IsRecordedActualDiscardRecoveryPhase(phase) || _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MouChenGongCe) <= 0)
            throw new InvalidOperationException("Mingce release belongs to its original actual own Play-start boundary with available Ce.");
        var r = new MouChenGongReleaseReceipt(MouChenGongSource(f), f.GameplayHash, phase, f.WindowContext!.ParentFrameId);
        ReplaceRuntimeTop(f = f with { MouChenGongRelease = r });
        AdvanceEventRulesAndQueueFact(new MouChenGongReleaseStartedEvent(f.Id, r.Source, r.GameplayHash, phase, r.WindowId));
        PublishMouChenGong(f, f.OwnerSeat, MouChenGongReleaseChoices(f), "明策：选择一名其他角色，造成等同当前策数的伤害，然后移除所有策。");
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouChenGongReleaseChoices(ProgramSkillFrame f) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)
        .Select(p => new PromptChoice(new($"mou-chen-gong.release.{f.Id}.{p.Seat}"), $"对{p.Name}造成策数点伤害", [], [p.Seat], new Dictionary<string, string>
        { ["program-action"] = "mou-chen-gong-release", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    [ProgramAnswer("mou-chen-gong-release")]
    private void AnswerMouChenGongRelease(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouChenGongRelease!;
        if (!MouChenGongReleaseRoot(f) || r.Stage != MouChenGongReleaseStage.TargetChoice || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], MouChenGongReleaseChoices(f).Where(c => c.Id == selected.Id).ToArray()) ||
            selected.Targets is not [var target] || _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MouChenGongCe) is not > 0)
            throw new InvalidOperationException("Mingce release requires its one living other and a fresh positive accepted Ce snapshot.");
        var x = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MouChenGongCe); ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { MouChenGongRelease = r with { Stage = MouChenGongReleaseStage.Damage, TargetSeat = target, FrozenX = x } });
        AdvanceEventRulesAndQueueFact(new MouChenGongReleaseAcceptedEvent(f.Id, target, x));
        AdvanceEventRulesAndQueueFact(new MouChenGongReleaseDamageIssuedEvent(f.Id, f.OwnerSeat, target, x));
        BeginProgramSkillDamage(f, target, x);
    }
    private void ContinueMouChenGongRelease(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouChenGongRelease: { } r } f || f.Id != id) return;
        AssertMouChenGong(f);
        if (r.Stage == MouChenGongReleaseStage.TargetChoice || f.AttackAttempt is not null) return;
        if (DrainMouChenGongChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouChenGongRelease!;
        if (r.Stage == MouChenGongReleaseStage.Damage && !r.Cleared)
        {
            var count = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MouChenGongCe);
            ReplaceRuntimeTop(f = f with { MouChenGongRelease = r with { Stage = MouChenGongReleaseStage.ClearChildren, Cleared = true, Before = _movementSequence, After = _movementSequence } });
            if (count > 0) ChangeMouChenGongCe(f, -count);
            AdvanceEventRulesAndQueueFact(new MouChenGongCeClearedEvent(f.Id, f.OwnerSeat, r.FrozenX!.Value, count));
            ContinueMouChenGongRelease(id); return;
        }
        AdvanceEventRulesAndQueueFact(new MouChenGongReleaseCompletedEvent(f.Id, r.TargetSeat, r.FrozenX, r.Cleared));
        ReplaceRuntimeTop(f = f with { MouChenGongRelease = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    private void ChangeMouChenGongCe(ProgramSkillFrame f, int delta)
    {
        var owner = _players[f.OwnerSeat]; var marker = PlayerMarkerKind.MouChenGongCe; var before = owner.Markers.GetValueOrDefault(marker);
        var sources = owner.MarkerSourceCounts.Where(p => p.Key.Marker == marker).ToArray();
        if (sources.Sum(p => p.Value) != before || before + delta < 0 || delta == 0) throw new InvalidOperationException("Ce must retain its native participant-marker source totals.");
        if (delta > 0)
        {
            var key = (marker, f.OwnerSeat); owner.MarkerSourceCounts[key] = owner.MarkerSourceCounts.GetValueOrDefault(key) + delta;
            owner.Markers[marker] = before + delta;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, f.OwnerSeat, marker, delta, before + delta, f.OwnerSeat, "mou-mobile:mingce.plan"));
        }
        else
        {
            if (delta != -before) throw new InvalidOperationException("Mingce release clears all currently held Ce after its native damage returns.");
            var current = before;
            foreach (var source in sources.OrderBy(p => p.Key.SkillOwnerSeat))
            {
                owner.MarkerSourceCounts.Remove(source.Key); current -= source.Value;
                if (current == 0) owner.Markers.Remove(marker); else owner.Markers[marker] = current;
                AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, f.OwnerSeat, marker, -source.Value, current, source.Key.SkillOwnerSeat, "mou-mobile:mingce.plan"));
            }
        }
        AdvanceEventRulesAndQueueFact(new MouChenGongCeChangedEvent(f.Id, f.OwnerSeat, before, before + delta));
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouChenGongMingceRelease)]
    private PromptChoice AiMouChenGongRelease(PendingDecision pending, ProgramSkillFrame f)
    {
        var view = CreateSnapshot(f.OwnerSeat); var amount = _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MouChenGongCe);
        return pending.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets.Single(),
            new(0, 0, 0, 0, 0, 0, false, false) { TargetDamage = amount, TargetHealthEffects = [new(PublicHealthEffectKind.Damage, amount)] })).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
}
