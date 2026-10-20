using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouHanDangStage { CostChoice, Paying, DiscardChildren, ModeChoice, ParticipantChoice, Drawing, DrawChildren }
public sealed record MouHanDangReceipt(CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, bool Gongqi)
{
    public MouHanDangStage Stage { get; init; }
    public int TargetSeat { get; init; }
    public string? Branch { get; init; }
    public int Index { get; init; }
    public int? PaidCardId { get; init; }
    public Suit PaidSuit { get; init; }
    public long PaymentSequence { get; init; }
    private readonly IReadOnlyList<int> _participants = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Participants { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
    public int RequestedDraw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHanDangReceipt? MouHanDang { get; init; } }
public sealed record MouHanDangStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, bool Gongqi, int TargetSeat) : IGameEvent;
public sealed record MouHanDangBranchEvent(long FrameId, int TargetSeat, string Branch) : IGameEvent;
public sealed record MouHanDangParticipantEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record MouHanDangDiscardedEvent(long FrameId, int Index, int Seat, int CardId, CardLocation From, CardLocation To, long Sequence, Suit EffectiveSuit) : IGameEvent;
public sealed record MouHanDangGongqiGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int CardId, Suit Suit, long PaymentSequence) : IGameEvent;
public sealed record MouHanDangUseRestrictedEvent(long ActionId, int ActorSeat, long GrantFrameId) : IGameEvent;
public sealed record MouHanDangJiefanDisabledEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record MouHanDangJiefanRestoredEvent(int OwnerSeat, int VictimSeat) : IGameEvent;
public sealed record MouHanDangDrawReturnedEvent(long FrameId, int TargetSeat, int RequestedCount, int ActualCount, int ReplacementCount, long? AttemptId) : IGameEvent;
public sealed record MouHanDangCompletedEvent(long FrameId, bool Gongqi) : IGameEvent;
public sealed record MouHanDangMovementSignedEvent(long FrameId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;

internal interface IMouHanDangHost
{ SkillProgramStepOutcome Gongqi(ProgramSkillFrame f); SkillProgramStepOutcome Jiefan(ProgramSkillFrame f); }
internal abstract class MouHanDangDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new MouHanDangHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class ResolveMouHanDangGongqiDescriptor : MouHanDangDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHanDangGongqi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class ResolveMouHanDangJiefanDescriptor : MouHanDangDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHanDangJiefan;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
internal sealed class MouHanDangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveMouHanDangGongqi ? host.GetCapability<IMouHanDangHost>().Gongqi(f) : host.GetCapability<IMouHanDangHost>().Jiefan(f);
}
