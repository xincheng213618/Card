using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuMengKejiReceipt? MouLuMengKeji { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuMengDujiangReceipt? MouLuMengDujiang { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuMengDuojingReceipt? MouLuMengDuojing { get; init; }
}

internal interface IMouLuMengHost
{
    SkillProgramStepOutcome Keji(ProgramSkillFrame frame);
    SkillProgramStepOutcome Dujiang(ProgramSkillFrame frame);
    SkillProgramStepOutcome Duojing(ProgramSkillFrame frame);
}
internal sealed class MouLuMengHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveMouLuMengKeji => host.GetCapability<IMouLuMengHost>().Keji(f),
        SkillProgramEffectOp.ResolveMouLuMengDujiang => host.GetCapability<IMouLuMengHost>().Dujiang(f),
        _ => host.GetCapability<IMouLuMengHost>().Duojing(f)
    };
}
