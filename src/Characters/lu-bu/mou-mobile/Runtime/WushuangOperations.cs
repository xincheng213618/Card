using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouLuBuWushuangBonusEvent(long FrameId, CardConversionSource Source, long AttackId, long WindowId,
    int ActualTurn, int VictimSeat, CardKind Kind, int Before, int After) : IGameEvent;
internal sealed class MouLuBuWushuangBonusDescriptor : MouLuBuMobileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuBuWushuangBonus;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuBuWushuangBonusHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(3));
}
internal sealed class MouLuBuWushuangBonusHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuBuWushuangBonus;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IMouLuBuMobileHost>().Bonus(f);
}
