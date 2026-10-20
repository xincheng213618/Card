using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IDuXiProgramHost
{
    SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}
internal sealed class DuXiHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IDuXiProgramHost>().Resolve(frame, operation);
}
internal abstract class DuXiDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new DuXiHandler(operation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => operation == SkillProgramEffectOp.RecordBixiongActualDiscard ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveQuxiAtPlayEndingDescriptor() : DuXiDescriptor(SkillProgramEffectOp.ResolveQuxiAtPlayEnding)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class TransferQuxiAtRoundOrDeathDescriptor() : DuXiDescriptor(SkillProgramEffectOp.TransferQuxiAtRoundOrDeath)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindows([SkillProgramTriggerWindow.RoundStarting, SkillProgramTriggerWindow.CharacterDied])];
}
internal sealed class RecordBixiongActualDiscardDescriptor() : DuXiDescriptor(SkillProgramEffectOp.RecordBixiongActualDiscard)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}

public enum DuXiQuxiStage { FaceDownChildren, Pair, Obtain, ObtainChildren, TransferFeng, TransferQian, Finished }
public sealed record DuXiQuxiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, long ParentWindowId,
    int ActualTurnNumber, DuXiQuxiStage Stage, bool Initial, int? LessHandSeat = null, int? MoreHandSeat = null,
    int? ObtainedCardId = null, CardLocation? ObtainedFrom = null, int SequenceBefore = 0, int SequenceAfter = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuXiQuxiReceipt? DuXiQuxi { get; init; }
}
public sealed record DuXiQuxiAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurnNumber, bool WasFaceDown) : IGameEvent;
public sealed record DuXiQuxiPairChosenEvent(long FrameId, int LessHandSeat, int MoreHandSeat, int LessHandCount, int MoreHandCount) : IGameEvent;
public sealed record DuXiQuxiCardObtainedEvent(long FrameId, int RecipientSeat, int CardId, CardLocation From, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record DuXiQuxiMarkerMovedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurnNumber,
    PlayerMarkerKind Marker, int? PreviousHolder, int Holder) : IGameEvent;
public sealed record DuXiQuxiDiscardSkippedEvent(long AcceptedFrameId, int OwnerSeat, int ActualTurnNumber) : IGameEvent;
public sealed record DuXiBixiongSuitRecordedEvent(long BatchId, CardConversionSource Source, int OwnerSeat, int ActualTurnNumber,
    long ActualDiscardPhaseToken, int CardId, int MovementSequence, Suit Suit) : IGameEvent;
public sealed record DuXiBixiongExpiredEvent(int OwnerSeat, int ActualTurnNumber) : IGameEvent;
