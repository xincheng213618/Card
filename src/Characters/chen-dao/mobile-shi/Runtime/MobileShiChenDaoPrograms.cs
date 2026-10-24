using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MobileShiChenDaoChoice(CardConversionSource Source, string GameplayHash, long WindowId,
    int ActualTurn, int PhaseId, int FrozenCount, bool Finished = false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiChenDaoChoice? MobileShiChenDao { get; init; }
}
public sealed record MobileShiHongyiMarksChangedEvent(long FrameId, CardConversionSource Source, int Before, int After) : IGameEvent;
public sealed record MobileShiHongyiPreparedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int FrozenCount, bool DrawFirst) : IGameEvent;
public sealed record MobileShiHongyiEndingEvent(long FrameId, long PreparationId, int OwnerSeat, int ActualTurn, int FrozenCount, bool Draw) : IGameEvent;
public sealed record MobileShiHongyiDrawBoundEvent(long FrameId, int OwnerSeat, int Count) : IGameEvent;
public sealed record MobileShiWanglieSelectedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int PhaseId, int CardId) : IGameEvent;
public sealed record MobileShiWanglieUsedEvent(long SelectionId, long UseId, long ActionId, int OwnerSeat,
    int ActualTurn, int PhaseId) : IGameEvent;
public sealed record MobileShiWanglieCompletedEvent(long SelectionId, long UseId, long ActionId, int OwnerSeat,
    int ActualTurn, int PhaseId) : IGameEvent;

internal interface IMobileShiChenDaoHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MobileShiChenDaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new MobileShiChenDaoHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => op == SkillProgramEffectOp.ResolveMobileShiWanglie
        ? [new RequireTriggerWindows([SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramTriggerWindow.CardUseCompleted])]
        : [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.AfterDamageApplied,
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramTriggerWindow.TurnEnding]),
           new CreateChoiceResult("hongyi-draw", ["draw-0", "draw-1", "draw-2", "draw-3", "draw-4"])];
}
internal sealed class ResolveMobileShiWanglieDescriptor() : MobileShiChenDaoDescriptor(SkillProgramEffectOp.ResolveMobileShiWanglie);
internal sealed class ResolveMobileShiHongyiDescriptor() : MobileShiChenDaoDescriptor(SkillProgramEffectOp.ResolveMobileShiHongyi);
internal sealed class MobileShiChenDaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiChenDaoHost>().Resolve(frame, op);
}
