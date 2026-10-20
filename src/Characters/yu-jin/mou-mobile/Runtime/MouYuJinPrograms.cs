using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouYuJinXiayuanReceipt? MouYuJinXiayuan { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouYuJinJieyueReceipt? MouYuJinJieyue { get; init; }
}

internal interface IMouYuJinHost
{
    SkillProgramStepOutcome Xiayuan(ProgramSkillFrame frame);
    SkillProgramStepOutcome Jieyue(ProgramSkillFrame frame);
}
internal sealed class MouYuJinHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveMouYuJinXiayuan ? host.GetCapability<IMouYuJinHost>().Xiayuan(f) : host.GetCapability<IMouYuJinHost>().Jieyue(f);
}
