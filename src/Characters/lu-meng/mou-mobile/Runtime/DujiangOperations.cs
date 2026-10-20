using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouLuMengDujiangReceipt(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase);
public sealed record MouLuMengDujiangAwakenedEvent(long FrameId, int OwnerSeat, CardConversionSource Source,
    string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int Armor) : IGameEvent;
internal sealed class ResolveMouLuMengDujiangDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuMengDujiang;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuMengHandler(SkillProgramEffectOp.ResolveMouLuMengDujiang);
}
