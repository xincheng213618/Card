using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouDongZhaoStage { ChoosingDraw, Drawing, DrawChildren, ChoosingGift, GiftChildren, ChoosingKept, DiscardChildren, ChoosingMove, MoveChildren, Complete, Damage }
public sealed record MouDongZhaoNameCount(CardKind Kind, int Count);
public sealed record MouDongZhaoHandEntity(int CardId, CardKind Kind, Suit Suit);
public sealed record MouDongZhaoReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    MouDongZhaoStage Stage, long WindowId, int RoundNumber, int TargetSeat, ActualDiscardRecoveryPhaseKey? Phase = null,
    int RequestedDraw = 0, int GiftCount = 0, NativeDrawOutcome? Draw = null, bool NamesFrozen = false,
    bool LostHand = false, int? MovedCardId = null, Suit? MovedSuit = null, bool FieldMoved = false,
    long Before = 0, long After = 0, long? GrantFrameId = null)
{
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<MouDongZhaoHandEntity> _hand = Array.AsReadOnly(Array.Empty<MouDongZhaoHandEntity>());
    private readonly IReadOnlyList<MouDongZhaoNameCount> _names = Array.AsReadOnly(Array.Empty<MouDongZhaoNameCount>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouDongZhaoHandEntity> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouDongZhaoNameCount> Names { get => _names; init => _names = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouDongZhaoLossSource(long GrantFrameId, CardConversionSource Source, string GameplayHash, Suit Suit, int CardId, CardLocation From);
public sealed partial record CardMovementBatchContext
{
    private readonly IReadOnlyList<MouDongZhaoLossSource> _mouYishiSources = Array.AsReadOnly(Array.Empty<MouDongZhaoLossSource>());
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MouDongZhaoLossSource> MouYishiSources { get => _mouYishiSources; init => _mouYishiSources = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouDongZhaoReceipt? MouDongZhao { get; init; }
}
public sealed record MouDongZhaoShunjiAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, int RoundNumber, int TargetSeat) : IGameEvent;
public sealed record MouDongZhaoDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement,
    long Before, long After, long? AttemptId) : IGameEvent;
public sealed record MouDongZhaoGiftSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouDongZhaoNameSpentEvent(long FrameId, int OwnerSeat, CardKind Kind) : IGameEvent;
public sealed record MouDongZhaoYishiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int HandCount) : IGameEvent;
public sealed record MouDongZhaoFieldMovedEvent(long FrameId, int CardId, Suit Suit, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouDongZhaoYishiGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    Suit Suit, int OwnTurnStarts, long MovementSequence) : IGameEvent;
public sealed record MouDongZhaoYishiLossDrawIssuedEvent(long FrameId, long GrantFrameId, long BatchId,
    CardConversionSource Source, string GameplayHash, Suit Suit) : IGameEvent;
public sealed record MouDongZhaoMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From,
    CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouDongZhaoCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;

internal interface IMouDongZhaoHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal sealed class MouDongZhaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMouDongZhaoHost>().Resolve(frame, op);
}
internal abstract class MouDongZhaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) =>
        context.Frame.MouDongZhao is not null && context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouDongZhaoShunjiDescriptor : MouDongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDongZhaoShunji;
    public override ISkillProgramEffectHandler Handler { get; } = new MouDongZhaoHandler(SkillProgramEffectOp.ResolveMouDongZhaoShunji);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
}
internal sealed class MouDongZhaoYishiDescriptor : MouDongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDongZhaoYishi;
    public override ISkillProgramEffectHandler Handler { get; } = new MouDongZhaoHandler(SkillProgramEffectOp.ResolveMouDongZhaoYishi);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class MouDongZhaoYishiLossDescriptor : MouDongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawMouDongZhaoYishiLoss;
    public override ISkillProgramEffectHandler Handler { get; } = new MouDongZhaoHandler(SkillProgramEffectOp.DrawMouDongZhaoYishiLoss);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry);
}
