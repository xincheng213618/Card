using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLixianReceipt? MouLixian { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouBingcaiReceipt? MouBingcai { get; init; }
}

internal interface IMouHuangYueyingHost
{
    SkillProgramStepOutcome ResolveMouHuangYueyingLixian(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveMouHuangYueyingBingcai(ProgramSkillFrame frame);
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLixianNativeOrigin? MouLixianOrigin { get; init; }
}
