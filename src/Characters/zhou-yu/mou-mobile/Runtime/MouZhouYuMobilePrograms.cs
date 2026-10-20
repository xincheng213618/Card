using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouZhouYuMobileTurn(int TurnNumber, int OwnerSeat, int Ordinal);
public enum MouZhouYuMobileStage { OwnerChoice, CoverChildren, TargetChoice, ChoiceChildren, GiftChildren, LossChildren, CleanupChildren }
public sealed record MouZhouYuMobileReceipt(CardConversionSource Source, string GameplayHash, MouZhouYuMobileTurn Turn, ActualDiscardRecoveryPhaseKey Phase, int TargetSeat)
{
    public MouZhouYuMobileStage Stage { get; init; }
    public int CardId { get; init; }
    public CardKind PrintedKind { get; init; }
    public Suit ActualSuit { get; init; }
    public Suit DeclaredSuit { get; init; }
    public long PaymentSequence { get; init; }
    public string? Answer { get; init; }
    public bool Revealed { get; init; }
    public long GiftSequence { get; init; }
    public int LossHpBefore { get; init; }
    public int LossHpAfter { get; init; }
    public bool LossPaid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhouYuMobileReceipt? MouZhouYuMobile { get; init; } }
public sealed record MouZhouYuMobileYingziIssuedEvent(long FrameId, CardConversionSource Source, MouZhouYuMobileTurn Turn, bool Hand, bool Hp, bool Equipment, int Count) : IGameEvent;
public sealed record MouZhouYuMobileStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, MouZhouYuMobileTurn Turn, ActualDiscardRecoveryPhaseKey Phase, int TargetSeat) : IGameEvent;
public sealed record MouZhouYuMobileCoveredEvent(long FrameId, int CardId, CardKind PrintedKind, Suit ActualSuit, Suit DeclaredSuit, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouZhouYuMobileAnsweredEvent(long FrameId, int TargetSeat, string Answer) : IGameEvent;
public sealed record MouZhouYuMobileDisabledEvent(long FrameId, int OwnerSeat, MouZhouYuMobileTurn Turn, string Reason) : IGameEvent;
public sealed record MouZhouYuMobileGiftedEvent(long FrameId, int CardId, int TargetSeat, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouZhouYuMobileLossIssuedEvent(long FrameId, int TargetSeat, int Requested) : IGameEvent;
public sealed record MouZhouYuMobileLossPaidEvent(long FrameId, int TargetSeat, int Before, int After) : IGameEvent;
public sealed record MouZhouYuMobileMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouZhouYuMobileCompletedEvent(long FrameId, bool Paid, string? Answer) : IGameEvent;
internal interface IMouZhouYuMobileHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, bool yingzi); }
internal abstract class MouZhouYuMobileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveMouZhouYuFanjian ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveMouZhouYuYingzi ? [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)] : [new ReadSelectedTarget()];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler => new MouZhouYuMobileHandler(Op);
}
internal sealed class ResolveMouZhouYuYingziDescriptor : MouZhouYuMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuYingzi; }
internal sealed class ResolveMouZhouYuFanjianDescriptor : MouZhouYuMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuFanjian; }
internal sealed class MouZhouYuMobileHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IMouZhouYuMobileHost>().Resolve(f, op == SkillProgramEffectOp.ResolveMouZhouYuYingzi);
}
