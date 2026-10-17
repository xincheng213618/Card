using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ShuangjiaInitialEntity(int OwnerSeat, int CardId, CardKind PrintedKind, long DealSequence);

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<ShuangjiaInitialEntity>? _shuangjiaInitialEntities;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShuangjiaInitialEntity>? ShuangjiaInitialEntities
    {
        get => _shuangjiaInitialEntities;
        init => _shuangjiaInitialEntities = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record ShuangjiaInitializedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int EntityCount) : IGameEvent;
public sealed record ShuangjiaInitialEntityMarkedEvent(long FrameId, int OriginalOwnerSeat, int CardId,
    CardKind PrintedKind, long EntrySequence) : IGameEvent;

internal interface ILeCaiWenJiShuangjiaHost
{
    SkillProgramStepOutcome InitializeShuangjia(ProgramSkillFrame frame);
}

internal sealed class InitializeShuangjiaInitialEntitiesDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeShuangjiaInitialEntities;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
    public override ISkillProgramEffectHandler Handler { get; } = new InitializeShuangjiaInitialEntitiesHandler();
}

internal sealed class InitializeShuangjiaInitialEntitiesHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeShuangjiaInitialEntities;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) =>
        host.GetCapability<ILeCaiWenJiShuangjiaHost>().InitializeShuangjia(frame);
}

internal static class ShuangjiaComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.InitializeShuangjiaInitialEntities)) return;
        if (p.Id != "le:shuangjia" || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Contributions.Count != 0 || p.Triggers is not [var initial] ||
            initial.Id != "mark-initial-entities" || initial.Window != SkillProgramTriggerWindow.GameStarting ||
            initial.Effects is not [{ Op: SkillProgramEffectOp.InitializeShuangjiaInitialEntities,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            !PlainInitial(initial))
            throw new InvalidOperationException(path + ": Shuangjia requires its exact native opening-entity initialization.");
    }

    private static bool PlainInitial(SkillProgramTrigger t) =>
        t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null &&
        t.UsageScope is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.OwnerRelation is null && t.SourceSkillId is null && t.SourceViewAsId is null &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 &&
        t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.MovementOccurrence is null && !t.MovementDiscardOnly && !t.IncludeResponseUses && !t.SingleActionInstance &&
        !t.DeferredTurnEndOnly && !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null &&
        t.DamageOccurrence is null && !t.NoDyingAtActivation && t.RequireDamageSource is null &&
        t.RequireNoCardConversion is null && !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets &&
        !t.AllowOwnDiscardPhaseEnded;
}
