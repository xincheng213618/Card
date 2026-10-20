using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ICaoSongProgramHost
{
    SkillProgramStepOutcome ReplaceDrawWithLiluGift(ProgramSkillFrame frame);
    void GrantYizhengSupport(ProgramSkillFrame frame);
}

internal sealed class CaoSongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        var capability = host.GetCapability<ICaoSongProgramHost>();
        if (op == SkillProgramEffectOp.ReplaceDrawWithLiluGift) return capability.ReplaceDrawWithLiluGift(frame);
        capability.GrantYizhengSupport(frame); return SkillProgramStepOutcome.Continue;
    }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaoSongLiluReceipt? CaoSongLilu { get; init; }
}

public sealed record CaoSongAmountSupportReceipt(int BeneficiarySeat, int OriginalAmount, int NextGrantIndex = 0,
    bool WaitingForMaximumChildren = false, bool Complete = false)
{
    public int? HpBeforeApplication { get; init; }
    private readonly IReadOnlyList<CaoSongYizhengGrantedEvent> _grants = Array.AsReadOnly(Array.Empty<CaoSongYizhengGrantedEvent>());
    public IReadOnlyList<CaoSongYizhengGrantedEvent> Grants
    {
        get => _grants;
        init => _grants = Array.AsReadOnly(value.ToArray());
    }
}
public sealed partial record RecoveryReplacementFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaoSongAmountSupportReceipt? CaoSongRecoverySupport { get; init; }
}
public sealed partial record BeforeDamageProgramWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaoSongAmountSupportReceipt? CaoSongDamageSupport { get; init; }
}
