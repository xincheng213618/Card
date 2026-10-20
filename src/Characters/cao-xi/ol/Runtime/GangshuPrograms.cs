using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record CaoXiGangshuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, long CardUseFrameId, long ActionId, CaoXiNumbers Numbers);
public sealed record CaoXiGangshuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long ParentWindowId, long CardUseFrameId, long ActionId, CaoXiNumbers Numbers) : IGameEvent;
public sealed record CaoXiGangshuIncreasedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillRuleQuery Query, int BeforeValue, int AfterValue, long? GrantedInDrawPhaseToken = null, long GrantedAfterActualPhaseToken = 0) : IGameEvent;
public sealed record CaoXiGangshuUnitExpiredEvent(long GrantFrameId, int OwnerSeat, string Reason, long NativeId) : IGameEvent;
public sealed record CaoXiGangshuActualOffsetEvent(long NativeId, int OwnerSeat, string Kind, int Cursor) : IGameEvent;
public sealed record CaoXiGangshuDrawCaptureEvent(ActualDiscardRecoveryPhaseKey Phase, int OwnerSeat) : IGameEvent;
public sealed record CaoXiGangshuDrawUnitIssuedEvent(ActualDiscardRecoveryPhaseKey Phase, long GrantFrameId, CardConversionSource Source) : IGameEvent;
internal sealed class ResolveGangshuAttributeIncrementDescriptor() : CaoXiDescriptor(SkillProgramEffectOp.ResolveGangshuAttributeIncrement)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)]; }
