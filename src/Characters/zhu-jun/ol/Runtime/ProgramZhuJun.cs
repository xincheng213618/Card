using System.Text.Json.Serialization;
namespace CardGame.Core;

// All persistent evidence is scalar. The native use owns its issued damage;
// the skill frame owns only the one draw and its suspended native children.
public sealed record ZhuJunCuipoQualifiedEvent(long OriginFrameId, long? ActionId, CardConversionSource Source, string GameplayHash,
    int Turn, int TurnOwner, CardKind EffectiveKind, int Ordinal, int NameLength, bool Damage) : IGameEvent;
public sealed record ZhuJunCuipoDamageReceipt(long UseId, long? ActionId, CardConversionSource Source, string GameplayHash,
    int Turn, int TurnOwner, CardKind EffectiveKind, int Ordinal, int NameLength, int ProviderSeat);
public sealed record ZhuJunCuipoDamageState
{
    private IReadOnlyList<ZhuJunCuipoDamageReceipt> _grants = [];
    public IReadOnlyList<ZhuJunCuipoDamageReceipt> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ZhuJunCuipoDrawReceipt(ZhuJunCuipoQualifiedEvent Qualification, long WindowId, int InstructionIndex,
    long Before, long After, int ActualCount);
public sealed record ZhuJunCuipoDrawIssuedEvent(long FrameId, long WindowId, long OriginFrameId, long? ActionId,
    CardConversionSource Source, long Before, long After, int ActualCount) : IGameEvent;
public sealed record ZhuJunCuipoDrawCompletedEvent(long FrameId, long OriginFrameId, long? ActionId) : IGameEvent;
public sealed record ZhuJunLegacyAlcoholReturn(long UseId, long ProgramParentId, long DyingId, int InstructionIndex,
    CardConversionSource ProducerSource, string ProducerGameplayHash, long ActionId, bool Returned = false);
public sealed record ZhuJunLegacyAlcoholWindowIssuedEvent(ZhuJunLegacyAlcoholReturn Return) : IGameEvent;
public sealed record ZhuJunLegacyAlcoholReturnedEvent(long UseId, long WindowId, long ProgramParentId, long DyingId) : IGameEvent;
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhuJunCuipoDrawReceipt? ZhuJunCuipoDraw { get; init; } }
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhuJunCuipoDamageState? ZhuJunCuipoDamage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhuJunLegacyAlcoholReturn? ZhuJunLegacyAlcohol { get; init; }
}
internal interface IZhuJunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveCuipoActualTurnUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveCuipoActualTurnUse;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuJunCuipoHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireCardActionActor()];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
}
internal sealed class ZhuJunCuipoHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveCuipoActualTurnUse;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IZhuJunHost>().Resolve(f);
}
internal sealed class ZhuJunCommittedUseContract : IProgramCommittedUseContract
{ public string SkillId => "ol:cuipo"; public void ValidateTrigger(SkillProgramTrigger t) => ZhuJunComposition.ValidateTrigger("ol:cuipo", t); }
internal static class ZhuJunComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses || t.Optional ||
            t.Subject is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveCuipoActualTurnUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.MarkerCost is not null ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 ||
            t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null || t.DamageOccurrence is not null || t.MovementOccurrence is not null || t.RequireDamageSource is not null ||
            t.RequireNoCardConversion is not null || t.GainPhaseQualification is not null || t.SingleActionInstance || t.AllowNoEventTarget || t.NoDyingAtActivation || t.DeferredTurnEndOnly ||
            t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly)
            throw new InvalidOperationException(path + ": Cuipo requires the mandatory actual-use actor, including genuine response uses, with one exact native instruction.");
    }
    [ProgramCatalogValidation(300)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveCuipoActualTurnUse)) && !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveCuipoActualTurnUse))) return;
        if (p.Id != "ol:cuipo" || p.Activations.Count != 0 || p.Triggers is not [var trigger] || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 ||
            p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Cuipo requires its complete standalone ordinary OL skill.");
        ValidateTrigger(path, trigger);
    }
}
