using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IQiaoZhouProgramHost
{
    SkillProgramStepOutcome Forecast(ProgramSkillFrame frame);
}
internal sealed class ResolveXingbuThreeCardForecastDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXingbuThreeCardForecast;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoZhouForecastHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.PublicControlValue(3));
}
internal sealed class QiaoZhouForecastHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXingbuThreeCardForecast;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IQiaoZhouProgramHost>().Forecast(frame);
}

public enum QiaoZhouForecastStage { RevealChildren, Recipient, CleanupChildren }
public sealed record QiaoZhouRevealedCard(int CardId, Suit Suit, int MovementSequence);
public sealed record QiaoZhouForecastReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long EndingWindowId, QiaoZhouForecastStage Stage, int RedCount, int SequenceBefore, int SequenceAfter)
{
    private readonly IReadOnlyList<QiaoZhouRevealedCard> _cards = Array.AsReadOnly(Array.Empty<QiaoZhouRevealedCard>());
    public IReadOnlyList<QiaoZhouRevealedCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public QiaoZhouForecastReceipt? QiaoZhouForecast { get; init; }
}
public sealed record QiaoZhouForecastGrantedEvent(long GrantId, CardConversionSource Source, string GameplayHash,
    int RecipientSeat, int IssuedTurn, int RedCount) : IGameEvent;
public sealed record QiaoZhouForecastActivatedEvent(long GrantId, int RecipientSeat, int ActualTurnNumber) : IGameEvent;
public sealed record QiaoZhouForecastExpiredEvent(long GrantId, int RecipientSeat, int ActualTurnNumber, string Reason) : IGameEvent;
public sealed record QiaoZhouDiscardPhaseSkippedEvent(long GrantId, int RecipientSeat, int ActualTurnNumber) : IGameEvent;
public sealed record QiaoZhouPreparationDiscardedEvent(long FrameId, long GrantId, int RecipientSeat, int CardId,
    int SequenceBefore, int SequenceAfter) : IGameEvent;

public enum QiaoZhouPreparationStage { Selecting, MovementChildren }
public sealed record QiaoZhouPreparationFrame(long Id, int OwnerSeat, int ActualTurnNumber,
    QiaoZhouForecastGrantedEvent Grant, QiaoZhouPreparationStage Stage = QiaoZhouPreparationStage.Selecting,
    int? PaidCardId = null, int SequenceBefore = 0, int SequenceAfter = 0, long? ActiveChildFrameId = null)
    : ResolutionFrame(Id, ResolutionFrameKind.QiaoZhouPreparation, ResolutionFrameStep.ResolvingEffect);
public sealed record QiaoZhouPreparationMovementReturn(long ParentFrameId, long GrantId, int OwnerSeat,
    int ActualTurnNumber, long BatchId);
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QiaoZhouPreparationMovementReturn? QiaoZhouPreparationReturn { get; init; }
}

internal sealed partial class TurnCardUseEffectStore
{
    internal IReadOnlyList<long> RevokeQiaoZhouForecastModifiers(long grantId)
    {
        var ids = _ruleModifiers.Where(m => m.ParentFrameId == grantId && m.Source.SkillId == "ol:xingbu")
            .Select(m => m.GrantSequence).Order().ToArray();
        _ruleModifiers.RemoveAll(m => ids.Contains(m.GrantSequence));
        return ids;
    }
}
