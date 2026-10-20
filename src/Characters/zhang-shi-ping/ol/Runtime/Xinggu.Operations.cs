using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class InitializeXingGuMountPileDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.InitializeXingGuMountPile)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)]; }
internal sealed class PlaceXingGuMountAndObtainDiamondDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]; }
public enum XingGuStage { InitialChildren, Choose, PlacementChildren, DiamondChildren, Finished }
public sealed record ZhangShiPingXingGuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, int ActualTurn, XingGuStage Stage, CardLocation PileLocation, int SequenceBefore = 0, int SequenceAfter = 0,
    int? CardId = null, int? RecipientSeat = null, int? DiamondCardId = null);
public sealed record ZhangShiPingXingGuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long ParentWindowId, bool Initial, CardLocation PileLocation) : IGameEvent;
public sealed record ZhangShiPingXingGuCardStoredEvent(long FrameId, int CardId, int MovementSequence) : IGameEvent;
public sealed record ZhangShiPingXingGuPlacementIssuedEvent(long FrameId, int CardId, int RecipientSeat, int SequenceBefore) : IGameEvent;
public sealed record ZhangShiPingXingGuDiamondIssuedEvent(long FrameId, int OwnerSeat, int? CardId, int SequenceBefore, int SequenceAfter) : IGameEvent;
