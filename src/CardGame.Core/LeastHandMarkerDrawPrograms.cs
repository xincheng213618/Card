namespace CardGame.Core;

public sealed record ProgramMarkerExtraDrawPolicy(int TargetSeat, PlayerMarkerKind Marker,
    int SourceSeat, string SkillId, string SkillInstanceId, string BindingId);
public sealed record ProgramLeastHandMarkerDrawReceipt(int InstructionIndex, int TargetSeat);
public sealed record ProgramLeastHandMarkerOrDrawEvent(long FrameId, int OwnerSeat, int TargetSeat,
    string SkillId, string SkillInstanceId, PlayerMarkerKind Marker, bool GrantedMarker) : IGameEvent;
public sealed record ProgramMarkerExtraDrawConsumedEvent(long FrameId, int TargetSeat,
    PlayerMarkerKind Marker, int SourceSeat, string SkillId, string SkillInstanceId) : IGameEvent;

public interface ILeastHandMarkerDrawProgramHost
{
    SkillProgramStepOutcome GrantLeastHandMarkerOrDraw(ProgramSkillFrame frame, int targetSeat,
        PlayerMarkerKind marker);
}
public sealed class GrantLeastHandMarkerOrDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantLeastHandMarkerOrDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        ((ILeastHandMarkerDrawProgramHost)host).GrantLeastHandMarkerOrDraw(frame, targetSeat, effect.Marker!.Value);
}
internal sealed class GrantLeastHandMarkerOrDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantLeastHandMarkerOrDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantLeastHandMarkerOrDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.SelectedTarget, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "marker", "amount", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget || r.RequiredInt("amount") != 1)
            throw new InvalidOperationException("Least-hand marker benefits require one selected living target and exactly one marker/card.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 1, r.Condition(),
            marker: r.RequiredEnum<PlayerMarkerKind>("marker"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseSkipped), new ReadTargetSet(1, 1)];
}

public sealed partial class GameEngine
{
    // Issued permanent obligations are independent of later source availability;
    // resolution progress itself remains only on the exact draw owning frame.
    private readonly Dictionary<(int Target, PlayerMarkerKind Marker), ProgramMarkerExtraDrawPolicy> _markerExtraDrawPolicies = [];
    private sealed partial class ProgramSkillHost : ILeastHandMarkerDrawProgramHost
    {
        public SkillProgramStepOutcome GrantLeastHandMarkerOrDraw(ProgramSkillFrame frame, int targetSeat,
            PlayerMarkerKind marker) => engine.BeginLeastHandMarkerOrDraw(frame, targetSeat, marker);
    }
    private SkillProgramStepOutcome BeginLeastHandMarkerOrDraw(ProgramSkillFrame frame, int targetSeat, PlayerMarkerKind marker)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.LeastHandMarkerDraw is not null || frame.SelectedTargetSeats is not [var selected] || selected != targetSeat ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseSkipped } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Window != context.Window || parent.ResumeDrawPhaseObligationFrameId is null)
            throw new InvalidOperationException("A least-hand marker benefit lost its actual skipped-draw parent.");
        var owner = _players[frame.OwnerSeat]; var target = _players[targetSeat];
        if (_winner != Winner.None || !owner.IsAlive || !target.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var minimum = _players.Where(p => p.IsAlive).Min(p => GetHand(p).Count);
        var markerBranch = GetHand(target).Count == minimum && target.Markers.GetValueOrDefault(marker) == 0;
        if (markerBranch)
        {
            MutateParticipantMarker(frame, targetSeat, marker, 1);
            _markerExtraDrawPolicies[(targetSeat, marker)] = new(targetSeat, marker, frame.OwnerSeat,
                frame.SkillId, frame.SkillInstanceId, GetProgramBindingId(frame));
        }
        else
        {
            ReplaceRuntimeTop(frame with { LeastHandMarkerDraw = new(frame.InstructionIndex, targetSeat) });
            DrawCards(target, 1, true, new("program.least-hand-marker.draw"));
        }
        AdvanceEventRulesAndQueueFact(new ProgramLeastHandMarkerOrDrawEvent(frame.Id, frame.OwnerSeat,
            targetSeat, frame.SkillId, frame.SkillInstanceId, marker, markerBranch));
        if (markerBranch) return SkillProgramStepOutcome.Continue;
        AdvanceRuntimeProgram(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumeLeastHandMarkerDraw(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.LeastHandMarkerDraw is not { } receipt) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex != receipt.InstructionIndex || receipt.InstructionIndex < 1 ||
            receipt.InstructionIndex > plan.Instructions.Count || plan.Instructions[receipt.InstructionIndex - 1].Op != SkillProgramEffectOp.GrantLeastHandMarkerOrDraw ||
            frame.SelectedTargetSeats is not [var seat] || seat != receipt.TargetSeat)
            throw new InvalidOperationException("A least-hand draw lost its exact completed instruction and selected target.");
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        ReplaceRuntimeTop(frame with { LeastHandMarkerDraw = null });
        if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "截辎得牌孩子结清后，来源失效，剩余程序取消。"); return true; }
        return false;
    }
    private bool HasIssuedExtraDrawMarker(int seat) => _markerExtraDrawPolicies.Values.Any(policy =>
        policy.TargetSeat == seat && _players[seat].Markers.GetValueOrDefault(policy.Marker) > 0);
    private int IndependentIssuedDrawMarkerCount(CharacterState target, PlayerMarkerKind marker, int source) =>
        target.IsAlive && target.Markers.GetValueOrDefault(marker) > 0 &&
        _markerExtraDrawPolicies.TryGetValue((target.Seat, marker), out var policy) && policy.SourceSeat == source ? 1 : 0;

    private ProgramMarkerExtraDrawPolicy? ConsumeIssuedExtraDrawMarker(DrawPhaseObligationFrame frame)
    {
        var owner = _players[frame.OwnerSeat];
        var policy = _markerExtraDrawPolicies.Values.Where(p => p.TargetSeat == owner.Seat && owner.Markers.GetValueOrDefault(p.Marker) > 0)
            .OrderBy(p => p.Marker).ThenBy(p => p.SourceSeat).FirstOrDefault();
        if (policy is null) return null;
        var key = (policy.Marker, policy.SourceSeat);
        if (owner.MarkerSourceCounts.GetValueOrDefault(key) < 1)
            throw new InvalidOperationException("An issued extra-draw marker lost its exact source attribution.");
        _markerExtraDrawPolicies.Remove((owner.Seat, policy.Marker));
        var sourceLeft = owner.MarkerSourceCounts[key] - 1;
        if (sourceLeft == 0) owner.MarkerSourceCounts.Remove(key); else owner.MarkerSourceCounts[key] = sourceLeft;
        var total = owner.Markers[policy.Marker] - 1;
        if (total == 0) owner.Markers.Remove(policy.Marker); else owner.Markers[policy.Marker] = total;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frame.Id, owner.Seat, policy.Marker, -1, total,
            policy.SourceSeat, "program.marker-extra-draw.consume"));
        AdvanceEventRulesAndQueueFact(new ProgramMarkerExtraDrawConsumedEvent(frame.Id, owner.Seat, policy.Marker,
            policy.SourceSeat, policy.SkillId, policy.SkillInstanceId));
        return policy;
    }
    private bool IsBeforeActualDamageDistancePolicyQualified(CharacterState owner) =>
        !EventsSinceLastBoundary(e => e is TurnStartedEvent).OfType<DamageAppliedEvent>()
            .Any(e => !e.SourceLess && e.SourceSeat == owner.Seat && e.Amount > 0);
}
