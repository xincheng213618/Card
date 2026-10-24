using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IBoundaryCaoXiuHost
{
    SkillProgramStepOutcome BeginQingxi(ProgramSkillFrame frame);
}

internal sealed class ResolveBoundaryCaoXiuQingxiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryCaoXiuQingxi;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveBoundaryCaoXiuQingxiHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PublicControlValue(3));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied), new RequireContext(ProgramContextCapability.Damage)];
}

internal sealed class ResolveBoundaryCaoXiuQingxiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryCaoXiuQingxi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IBoundaryCaoXiuHost>().BeginQingxi(frame);
}

public sealed record BoundaryCaoXiuQingxiDraft(long DamageWindowId, long AttackId, int TargetSeat, int Distance, int RequiredCount)
{
    private readonly IReadOnlyList<int> _selectedIds = [];
    public IReadOnlyList<int> SelectedIds { get => _selectedIds; init => _selectedIds = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundaryCaoXiuQingxiDraft? BoundaryCaoXiuQingxi { get; init; }
}

public sealed record BoundaryCaoXiuQingxiResolvedEvent(long FrameId, CardConversionSource Source, long AttackId,
    int TargetSeat, int Distance, int RequiredCount, bool Discarded, int DamageBefore, int DamageAfter) : IGameEvent;
