using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ICaoSongProgramHost
{
    SkillProgramStepOutcome ReplaceDrawWithLiluGift(ProgramSkillFrame frame);
    void GrantYizhengSupport(ProgramSkillFrame frame);
}

internal sealed class ReplaceDrawWithLiluGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceDrawWithLiluGift;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoSongHandler(SkillProgramEffectOp.ReplaceDrawWithLiluGift);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}

internal sealed class GrantYizhengSupportDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantYizhengSupport;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoSongHandler(SkillProgramEffectOp.GrantYizhengSupport);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
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

public enum CaoSongLiluStage { Drawing, Recipient, Cards, GiftChildren, MaximumChildren, RecoveryChildren }
public sealed record CaoSongLiluReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DrawWindowId, int FrozenHandLimit, CaoSongLiluStage Stage, int? RecipientSeat = null,
    int GiftCount = 0, int? PreviousGiftCount = null, bool Grows = false, int SequenceBefore = 0, int SequenceAfter = 0)
{
    private readonly IReadOnlyList<int> _selectedCardIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> SelectedCardIds
    {
        get => _selectedCardIds;
        init => _selectedCardIds = Array.AsReadOnly(value.ToArray());
    }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CaoSongLiluReceipt? CaoSongLilu { get; init; }
}

public sealed record CaoSongLiluGiftCommittedEvent(long ProgramFrameId, CardConversionSource Source,
    int RecipientSeat, int GiftCount, int? PreviousGiftCount, bool Grows,
    int SequenceBefore, int SequenceAfter) : IGameEvent;

public sealed record CaoSongYizhengGrantedEvent(long GrantId, CardConversionSource Source,
    int RecipientSeat, int IssuedTurn) : IGameEvent;
public sealed record CaoSongYizhengExpiredEvent(long GrantId, int OwnerSeat, int TurnNumber) : IGameEvent;
public sealed record CaoSongYizhengAmountPaidEvent(long ParentFrameId, long GrantId, int OwnerSeat,
    int BeneficiarySeat, bool Recovery, int MaximumHpBefore, int MaximumHpAfter) : IGameEvent;
public sealed record CaoSongYizhengAmountIncreasedEvent(long ParentFrameId, long GrantId,
    int BeneficiarySeat, bool Recovery, int Before, int After) : IGameEvent;

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
