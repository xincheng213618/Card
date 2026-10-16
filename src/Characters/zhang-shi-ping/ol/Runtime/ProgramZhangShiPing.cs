using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IZhangShiPingProgramHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal sealed class ZhangShiPingHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhangShiPingProgramHost>().Resolve(frame, operation);
}
internal abstract class ZhangShiPingDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhangShiPingHandler(operation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => operation is SkillProgramEffectOp.InitializeXingGuMountPile or SkillProgramEffectOp.ResolveHongjiGrantedPhase
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ArmHongjiNextPhaseDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.ArmHongjiNextPhase)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]; }
internal sealed class ResolveHongjiGrantedPhaseDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.ResolveHongjiGrantedPhase)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindows([SkillProgramTriggerWindow.DrawPhaseEnded, SkillProgramTriggerWindow.PlayEnding])]; }
internal sealed class InitializeXingGuMountPileDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.InitializeXingGuMountPile)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)]; }
internal sealed class PlaceXingGuMountAndObtainDiamondDescriptor() : ZhangShiPingDescriptor(SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]; }

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
public enum XingGuStage { InitialChildren, Choose, PlacementChildren, DiamondChildren, Finished }
public sealed record ZhangShiPingXingGuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long ParentWindowId, int ActualTurn, XingGuStage Stage, CardLocation PileLocation, int SequenceBefore = 0, int SequenceAfter = 0,
    int? CardId = null, int? RecipientSeat = null, int? DiamondCardId = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangShiPingHongjiReceipt? ZhangShiPingHongji { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangShiPingXingGuReceipt? ZhangShiPingXingGu { get; init; }
}
public sealed record ZhangShiPingHongjiGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int RoundNumber, int RecipientSeat, TurnPhase Phase, long PreparationParentId) : IGameEvent;
public sealed record ZhangShiPingHongjiConsumedEvent(long PromiseFrameId, long FrameId, CardConversionSource Source,
    int ActualTurn, int RecipientSeat, TurnPhase Phase, long ParentWindowId) : IGameEvent;
public sealed record ZhangShiPingHongjiReturnedEvent(long PromiseFrameId, long FrameId, int ActualTurn, int RecipientSeat, TurnPhase Phase) : IGameEvent;
public sealed record ZhangShiPingXingGuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long ParentWindowId, bool Initial, CardLocation PileLocation) : IGameEvent;
public sealed record ZhangShiPingXingGuCardStoredEvent(long FrameId, int CardId, int MovementSequence) : IGameEvent;
public sealed record ZhangShiPingXingGuPlacementIssuedEvent(long FrameId, int CardId, int RecipientSeat, int SequenceBefore) : IGameEvent;
public sealed record ZhangShiPingXingGuDiamondIssuedEvent(long FrameId, int OwnerSeat, int? CardId, int SequenceBefore, int SequenceAfter) : IGameEvent;

internal static class ZhangShiPingComposition
{
    private static bool IsOwn(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ArmHongjiNextPhase or SkillProgramEffectOp.ResolveHongjiGrantedPhase or
        SkillProgramEffectOp.InitializeXingGuMountPile or SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.Any(t => t.Effects.Any(e => IsOwn(e.Op))) && !p.Activations.Any(a => a.Effects.Any(e => IsOwn(e.Op)))) return;
        if (p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.Contributions.Count != 0 || p.CardIdentities.Count != 0 ||
            p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 ||
            p.LordSkillProjection || p.CannotChallengeDeclarations || p.Id is not ("ol:hongji" or "ol:xinggu"))
            throw new InvalidOperationException($"{path}: Zhang Shiping requires his exact lifecycle-only paired skill contracts.");
        if (p.Triggers.Count != (p.Id == "ol:hongji" ? 4 : 2)) throw new InvalidOperationException($"{path}: Zhang Shiping has an incomplete lifecycle contract.");
        foreach (var t in p.Triggers)
        {
            if (t.Effects.Count != 1 || !IsOwn(t.Effects[0].Op) || t.Effects[0].Target != SkillProgramEffectTarget.Owner ||
                t.Effects[0].Condition.Kind != SkillProgramConditionKind.Always || t.Effects[0].ActorReference is not null || t.Effects[0].TargetReference is not null ||
                t.Subject != SkillProgramTriggerSubject.Owner || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null ||
                t.OwnerRelation is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
                t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.MovementReasons.Count != 0 || t.MarkerCost is not null ||
                t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.DynamicUsageLimit is not null || t.AllowNoEventTarget || t.DeferredTurnEndOnly)
                throw new InvalidOperationException($"{path}: Zhang Shiping requires one zero-input owner operation without unrelated event filters.");
            var valid = t.Effects[0].Op switch
            {
                SkillProgramEffectOp.ArmHongjiNextPhase => p.Id == "ol:hongji" && t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && t.Optional &&
                    t.TurnOwnerScope is SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving,
                SkillProgramEffectOp.ResolveHongjiGrantedPhase => p.Id == "ol:hongji" && !t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.AnyLiving &&
                    t.Window is SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.PlayEnding,
                SkillProgramEffectOp.InitializeXingGuMountPile => p.Id == "ol:xinggu" && t.Window == SkillProgramTriggerWindow.GameStarting && !t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
                SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond => p.Id == "ol:xinggu" && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
                _ => false
            };
            if (!valid) throw new InvalidOperationException($"{path}: Zhang Shiping operation is in the wrong native lifecycle window.");
        }
        if (p.Id == "ol:hongji" && (p.Triggers.Count(t => t.Effects[0].Op == SkillProgramEffectOp.ArmHongjiNextPhase && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own) != 1 ||
            p.Triggers.Count(t => t.Effects[0].Op == SkillProgramEffectOp.ArmHongjiNextPhase && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving) != 1 ||
            p.Triggers.Count(t => t.Effects[0].Op == SkillProgramEffectOp.ResolveHongjiGrantedPhase && t.Window == SkillProgramTriggerWindow.DrawPhaseEnded) != 1 ||
            p.Triggers.Count(t => t.Effects[0].Op == SkillProgramEffectOp.ResolveHongjiGrantedPhase && t.Window == SkillProgramTriggerWindow.PlayEnding) != 1))
            throw new InvalidOperationException($"{path}: Hongji requires exactly one own/foreign preparation and draw/play issued-phase pair.");
        if (p.Id == "ol:xinggu" && (p.Triggers.Count(t => t.Effects[0].Op == SkillProgramEffectOp.InitializeXingGuMountPile) != 1 ||
            p.Triggers.Count(t => t.Effects[0].Op == SkillProgramEffectOp.PlaceXingGuMountAndObtainDiamond) != 1))
            throw new InvalidOperationException($"{path}: Xinggu requires exactly one game-start initialization and one own-ending placement.");
    }
}
