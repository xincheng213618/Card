using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum RuanHuiStage { RevealChildren, DrawChoice, SettleChildren, TargetChoice, RandomChildren, ClaimChildren }
public sealed record RuanHuiEntity(int CardId, CardKind Kind, int Rank, CardLocation From, long Sequence = 0);
public sealed record RuanHuiStep(int Index, RuanHuiStage Stage, long Before, long After, int Count);
public sealed record RuanHuiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex, long ParentId, int ActualTurn, long? LeaseId, long? ActionId) : IGameEvent;
public sealed record RuanHuiRevealedEntityEvent(long FrameId, int Index, RuanHuiEntity Entity) : IGameEvent;
public sealed record RuanHuiDecisionEvent(long FrameId, bool ReplaceDraw) : IGameEvent;
public sealed record RuanHuiRandomEntityEvent(long FrameId, int TargetSeat, RuanHuiEntity Entity) : IGameEvent;
public sealed record RuanHuiStepEvent(long FrameId, RuanHuiStep Step) : IGameEvent;
public sealed record RuanHuiCompletedEvent(long FrameId) : IGameEvent;
public sealed record JingzhongDiscardEvent(long PhaseToken, int OwnerSeat, long MovementSequence, int CardId) : IGameEvent;
public sealed record JingzhongLeaseEvent(long FrameId, CardConversionSource Source, string GameplayHash, int RecipientSeat, long AfterPhaseToken, int ActualTurn) : IGameEvent;
public sealed record JingzhongUseEntityEvent(long PhaseToken, long ActionId, long UseId, int OriginalActor, bool Response, int Index, int MaterialCount, int CardId, CardKind PhysicalKind, CardLocation OriginalFrom, long CapturedAfterSequence, long? PaidEntrySequence) : IGameEvent;
public sealed record JingzhongSettledEntityEvent(long LeaseId, long PhaseToken, long ActionId, long BatchId, int CardId, long MovementSequence) : IGameEvent;
public sealed record JingzhongClaimIssuedEvent(long FrameId, long LeaseId, long PhaseToken, long ActionId, long BatchId, int EntityCount) : IGameEvent;
public sealed record RuanHuiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex, long ParentId, int ActualTurn,
    RuanHuiStage Stage, IReadOnlyList<RuanHuiEntity> Entities, IReadOnlyList<RuanHuiStep> Steps, bool? ReplaceDraw = null, int? TargetSeat = null, long? LeaseId = null, long? ActionId = null, RuanHuiEntity? RandomEntity = null)
{
    private IReadOnlyList<RuanHuiEntity> _entities = Array.AsReadOnly(Entities.ToArray());
    public IReadOnlyList<RuanHuiEntity> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<RuanHuiStep> _steps = Array.AsReadOnly(Steps.ToArray());
    public IReadOnlyList<RuanHuiStep> Steps { get => _steps; init => _steps = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RuanHuiReceipt? RuanHui { get; init; } }
internal interface IRuanHuiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class RuanHuiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new RuanHuiHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2d));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch {
        SkillProgramEffectOp.ResolveMingchaRevealDrawChoice => SkillProgramTriggerWindow.DrawPhaseStarting,
        SkillProgramEffectOp.ArmJingzhongNextActualPlay => SkillProgramTriggerWindow.DiscardPhaseEnded, _ => SkillProgramTriggerWindow.DiscardPileReceived })];
}
internal sealed class ResolveMingchaRevealDrawChoiceDescriptor : RuanHuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMingchaRevealDrawChoice; }
internal sealed class ArmJingzhongNextActualPlayDescriptor : RuanHuiDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmJingzhongNextActualPlay; }
internal sealed class ObtainJingzhongUsedEntitiesDescriptor : RuanHuiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainJingzhongUsedEntities; }
internal sealed class RuanHuiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<IRuanHuiHost>().Resolve(f, op);
}
internal static class RuanHuiComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMingchaRevealDrawChoice or SkillProgramEffectOp.ArmJingzhongNextActualPlay or SkillProgramEffectOp.ObtainJingzhongUsedEntities;
    internal static void Activation(string path, SkillProgramActivation a)
    { if (a.Effects.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": Ruan Hui requires an actual draw/discard/movement boundary."); }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [var e] || !Handles(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.UsageLimit is not null || t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
            (e.Op == SkillProgramEffectOp.ResolveMingchaRevealDrawChoice ? t.Window != SkillProgramTriggerWindow.DrawPhaseStarting || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional :
             e.Op == SkillProgramEffectOp.ArmJingzhongNextActualPlay ? t.Window != SkillProgramTriggerWindow.DiscardPhaseEnded || t.Subject != SkillProgramTriggerSubject.Owner || !t.AllowOwnDiscardPhaseEnded || !t.Optional :
             t.Window != SkillProgramTriggerWindow.DiscardPileReceived || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.MovementOccurrence is not null))
            throw new InvalidOperationException(path + $": Ruan Hui requires a single exact native phase or issued movement instruction (binding={t.Id}, window={t.Window}, subject={t.Subject}, optional={t.Optional}, ownDiscard={t.AllowOwnDiscardPhaseEnded}, movement={t.MovementOccurrence}, draw={t.DrawPhaseMode}).");
    }
}
