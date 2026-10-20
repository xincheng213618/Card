using System.Text.Json.Serialization;
namespace CardGame.Core;
internal interface IWuJingHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class WuJingDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler { get; } = new WuJingHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.UseHejiAtCompletedSingleTarget ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash ? SkillProgramTriggerWindow.DiscardPileReceived : SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
}
internal sealed class WuJingHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IWuJingHost>().Resolve(f, op);
}
public enum WuJingStage { UseChoice, FactionRequest, IssuingUse, NativeUse, NativeChildren, RewardChildren, ClaimChildren }
public sealed record WuJingMaterial(int CardId, CardKind PrintedKind, CardLocation From, long Sequence = 0);
public sealed record WuJingUseSelection(CardKind Kind, bool Zhangba, bool UsesFan)
{
    private IReadOnlyList<int> _ids = []; private IReadOnlyList<CardConversionSource> _sources = [];
    public IReadOnlyList<int> CardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardConversionSource> Conversions { get => _sources; init => _sources = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WuJingReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long WindowId, int ActualTurn, WuJingStage Stage,
    long UseId, long ActionId, int TargetSeat, WuJingUseSelection? Selection = null, WuJingHejiOrigin? NativeOrigin = null, bool NativeCompleted = false,
    long Before = 0, long After = 0, int? RewardCardId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FactionResponsePolicySource? FactionPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RoundOnceCardlessBasicPaymentReceipt? RoundOncePayment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DrawFundedDistinctBasicPayment? DrawFundedPayment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ChainedStateBasicPayment? ChainedPayment { get; init; }
    private IReadOnlyList<WuJingMaterial> _entities = [];
    public IReadOnlyList<WuJingMaterial> Entities { get => _entities; init => _entities = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuJingReceipt? WuJing { get; init; } }
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuJingHejiUseState? WuJingHeji { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuJingLiubingAppearance? WuJingLiubing { get; init; }
}
public sealed record WuJingActualPhysicalSlashEvent(long UseId, long ActionId, int ActualTurn, int TurnOwner, int ActorSeat, int CardId, CardKind Kind, int Ordinal) : IGameEvent;
public sealed record WuJingUseMaterialEvent(long UseId, long ActionId, int ActualTurn, long? PlayToken, int OriginalActor, int Index, int Count, int CardId, CardKind PrintedKind, CardLocation From, long Before) : IGameEvent;
public sealed record WuJingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long WindowId, int ActualTurn, long UseId, long ActionId, int TargetSeat) : IGameEvent;
public sealed record WuJingRewardIssuedEvent(long FrameId, long ProgramFrameId, long UseId, int? CardId, long Before, long After) : IGameEvent;
public sealed record WuJingClaimIssuedEvent(long FrameId, long UseId, long ActionId, long BatchId, int Count, long Before, long After) : IGameEvent;
public sealed record WuJingCompletedEvent(long FrameId, bool Applied) : IGameEvent;
internal static class WuJingComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.UseHejiAtCompletedSingleTarget or SkillProgramEffectOp.RewardHejiUnconvertedUse or SkillProgramEffectOp.ClaimLiubingUndamagingSlash;
    [ProgramCatalogValidation(380)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op))) && !p.Activations.Any(a => a.Effects.Any(e => Handles(e.Op)))) return;
        if (p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations ||
            (p.Id == "ol:heji" ? p.Triggers.Count != 2 || p.Triggers.Count(t => t.Effects is [{ Op: SkillProgramEffectOp.UseHejiAtCompletedSingleTarget }]) != 1 || p.Triggers.Count(t => t.Effects is [{ Op: SkillProgramEffectOp.RewardHejiUnconvertedUse }]) != 1 : p.Id != "ol:liubing" || p.Triggers is not [{ Effects: [{ Op: SkillProgramEffectOp.ClaimLiubingUndamagingSlash }] }]))
            throw new InvalidOperationException(path + ": Wu Jing requires complete native follow-up/reward and exact used-entity acquisition instructions.");
        foreach (var t in p.Triggers)
        {
            if (t.Effects is not [var e] || !Handles(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.DamageOccurrence is not null || t.MovementOccurrence is not null || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 || t.DamageCardKinds.Count != 0 || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.GainPhaseQualification is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.DeferredTurnEndOnly || t.IncludeResponseUses || t.SingleActionInstance || t.OnlyDesignatedCardTargets || t.AllowNoEventTarget || t.AllowOwnDiscardPhaseEnded || t.NoDyingAtActivation || t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
                (e.Op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash ? t.Id != "claim-native-undamaging-slash" || t.Window != SkillProgramTriggerWindow.DiscardPileReceived || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || t.CardKinds.Count != 0 || t.Optional || t.Priority != 0 :
                 t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.Subject is not null || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !t.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel]) ||
                 (e.Op == SkillProgramEffectOp.UseHejiAtCompletedSingleTarget ? t.Id != "completed-single-target-followup" || !t.Optional || t.Priority != 0 : t.Id != "issued-followup-red-reward" || t.Optional || t.Priority != 1000)))
                throw new InvalidOperationException(path + ": Wu Jing requires exact unfiltered completed-use or settled-material windows with their mandatory/optional timing.");
        }
    }
}
