using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ICaoShuangProgramHost
{
    SkillProgramStepOutcome InheritDeadGeneralChosenSkill(ProgramSkillFrame frame);
    SkillProgramStepOutcome PlaceDamagedCardAsDelayedTrick(ProgramSkillFrame frame);
}
internal sealed class CaoShuangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.InheritDeadGeneralChosenSkill ? host.GetCapability<ICaoShuangProgramHost>().InheritDeadGeneralChosenSkill(f)
            : host.GetCapability<ICaoShuangProgramHost>().PlaceDamagedCardAsDelayedTrick(f);
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoShuangTuoguReceipt? CaoShuangTuogu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoShuangShanzhuanReceipt? CaoShuangShanzhuan { get; init; }
}
