using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class ResolveQuxiAtPlayEndingDescriptor() : DuXiDescriptor(SkillProgramEffectOp.ResolveQuxiAtPlayEnding)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class TransferQuxiAtRoundOrDeathDescriptor() : DuXiDescriptor(SkillProgramEffectOp.TransferQuxiAtRoundOrDeath)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindows([SkillProgramTriggerWindow.RoundStarting, SkillProgramTriggerWindow.CharacterDied])];
}

public enum DuXiQuxiStage { FaceDownChildren, Pair, Obtain, ObtainChildren, TransferFeng, TransferQian, Finished }
public sealed record DuXiQuxiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, long ParentWindowId,
    int ActualTurnNumber, DuXiQuxiStage Stage, bool Initial, int? LessHandSeat = null, int? MoreHandSeat = null,
    int? ObtainedCardId = null, CardLocation? ObtainedFrom = null, int SequenceBefore = 0, int SequenceAfter = 0);
public sealed record DuXiQuxiAcceptedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurnNumber, bool WasFaceDown) : IGameEvent;
public sealed record DuXiQuxiPairChosenEvent(long FrameId, int LessHandSeat, int MoreHandSeat, int LessHandCount, int MoreHandCount) : IGameEvent;
public sealed record DuXiQuxiCardObtainedEvent(long FrameId, int RecipientSeat, int CardId, CardLocation From, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record DuXiQuxiMarkerMovedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurnNumber,
    PlayerMarkerKind Marker, int? PreviousHolder, int Holder) : IGameEvent;
public sealed record DuXiQuxiDiscardSkippedEvent(long AcceptedFrameId, int OwnerSeat, int ActualTurnNumber) : IGameEvent;
