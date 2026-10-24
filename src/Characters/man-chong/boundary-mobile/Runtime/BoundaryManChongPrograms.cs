using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IBoundaryManChongHost
{
    SkillProgramStepOutcome Choose(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class ResolveBoundaryManChongJunxingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryManChongJunxing;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveBoundaryManChongJunxingHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TurnOver, static (_, context) => context.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, reader.RequiredEnum<SkillProgramEffectTarget>("target"), 0,
            reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"), resultBind: reader.RequiredIdentifier("resultBind"));
        if (effect.Target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException("Junxing requires its owner placeholder.");
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!), new ReadSelectedTarget(), new CreateChoiceResult(effect.ResultBind!, ["discarded", "turned"])];
}

internal sealed class ResolveBoundaryManChongJunxingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryManChongJunxing;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IBoundaryManChongHost>().Choose(frame, effect);
}

public sealed record BoundaryManChongJunxingDraft(int InstructionIndex, int TargetSeat, int Count,
    string SourceBind, string ResultBind, bool Paid = false, long SequenceBefore = 0, long SequenceAfter = 0)
{
    private readonly IReadOnlyList<int> _selected = [];
    public IReadOnlyList<int> SelectedIds { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundaryManChongJunxingDraft? BoundaryManChongJunxing { get; init; }
}

public sealed record BoundaryManChongChoiceCommittedEvent(long FrameId, int InstructionIndex, string ResultBind,
    int TargetSeat, int Count, bool Discarded, long SequenceBefore, long SequenceAfter) : IGameEvent;
