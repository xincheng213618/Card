using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class ArmHongjiNextPhaseDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.ArmHongjiNextPhase)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]; }
internal sealed class ResolveHongjiGrantedPhaseDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.ResolveHongjiGrantedPhase)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindows([SkillProgramTriggerWindow.DrawPhaseEnded, SkillProgramTriggerWindow.PlayEnding])];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}

public enum HongjiStage { Choose, ExtraPhase, Returned }
public sealed record ZhangShiPingHongjiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, int ActualTurn, int RecipientSeat, HongjiStage Stage, bool DrawEligible = false, bool PlayEligible = false,
    TurnPhase Phase = TurnPhase.Draw, TurnPhase ReturnPhase = TurnPhase.Draw,
    ActualDiscardRecoveryPhaseKey? ReturnActualPhase = null, ZhangShiPingHongjiGrantedEvent? Promise = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramPhaseSchedule? SuspendedSchedule { get; init; }
    private readonly IReadOnlyList<DrawPhaseObligationFrame> _drawPhaseParents = Array.AsReadOnly(Array.Empty<DrawPhaseObligationFrame>());
    public IReadOnlyList<DrawPhaseObligationFrame> DrawPhaseParents { get => _drawPhaseParents; init => _drawPhaseParents = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ZhangShiPingHongjiGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int RoundNumber, int RecipientSeat, TurnPhase Phase, long PreparationParentId) : IGameEvent;
public sealed record ZhangShiPingHongjiConsumedEvent(long PromiseFrameId, long FrameId, CardConversionSource Source,
    int ActualTurn, int RecipientSeat, TurnPhase Phase, long ParentWindowId) : IGameEvent;
public sealed record ZhangShiPingHongjiReturnedEvent(long PromiseFrameId, long FrameId, int ActualTurn, int RecipientSeat, TurnPhase Phase) : IGameEvent;
