using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record YintianFirstRecoveryEvent(long ChangeId, long? NativeParentId, int ActualTurn,
    int TurnOwner, int? SourceSeat, int TargetSeat, int Amount, int HpBefore, int HpAfter) : IGameEvent;
public sealed record YintianRecoveryQualifiedEvent(long ChangeId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record YintianChargeIssuedEvent(long GrantId, CardConversionSource Source, string GameplayHash,
    long RecoveryChangeId, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record YintianChargeRetainedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long RecoveryChangeId, int ActualTurn, int TurnOwner, long ExistingGrantId) : IGameEvent;
public sealed record YintianChargeConsumedEvent(long GrantId, long InvoiceId, long AttackId, long DamageFrameId,
    int SourceSeat, int TargetSeat, int ChainCursor, int Amount) : IGameEvent;
public sealed record YintianDamageIssuedEvent(long InvoiceId, long GrantId, long AttackId, int SourceSeat,
    int TargetSeat, int ChainCursor, int InheritedAmount) : IGameEvent;
public sealed record YintianDamageTransferredEvent(long InvoiceId, long GrantId, long AttackId, int SourceSeat,
    int OriginalTargetSeat, int TargetSeat, int ChainCursor, long WindowId, int Amount) : IGameEvent;
public sealed record YintianDamageRebasedEvent(long InvoiceId, long AttackId, int TargetSeat,
    int ChainCursor, int InheritedAmount) : IGameEvent;

public sealed record YintianRecoveryReceipt(CardConversionSource Source, string GameplayHash,
    long ChangeId, int ActualTurn, int TurnOwner, long ChargeId, bool Issued);
public sealed record YintianDamageReceipt(long InvoiceId, long GrantId, long AttackId, int SourceSeat,
    int TargetSeat, int ChainCursor, int InheritedAmount,
    int? TransferredTargetSeat = null, long? TransferWindowId = null);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YintianRecoveryReceipt? Yintian { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YintianDamageReceipt? YintianDamage { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YintianDamageReceipt? YintianDamage { get; init; }
}
public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YintianDamageReceipt? YintianDamage { get; init; }
}

internal interface IYintianHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame);
}
internal sealed class ResolveYintianFirstRecoveryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYintianFirstRecovery;
    public override ISkillProgramEffectHandler Handler { get; } = new YintianHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHpRecovered)];
}
internal sealed class YintianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYintianFirstRecovery;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => host.GetCapability<IYintianHost>().Execute(frame);
}
