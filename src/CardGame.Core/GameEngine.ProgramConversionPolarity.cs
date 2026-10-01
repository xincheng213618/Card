namespace CardGame.Core;

public sealed record ProgramConversionPolarityCommittedEvent(long FrameId, long? ParentFrameId,
    int OwnerSeat, string SkillId, string SkillInstanceId, string BindingId,
    SkillPolarity PreviousState, SkillPolarity CurrentState, int TurnNumber, int PhaseInstanceId) : IGameEvent;
internal interface IConversionPolarityProgramHost { void CommitConversionPolarity(ProgramSkillFrame frame); }
internal sealed class CommitConversionPolarityDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CommitConversionPolarity;
    public override bool UsesConversionPolarity => true;
    public override ISkillProgramEffectHandler Handler { get; } = new CommitConversionPolarityHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ToggleBooleanState, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
public sealed class CommitConversionPolarityHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.CommitConversionPolarity;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    { ((IConversionPolarityProgramHost)host).CommitConversionPolarity(frame); return SkillProgramStepOutcome.Continue; }
}
public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IConversionPolarityProgramHost
    { public void CommitConversionPolarity(ProgramSkillFrame frame) => engine.CommitProgramConversionPolarity(frame); }
    private SkillPolarity GetProgramConversionPolarity(int ownerSeat, string skillId) =>
        _skillRuntimeState.GetConversionState(ownerSeat, skillId);
    private SkillPolarity CommitProgramConversionPolarity(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.ConversionPreviousPolarity is { } committed) return committed;
        var previous = GetProgramConversionPolarity(active.OwnerSeat, active.SkillId);
        var parentId = _resolutionStack.Count > 1 ? (long?)_resolutionStack[^2].Id : null;
        ReplaceRuntimeTop(active with { ConversionPreviousPolarity = previous });
        var current = _skillRuntimeState.ToggleConversionState(active.OwnerSeat, active.SkillId);
        AdvanceEventRulesAndQueueFact(new ProgramConversionPolarityCommittedEvent(active.Id, parentId,
            active.OwnerSeat, active.SkillId, active.SkillInstanceId, GetProgramBindingId(active), previous,
            current, _turnNumber, _cardUseDebitPhaseInstanceId));
        return previous;
    }
}
