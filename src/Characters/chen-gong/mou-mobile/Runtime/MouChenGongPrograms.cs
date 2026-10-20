using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouChenGongGiftStage { GiftChoice, GiftChildren, RecipientChoice, LosingHp, LossChildren, Drawing, DrawChildren }
public enum MouChenGongReleaseStage { TargetChoice, Damage, ClearChildren }
public sealed record MouChenGongGiftPayment(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence);
public sealed record MouChenGongLossPayment(long ChangeId, int HpBefore, int HpAfter, int ActualAmount, bool IdentityPrevented = false);
public sealed record MouChenGongGiftReceipt(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat)
{
    public MouChenGongGiftStage Stage { get; init; }
    public MouChenGongGiftPayment? Gift { get; init; }
    public string? Branch { get; init; }
    public MouChenGongLossPayment? Loss { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool CeGained { get; init; }
}
public sealed record MouChenGongReleaseReceipt(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, long WindowId)
{
    public MouChenGongReleaseStage Stage { get; init; }
    public int? TargetSeat { get; init; }
    public int? FrozenX { get; init; }
    public bool Cleared { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed record MouChenGongZhichiProtection(CardConversionSource Source, string GameplayHash, long WindowId, long DamageFrameId,
    long AttackResolutionId, int ActualTurnNumber, int ActualTurnOwnerSeat, int ActualTurnOrdinal, int DamageTargetSeat,
    int DamageAmount, DamageNature Nature, bool SourceLess);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenGongGiftReceipt? MouChenGongGift { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenGongReleaseReceipt? MouChenGongRelease { get; init; }
}
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouChenGongZhichiProtection? MouChenGongProtection { get; init; } }
public sealed record MouChenGongMingceStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat) : IGameEvent;
public sealed record MouChenGongGiftPaidEvent(long FrameId, int RecipientSeat, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouChenGongBranchAcceptedEvent(long FrameId, int RecipientSeat, string Branch) : IGameEvent;
public sealed record MouChenGongLossIssuedEvent(long FrameId, int RecipientSeat, int RequestedAmount) : IGameEvent;
public sealed record MouChenGongLossReturnedEvent(long FrameId, int RecipientSeat, long ChangeId, int HpBefore, int HpAfter,
    int ActualAmount, bool IdentityPrevented) : IGameEvent;
public sealed record MouChenGongDrawReturnedEvent(long FrameId, int RecipientSeat, int RequestedCount, int ActualCount,
    int ReplacementCount, long SequenceBefore, long SequenceAfter, long? AttemptId) : IGameEvent;
public sealed record MouChenGongCeChangedEvent(long FrameId, int OwnerSeat, int Before, int After) : IGameEvent;
public sealed record MouChenGongMingceCompletedEvent(long FrameId, int RecipientSeat, string? Branch, bool CeGained) : IGameEvent;
public sealed record MouChenGongReleaseStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, long WindowId) : IGameEvent;
public sealed record MouChenGongReleaseAcceptedEvent(long FrameId, int TargetSeat, int FrozenX) : IGameEvent;
public sealed record MouChenGongReleaseDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record MouChenGongCeClearedEvent(long FrameId, int OwnerSeat, int FrozenX, int ClearedCount) : IGameEvent;
public sealed record MouChenGongReleaseCompletedEvent(long FrameId, int? TargetSeat, int? FrozenX, bool Cleared) : IGameEvent;
public sealed record MouChenGongMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From,
    CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouChenGongZhichiIssuedEvent(MouChenGongZhichiProtection Protection) : IGameEvent;
public sealed record MouChenGongZhichiPreventedEvent(long WindowId, MouChenGongZhichiProtection Protection,
    int? SourceSeat, int TargetSeat, int Amount) : IGameEvent;

internal interface IMouChenGongHost
{
    SkillProgramStepOutcome Gift(ProgramSkillFrame frame);
    SkillProgramStepOutcome Release(ProgramSkillFrame frame);
}
internal sealed class ResolveMouChenGongMingceGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenGongMingceGift;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) =>
    { c.PublicControlValue(4); c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.SelectedTarget, 1, e.Condition)); });
    public override ISkillProgramEffectHandler Handler { get; } = new MouChenGongGiftHandler();
}
internal sealed class ResolveMouChenGongMingceReleaseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenGongMingceRelease;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(6));
    public override ISkillProgramEffectHandler Handler { get; } = new MouChenGongReleaseHandler();
}
internal sealed class MouChenGongGiftHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenGongMingceGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouChenGongHost>().Gift(f);
}
internal sealed class MouChenGongReleaseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouChenGongMingceRelease;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouChenGongHost>().Release(f);
}
