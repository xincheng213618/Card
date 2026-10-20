using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouHuaXiongYangweiReceipt? MouHuaXiongYangwei { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouHuaXiongYaowuReceipt? MouHuaXiongYaowu { get; init; }
}

internal interface IMouHuaXiongHost
{
    SkillProgramStepOutcome Yangwei(ProgramSkillFrame frame);
    SkillProgramStepOutcome Yaowu(ProgramSkillFrame frame);
}
internal sealed class MouHuaXiongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveMouHuaXiongYangwei ? host.GetCapability<IMouHuaXiongHost>().Yangwei(f) : host.GetCapability<IMouHuaXiongHost>().Yaowu(f);
}
