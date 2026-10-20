using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouHuangYueyingMobileStage { Choosing, Drawing, DrawChildren, MovementChildren }
public sealed record MouHuangYueyingMobileReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    MouHuangYueyingMobileStage Stage, int ActualTurn, int TurnOwnerSeat, long WindowId, int RecipientSeat,
    ActualDiscardRecoveryPhaseKey? Phase = null, bool Paid = false, int? CardId = null, CardLocation? From = null,
    long Before = 0, long After = 0, long? GrantId = null, long? ClaimSequence = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouHuangYueyingMobileReceipt? MouHuangYueyingMobile { get; init; }
}
public sealed record MouHuangYueyingJizhiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, int ActualTurn, int TurnOwnerSeat) : IGameEvent;
public sealed record MouHuangYueyingJizhiExemptEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int TurnOwnerSeat, int CardId, long MovementSequence) : IGameEvent;
public sealed record MouHuangYueyingJizhiDrawReturnedEvent(long FrameId, long? AttemptFrameId, int ActualCount, int ReplacementCount) : IGameEvent;
public sealed record MouHuangYueyingQicaiReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat) : IGameEvent;
public sealed record MouHuangYueyingQicaiEquipmentPlacedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int RecipientSeat, int CardId, CardLocation From, CardLocation To, long MovementSequence, int? ReplacedCardId) : IGameEvent;
public sealed record MouHuangYueyingQiGrantedEvent(long GrantId, CardConversionSource Source, string GameplayHash, int RecipientSeat, long MovementSequence, long? PreviousGrantId) : IGameEvent;
public sealed record MouHuangYueyingQiGainClaimedEvent(long GrantId, long BatchId, int MovementIndex, CardConversionSource Source, string GameplayHash, int GainingSeat, int CardId, CardLocation OriginalFrom, long GainSequence, int Remaining) : IGameEvent;
public sealed record MouHuangYueyingQiTransferredEvent(long FrameId, long GrantId, long GainSequence, int CardId, CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record MouHuangYueyingQiClaimSettledEvent(long FrameId, long GrantId, long GainSequence) : IGameEvent;
public sealed record MouHuangYueyingMobileCompletedEvent(long FrameId, SkillProgramEffectOp Operation, bool Paid) : IGameEvent;

internal interface IMouHuangYueyingMobileHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal interface IMouHuangYueyingMobileQuery : IProgramSelectionQuery
{ bool CanPlace(int owner, int target); }
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 31500, SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment)]
internal sealed class MouHuangYueyingMobileTargets : IProgramSelectionTargetFilter
{ public bool Allows(ProgramSelectionContext c, int target, IProgramSelectionQueries q) => q.Get<IMouHuangYueyingMobileQuery>().CanPlace(c.OwnerSeat, target); }
internal abstract class MouHuangYueyingMobileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MouHuangYueyingMobileHandler(Op);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveMouHuangYueyingJizhi => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseBeforeTargetEffects, SkillProgramTriggerWindow.CardResponseAccepted])],
        SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment => [new ReadSelectedTarget()],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)]
    };
}
internal sealed class ResolveMouHuangYueyingJizhiDescriptor : MouHuangYueyingMobileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuangYueyingJizhi;
  public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase); }
internal sealed class PlaceMouHuangYueyingQicaiEquipmentDescriptor : MouHuangYueyingMobileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment;
  public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase); }
internal sealed class TransferMouHuangYueyingQicaiGainsDescriptor : MouHuangYueyingMobileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains; }
internal sealed class MouHuangYueyingMobileHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouHuangYueyingMobileHost>().Resolve(f, op);
}
