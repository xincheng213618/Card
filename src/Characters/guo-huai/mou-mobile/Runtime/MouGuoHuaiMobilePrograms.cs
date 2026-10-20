using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouGuoHuaiStage { StartRewards, SelectingCard, SelectingRecipient, PlacingChildren, StoreChildren, Drawing, DrawChildren }
public sealed record MouGuoHuaiPrediction(int CardId, int Ordinal, int? RecipientSeat);
public sealed record MouGuoHuaiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long ParentId, int ActualTurn)
{
    public MouGuoHuaiStage Stage { get; init; }
    private IReadOnlyList<int> _cards = [];
    private IReadOnlyList<long> _rewards = [];
    private IReadOnlyList<MouGuoHuaiPrediction> _predictions = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<long> Rewards { get => _rewards; init => _rewards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouGuoHuaiPrediction> Predictions { get => _predictions; init => _predictions = Array.AsReadOnly(value.ToArray()); }
    public int SelectedCard { get; init; }
    public int Index { get; init; }
    public long PredictionId { get; init; }
    public int Requested { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGuoHuaiReceipt? MouGuoHuai { get; init; } }
public sealed record MouGuoHuaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long ParentId, int ActualTurn) : IGameEvent;
public sealed record MouGuoHuaiStoredEvent(long FrameId, CardConversionSource Source, int Index, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouGuoHuaiPredictionIssuedEvent(long PredictionId, long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int CardId, int Ordinal, int? RecipientSeat) : IGameEvent;
public sealed record MouGuoHuaiPredictionPlacedEvent(long PredictionId, int CardId, int Ordinal, int ActualPosition, bool AtBottom, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouGuoHuaiPredictionVerifiedEvent(long PredictionId, long BatchId, int MovementIndex, long Sequence, int GainingSeat, bool Success) : IGameEvent;
public sealed record MouGuoHuaiPredictionDueEvent(long PredictionId, int ActualTurn, int OwnerSeat, bool NoneSuccess) : IGameEvent;
public sealed record MouGuoHuaiPredictionConsumedEvent(long PredictionId, long FrameId, string Reason) : IGameEvent;
public sealed record MouGuoHuaiGainQualifiedEvent(long PredictionId, long BatchId, CardConversionSource Source, string GameplayHash, int MovementIndex, long Sequence, int GainingSeat) : IGameEvent;
public sealed record MouGuoHuaiRewardIssuedEvent(long FrameId, long PredictionId, int Requested, int OwnerSeat) : IGameEvent;
public sealed record MouGuoHuaiDrawReturnedEvent(long FrameId, long PredictionId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouGuoHuaiMovementEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouGuoHuaiCompletedEvent(long FrameId, SkillProgramEffectOp Op, int Predictions, int Rewards) : IGameEvent;

internal interface IMouGuoHuaiMobileHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouGuoHuaiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ISkillProgramEffectHandler Handler => new MouGuoHuaiHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ResolveMouGuoHuaiJingceStartDescriptor : MouGuoHuaiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class ResolveMouGuoHuaiJingcePredictionDescriptor : MouGuoHuaiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class ResolveMouGuoHuaiJingceRewardDescriptor : MouGuoHuaiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuoHuaiJingceReward;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];
}
internal sealed class MouGuoHuaiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IMouGuoHuaiMobileHost>().Resolve(f, op);
}
