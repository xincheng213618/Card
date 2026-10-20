using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum HeJinMouzhuStage { ChoosingGift, GiftChildren, ChoosingAssault, AssaultIssued }
public sealed record HeJinMouzhuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int PhaseInstanceId, int ActorSeat, long StartedAt, HeJinMouzhuStage Stage = HeJinMouzhuStage.ChoosingGift,
    int? GiftId = null, long Before = 0, long After = 0, HeJinMouzhuUseOrigin? Assault = null)
{
    private readonly IReadOnlyList<int> _handIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> HandIds { get => _handIds; init => _handIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HeJinMouzhuUseOrigin(long ProgramFrameId, int InstructionIndex, long CardUseFrameId,
    CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstanceId,
    int ActorSeat, int TargetSeat, CardKind CardKind, bool AttackStarted = false);
public sealed record HeJinMouzhuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int PhaseInstanceId, int ActorSeat, long MovementSequence, int HandCount) : IGameEvent;
public sealed record HeJinMouzhuGiftPaidEvent(long FrameId, int ActorSeat, int OwnerSeat, long Before, long After) : IGameEvent;
public sealed record HeJinMouzhuAssaultIssuedEvent(HeJinMouzhuUseOrigin Origin) : IGameEvent;
public sealed record HeJinMouzhuCompletedEvent(long FrameId, bool AssaultIssued) : IGameEvent;
internal sealed class ResolveMouzhuHandDemandAndAssaultDescriptor() : HeJinDescriptor(SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
