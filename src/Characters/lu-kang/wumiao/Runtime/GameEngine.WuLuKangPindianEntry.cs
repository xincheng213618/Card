using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WuLuKangPindianSelectionAdmission(long FrameId, CardConversionSource ProgramSource,
    string ProgramHash, CardConversionSource CapabilitySource, string CapabilityHash,
    ActualDiscardRecoveryPhaseKey Phase);
public sealed record WuLuKangPindianSelectionAdmittedEvent(WuLuKangPindianSelectionAdmission Admission) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WuLuKangPindianSelectionAdmission? WuLuKangPindianSelection { get; init; }
}

public sealed partial class GameEngine
{
    // A selected contest card is its Pindian source, not an additional cost.
    // All other activations retain their declared physical-card requirements.
    private static bool IsWuLuKangSelectedPindianSource(SkillProgramActivation a) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_kang_wumiao_WuLuKangPindianEntry.IsWuLuKangSelectedPindianSource(a);

    private bool WuLuKangCanOmitPindianSource(int owner, SkillProgramActivation activation) =>
        IsWuLuKangSelectedPindianSource(activation) && CanWuLuKangDiscardForPindian(_players[owner]);

    private bool WuLuKangActivationTargetHasPindianSource(ProgramExecutionPlan plan, CharacterState target) =>
        GetHand(target).Count > 0 ||
        (plan.Features.HasOperation(SkillProgramEffectOp.Pindian) || plan.Features.HasOperation(SkillProgramEffectOp.StartPindian) ||
         plan.Features.HasOperation(SkillProgramEffectOp.StartSimultaneousPindian) ||
         plan.Features.HasOperation(SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian)) &&
        CanWuLuKangDiscardForPindian(target);

    private static bool IsWuLuKangPindianTargetInstruction(IReadOnlyList<SkillProgramEffect> instructions, int index) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_kang_wumiao_WuLuKangPindianEntry.IsWuLuKangPindianTargetInstruction(instructions,index);

    private IReadOnlyList<int> WuLuKangProgramSelectionSeats(int owner, SkillProgramEffect selection,
        IReadOnlyList<SkillProgramEffect> instructions, ProgramSkillWindowContext? context = null)
    {
        var seats = GetProgramTargetSeats(owner, selection.TargetKind!.Value, context, selection.Marker, selection.ActorReference);
        if (!instructions.Select((e, i) => (e, i)).Any(x => ReferenceEquals(x.e, selection) &&
                IsWuLuKangPindianTargetInstruction(instructions, x.i))) return seats;
        return seats.Concat(_players.Where(p => p.IsAlive && p.Seat != owner && CanWuLuKangDiscardForPindian(p) &&
            CanBePindianTarget(owner, p.Seat)).Select(p => p.Seat)).Distinct().Order().ToArray();
    }

    private IReadOnlyList<int> WuLuKangProgramSelectionSeats(ProgramSkillFrame frame, SkillProgramTargetKind kind,
        IReadOnlyList<CardZoneKind> zones, PlayerMarkerKind? marker, ProgramParticipantReference? actor)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var index = frame.InstructionIndex - 1;
        return IsWuLuKangPindianTargetInstruction(plan.Instructions, index) && zones.Count == 0 &&
            plan.Instructions[index].TargetKind == kind && plan.Instructions[index].Marker == marker &&
            plan.Instructions[index].ActorReference == actor
            ? WuLuKangProgramSelectionSeats(frame.OwnerSeat, plan.Instructions[index], plan.Instructions, frame.WindowContext)
            : GetProgramTargetSeats(frame.OwnerSeat, kind, frame.WindowContext, marker, actor);
    }

    private bool WuLuKangProgramSelectionTargetEligible(ProgramSkillFrame frame, SkillProgramTargetKind kind,
        IReadOnlyList<CardZoneKind> zones, int seat, PlayerMarkerKind? marker, ProgramParticipantReference? actor) =>
        IsProgramTargetEligible(frame.OwnerSeat, kind, zones, seat, marker, frame.WindowContext, actor) ||
        zones.Count == 0 && WuLuKangProgramSelectionSeats(frame, kind, zones, marker, actor).Contains(seat);

    private ProgramSkillFrame CaptureWuLuKangPindianSelection(ProgramSkillFrame f, SkillProgramActivation activation)
    {
        if (f.SelectedCardIds.Count != 0 || !IsWuLuKangSelectedPindianSource(activation)) return f;
        if (!WuLuKangCanOmitPindianSource(f.OwnerSeat, activation) || f.TriggerId is not null ||
            f.InstructionIndex != 0 || f.WuLuKangPindianSelection is not null ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase ||
            phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("A cardless Pindian source requires its original eligible maximum-deck policy.");
        var source = CardPolicies(_players[f.OwnerSeat], SkillProgramCardPolicyKind.PindianDiscardForMaximumDeck)
            .First(item => item.Source.SkillId == WuLuKangComposition.Shenduan && WuLuKangComposition.IsShenduan(item.Source.Program));
        var admission = new WuLuKangPindianSelectionAdmission(f.Id,
            new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            new(source.Source.SkillId, source.Policy.Id, f.OwnerSeat, source.Source.SkillInstanceId),
            source.Source.Program.GameplayHash, phase);
        return f with { WuLuKangPindianSelection = admission };
    }

    private void IssueWuLuKangPindianSelection(ProgramSkillFrame f)
    {
        if (f.WuLuKangPindianSelection is not { } admission) return;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != f.Id ||
            current.WuLuKangPindianSelection != admission ||
            ProgramEventHistory<WuLuKangPindianSelectionAdmittedEvent>().Any(e => e.Admission.FrameId == f.Id))
            throw new InvalidOperationException("Pindian source admission belongs to its one original native activation.");
        AdvanceEventRulesAndQueueFact(new WuLuKangPindianSelectionAdmittedEvent(admission));
    }

    private bool HasWuLuKangPindianSelection(ProgramSkillFrame f, SkillProgramActivation a) =>
        f.WuLuKangPindianSelection is { } admission && f.TriggerId is null &&
        f.SelectedCardIds.Count == 0 && IsWuLuKangSelectedPindianSource(a) &&
        admission.FrameId == f.Id && admission.ProgramSource == new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) &&
        admission.ProgramHash == f.GameplayHash && admission.CapabilitySource.OwnerSeat == f.OwnerSeat &&
        admission.CapabilitySource.SkillId == WuLuKangComposition.Shenduan &&
        admission.CapabilitySource.BindingId == "maximum-deck-source" &&
        admission.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && admission.Phase.ActorSeat == f.OwnerSeat &&
        IsRecordedActualDiscardRecoveryPhase(admission.Phase) &&
        _contentRegistry.GetSkill(WuLuKangComposition.Shenduan).Program is { } p &&
        p.GameplayHash == admission.CapabilityHash && WuLuKangComposition.IsShenduan(p) &&
        ProgramEventHistory<WuLuKangPindianSelectionAdmittedEvent>().Count(e => e.Admission == admission) == 1 &&
        ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
            e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) == 1;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 29703)]
    private void AssertWuLuKangPindianSelection(ProgramSkillFrame f)
    {
        if (f.WuLuKangPindianSelection is null) return;
        var program = _contentRegistry.GetSkill(f.SkillId).Program;
        if (program is null || program.GameplayHash != f.GameplayHash ||
            program.Activations.SingleOrDefault(a => a.Id == f.ActivationId) is not { } activation ||
            !HasWuLuKangPindianSelection(f, activation))
            throw new InvalidOperationException("A cardless Pindian source lost its accepted, source-bound original activation.");
    }
}
