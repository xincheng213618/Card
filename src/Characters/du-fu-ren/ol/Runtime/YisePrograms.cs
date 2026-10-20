using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record DuFuRenDamageReceipt(long AttackId, int ChainCursor, int TargetSeat, int InheritedAmount,
    IReadOnlyList<long> GrantIds, int? TransferredTargetSeat = null, long? TransferWindowId = null)
{
    private readonly IReadOnlyList<long> _grantIds = Array.AsReadOnly(GrantIds.ToArray());
    public IReadOnlyList<long> GrantIds { get => _grantIds; init => _grantIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DuFuRenBlackIssuedEvent(long FrameId, CardConversionSource Source, long BatchId, int RecipientSeat) : IGameEvent;
public sealed record DuFuRenBlackConsumedEvent(long GrantId, long AttackId, long DamageFrameId, int TargetSeat, int ChainCursor) : IGameEvent;
public sealed record DuFuRenDamageIssuedEvent(long AttackId, int TargetSeat, int ChainCursor, int InheritedAmount, int Bonus) : IGameEvent;
public sealed record DuFuRenDamageRebasedEvent(long AttackId, int TargetSeat, int ChainCursor, int InheritedAmount) : IGameEvent;
public sealed record DuFuRenDamageTransferredEvent(long AttackId, int OriginalTargetSeat, int TargetSeat, int ChainCursor, long WindowId, int Amount) : IGameEvent;
internal sealed class ResolveYiseOwnedGainDescriptor() : DuFuRenDescriptor(SkillProgramEffectOp.ResolveYiseOwnedGain)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
