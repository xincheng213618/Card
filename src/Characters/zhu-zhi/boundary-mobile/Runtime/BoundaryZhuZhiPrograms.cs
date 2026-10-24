using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IBoundaryZhuZhiHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal abstract class BoundaryZhuZhiDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new BoundaryZhuZhiHandler(Op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.BeginBoundaryZhuZhiRescue
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.Recover, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.BeginBoundaryZhuZhiRescue => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering), new SelectSingleTarget()],
        SkillProgramEffectOp.PayBoundaryZhuZhiAnguo => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering), new ReadSelectedTarget()],
        _ => [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.PlayPhaseStarting])]
    };
}
internal sealed class ResolveBoundaryZhuZhiAnguoDescriptor : BoundaryZhuZhiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryZhuZhiAnguo; }
internal sealed class BeginBoundaryZhuZhiRescueDescriptor : BoundaryZhuZhiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.BeginBoundaryZhuZhiRescue; }
internal sealed class PayBoundaryZhuZhiAnguoDescriptor : BoundaryZhuZhiDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayBoundaryZhuZhiAnguo; }
internal sealed class BoundaryZhuZhiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IBoundaryZhuZhiHost>().Resolve(frame, operation);
}

internal static class BoundaryZhuZhiComposition
{
    internal const string Skill = "boundary:anguo-mobile";
    [ProgramCatalogValidation(53200)]
    internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.ResolveBoundaryZhuZhiAnguo or SkillProgramEffectOp.BeginBoundaryZhuZhiRescue or SkillProgramEffectOp.PayBoundaryZhuZhiAnguo)) return;
        if (program.Id != Skill || program.Activations.Count != 0 || program.Triggers is not
            [{ Id:"initial-mark", Window:SkillProgramTriggerWindow.GameStarting, Subject:SkillProgramTriggerSubject.Owner, Optional:false,
                Effects:[{Op:SkillProgramEffectOp.ResolveBoundaryZhuZhiAnguo}] },
             { Id:"move-mark", Window:SkillProgramTriggerWindow.PlayPhaseStarting, Subject:SkillProgramTriggerSubject.Owner, Optional:true,
                Effects:[{Op:SkillProgramEffectOp.ResolveBoundaryZhuZhiAnguo}] },
             { Id:"prevent-lethal", Window:SkillProgramTriggerWindow.BeforeDamageApplied, Subject:SkillProgramTriggerSubject.DamageTarget, Optional:false,
                Effects:[{Op:SkillProgramEffectOp.PreventCurrentDamage}] },
             { Id:"rescue-marked", Window:SkillProgramTriggerWindow.DyingEntering, Subject:SkillProgramTriggerSubject.Any, Optional:false,
                Effects:[{Op:SkillProgramEffectOp.BeginBoundaryZhuZhiRescue},
                  {Op:SkillProgramEffectOp.RecoverTo, Target:SkillProgramEffectTarget.SelectedTarget, NumberExpression:SkillProgramNumberExpression.IntegerConstant, MinimumValue:1, ClampToMaxHp:true},
                  {Op:SkillProgramEffectOp.PayBoundaryZhuZhiAnguo}, {Op:SkillProgramEffectOp.GrantArmor, Target:SkillProgramEffectTarget.SelectedTarget, Amount:1}] }])
            throw new InvalidOperationException(path + ": mobile boundary Anguo requires its original mark, phase move, lethal prevention and native recovery/payment/armor chain.");
    }
}

public enum BoundaryZhuZhiStage { MarkTarget, Recovery, PaymentChoice, Paying, PaymentChildren, Paid }
public sealed record BoundaryZhuZhiReceipt(CardConversionSource Source, string GameplayHash, long WindowId, string Binding,
    BoundaryZhuZhiStage Stage, int Victim = -1, long DyingId = 0)
{
    public string? Payment { get; init; }
    public int HpBefore { get; init; }
    public int MaximumBefore { get; init; }
    public int Requested { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundaryZhuZhiReceipt? BoundaryZhuZhi { get; init; }
}
public sealed record BoundaryZhuZhiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, string Binding, int Victim, long DyingId) : IGameEvent;
public sealed record BoundaryZhuZhiMarkEvent(long FrameId, CardConversionSource Source, int PreviousHolder, int Holder) : IGameEvent;
public sealed record BoundaryZhuZhiRescueIssuedEvent(long FrameId, int Victim, long DyingId, int RemovedMarks) : IGameEvent;
public sealed record BoundaryZhuZhiPaymentEvent(long FrameId, int Owner, string Choice, int HpBefore,
    int HpAfter, int MaximumBefore, int MaximumAfter) : IGameEvent;
