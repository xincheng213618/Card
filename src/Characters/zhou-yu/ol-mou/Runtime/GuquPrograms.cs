using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record GuquUseClaim(CardConversionSource Source, string GameplayHash, long ParentFrameId,
    long ActionId, int ActorSeat, int ProviderSeat, CardKind EffectiveKind, int ActualTurn,
    int TurnOwner, long MovementSequence, int QueMaterialCount);
public sealed record GuquUseAdmission(long UseId, long ActionId, int ActorSeat, int ProviderSeat, CardKind EffectiveKind,
    int ActualTurn, int TurnOwner, long MovementSequence, int MaterialCount, long? DeclarationId);
public sealed record GuquDeclarationPermit(long DeclarationId, int ActorSeat, int ProviderSeat, int CardId,
    CardKind PrintedKind, CardLocation From, CardConversionSource Conversion, CardKind OutputKind,
    int ActualTurn, int TurnOwner, long MovementSequence);
public enum GuquStage { Drawing, DrawChildren }
public sealed record GuquReceipt(GuquUseClaim Claim, long WindowId, GuquStage Stage)
{
    public NativeDrawOutcome? DrawOutcome { get; init; }
}
public abstract partial record ResolutionFrame
{
    private readonly IReadOnlyList<GuquUseClaim>? _guquClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GuquUseClaim>? GuquClaims
    { get => _guquClaims; init => _guquClaims = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<GuquUseAdmission>? _guquResponseAdmissions;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GuquUseAdmission>? GuquResponseAdmissions
    { get => _guquResponseAdmissions; init => _guquResponseAdmissions = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GuquUseAdmission? GuquAdmission { get; init; } }
public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GuquDeclarationPermit? GuquPermit { get; init; }
    private readonly IReadOnlyList<GuquUseClaim>? _guquDeclarationClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GuquUseClaim>? GuquDeclarationClaims
    { get => _guquDeclarationClaims; init => _guquDeclarationClaims = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GuquReceipt? Guqu { get; init; } }

public sealed record GuquQueMarkedEvent(CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwner, long RevealFrameId, int HandOwnerSeat, int CardId, CardKind PrintedKind,
    long MovementSequence) : IGameEvent;
public sealed record GuquUseAcceptedEvent(GuquUseAdmission Admission) : IGameEvent;
public sealed record GuquDeclarationCapturedEvent(GuquDeclarationPermit Permit) : IGameEvent;
public sealed record GuquUseClaimedEvent(GuquUseClaim Claim) : IGameEvent;
public sealed record GuquUseMaterialClaimedEvent(long ParentFrameId, long ActionId, CardConversionSource Source,
    int Index, int CardId, CardKind PrintedKind, CardLocation From) : IGameEvent;
public sealed record GuquStartedEvent(long FrameId, GuquUseClaim Claim, long WindowId) : IGameEvent;
public sealed record GuquDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount,
    int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record GuquCompletedEvent(long FrameId, long ActionId) : IGameEvent;

internal interface IMouZhouYuGuquHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveMouZhouYuGuquDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuGuqu;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new MouZhouYuGuquHandler();
}
internal sealed class MouZhouYuGuquHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuGuqu;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IMouZhouYuGuquHost>().Resolve(frame);
}
internal static class MouZhouYuGuquComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "mou-ol:guqu" && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveMouZhouYuGuqu)) return;
        if (p.Id != "mou-ol:guqu" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.Triggers is not [var t] ||
            t.Id != "completed-que-use" || t.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.Optional || t.Subject is not null ||
            !t.IncludeResponseUses || !t.SingleActionInstance || t.AllowNoEventTarget || t.OnlyDesignatedCardTargets ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.UsageLimit is not null || t.UsageScope is not null ||
            t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.ChoiceGroup is not null || t.OrderGroup is not null ||
            t.MarkerCost is not null || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementOccurrence is not null ||
            t.Suits.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 ||
            t.DamageOccurrence is not null || t.GainPhaseQualification is not null || t.EvaluateConditionAtResolution ||
            t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.NoDyingAtActivation || t.DeferredTurnEndOnly ||
            t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.AllowOwnDiscardPhaseEnded ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouZhouYuGuqu, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Guqu requires all public Hand reveals and one exact completed true Use draw, including native response Uses and excluding pure Played actions.");
    }
}
