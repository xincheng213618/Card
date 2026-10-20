using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouLuBuLiyuStage { ChoosingCard, ObtainChildren, Drawing, DrawChildren, VictimChoice, DuelIssued, GrantChildren, Complete }
public sealed record MouLuBuLiyuReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long DamageFrameId,
    long OuterUseId, long OuterActionId, int VictimSeat, int DamageAmount, int ActualTurn)
{
    public MouLuBuLiyuStage Stage { get; init; }
    public int Selections { get; init; }
    public int ActualObtained { get; init; }
    public int CategoryMask { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int? DuelTarget { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuBuLiyuReceipt? MouLuBuLiyu { get; init; } }
public sealed record MouLuBuWushuangBonusEvent(long FrameId, CardConversionSource Source, long AttackId, long WindowId,
    int ActualTurn, int VictimSeat, CardKind Kind, int Before, int After) : IGameEvent;
public sealed record MouLuBuLiyuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long DamageFrameId, long OuterUseId, long OuterActionId, int VictimSeat, int DamageAmount, int ActualTurn) : IGameEvent;
public sealed record MouLuBuLiyuObtainedEvent(long FrameId, int Index, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long Sequence, bool ActualGain, int CategoryMask) : IGameEvent;
public sealed record MouLuBuLiyuDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual,
    int Replacement, int CategoryMask, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record MouLuBuLiyuMovementEvent(long FrameId, int CardId, CardKind Kind, CardLocation From,
    CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouLuBuLiyuBranchEvent(long FrameId, int ChooserSeat, string Branch, int? TargetSeat, int CategoryMask) : IGameEvent;
public sealed record MouLuBuLiyuGrantEvent(long FrameId, CardConversionSource Source, int RecipientSeat, string GrantId,
    string SkillInstanceId, int AfterTurnStartOrdinal) : IGameEvent;
public sealed record MouLuBuLiyuGrantExpiredEvent(string GrantId, int RecipientSeat, int ActualTurn) : IGameEvent;
public sealed record MouLuBuLiyuDuelIssuedEvent(long FrameId, long UseId, int ActorSeat, int TargetSeat) : IGameEvent;
public sealed record MouLuBuLiyuDuelReturnedEvent(long FrameId, long UseId, bool DamageApplied) : IGameEvent;
public sealed record MouLuBuLiyuCompletedEvent(long FrameId, int Selections, int ActualObtained, int CategoryMask) : IGameEvent;

internal interface IMouLuBuMobileHost
{ SkillProgramStepOutcome Bonus(ProgramSkillFrame f); SkillProgramStepOutcome Liyu(ProgramSkillFrame f); }
internal abstract class MouLuBuMobileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class MouLuBuWushuangBonusDescriptor : MouLuBuMobileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuBuWushuangBonus;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuBuWushuangBonusHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(3));
}
internal sealed class MouLuBuLiyuDescriptor : MouLuBuMobileDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuBuLiyu;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuBuLiyuHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.DamageTargetObtainValue());
}
internal sealed class MouLuBuWushuangBonusHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuBuWushuangBonus;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IMouLuBuMobileHost>().Bonus(f);
}
internal sealed class MouLuBuLiyuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuBuLiyu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IMouLuBuMobileHost>().Liyu(f);
}
