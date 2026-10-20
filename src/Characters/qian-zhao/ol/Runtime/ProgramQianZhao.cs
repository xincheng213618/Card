using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WeifuStage { PaymentChildren, Judging, JudgmentChildren, DrawChildren }
public sealed record WeifuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, int ActualTurn,
    int TurnOwnerSeat, int CostCardId, CardLocation CostFrom, SkillProgramCardCategory CostCategory, long Before, long After,
    WeifuStage Stage = WeifuStage.PaymentChildren, long? JudgmentId = null, int? ResultCardId = null,
    CardKind? ResultKind = null, SkillProgramCardCategory? ResultCategory = null, bool Issued = false, int DrawCount = 0);
public sealed record WeifuTargetDraft(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long UseId, long ActionId, int Maximum)
{
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WeifuUseReceipt(long UseId, long ActionId, int ActorSeat, int ActualTurn, int TurnOwnerSeat,
    SkillProgramCardCategory Category, bool Offered = false)
{
    public int ProviderSeat { get; init; }
    private readonly IReadOnlyList<WeifuGrantedEvent> _grants = Array.AsReadOnly(Array.Empty<WeifuGrantedEvent>());
    private readonly IReadOnlyList<int> _original = Array.AsReadOnly(Array.Empty<int>()), _added = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<WeifuGrantedEvent> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
public enum KuansaiStage { Target, Benefit, Children }
public sealed record KuansaiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int TurnOwnerSeat, long UseId, long ActionId, int ThresholdHp, KuansaiStage Stage = KuansaiStage.Target,
    int? ChooserSeat = null, bool? Gift = null, int? CardId = null, CardLocation? From = null, long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeifuReceipt? Weifu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeifuTargetDraft? WeifuTargets { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KuansaiReceipt? Kuansai { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WeifuUseReceipt? WeifuUse { get; init; }
}
public sealed record WeifuPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, int CardId, CardLocation From, SkillProgramCardCategory Category, long Before, long After) : IGameEvent;
public sealed record WeifuJudgedEvent(long FrameId, long JudgmentId, int CardId, CardKind Kind, SkillProgramCardCategory Category) : IGameEvent;
public sealed record WeifuGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, SkillProgramCardCategory Category, long JudgmentId) : IGameEvent;
public sealed record WeifuGrantConsumedEvent(long GrantFrameId, long UseId, long ActionId, int ActorSeat, SkillProgramCardCategory Category, bool Counterspell, CardKind? ResponseKind = null) : IGameEvent;
public sealed record WeifuGrantExpiredEvent(long GrantFrameId, int ActualTurn, int TurnOwnerSeat) : IGameEvent;
public sealed record WeifuDrawPaidEvent(long FrameId, int Count) : IGameEvent;
public sealed record WeifuUseIssuedEvent(long UseId, long ActionId, int ActorSeat, int ActualTurn, int TurnOwnerSeat,
    SkillProgramCardCategory Category, int GrantCount, int ProviderSeat) : IGameEvent;
public sealed record WeifuActorChangedEvent(long UseId, long ActionId, long ProgramFrameId, CardConversionSource Producer,
    string GameplayHash, int InstructionIndex, int PreviousActor, int Actor, int Provider) : IGameEvent;
public sealed record WeifuTargetAddedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, int TargetSeat, int Index) : IGameEvent;
public sealed record WeifuRedirectedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int TargetIndex, int PreviousSeat, int TargetSeat) : IGameEvent;
public sealed record KuansaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, long UseId, long ActionId, int ThresholdHp, int TargetCount) : IGameEvent;
public sealed record KuansaiTargetChosenEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record KuansaiPaidEvent(long FrameId, int ChooserSeat, bool Gift, int? CardId, CardLocation? From, long Before, long After) : IGameEvent;
public sealed record QianZhaoCompletedEvent(long FrameId, string SkillId) : IGameEvent;

internal interface IQianZhaoHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class QianZhaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new QianZhaoHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveWeifuJudgmentDescriptor() : QianZhaoDescriptor(SkillProgramEffectOp.ResolveWeifuJudgment)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ConsumeSelectedCards(1)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (e, c) => c.DiscardSelected(e));
}
internal sealed class OfferWeifuCategoryTargetsDescriptor() : QianZhaoDescriptor(SkillProgramEffectOp.OfferWeifuCategoryTargets)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]; }
internal sealed class ResolveKuansaiTargetChoiceDescriptor() : QianZhaoDescriptor(SkillProgramEffectOp.ResolveKuansaiTargetChoice)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]; }
internal sealed class QianZhaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IQianZhaoHost>().Execute(f, op);
}
internal static class QianZhaoComposition
{
    [ProgramCatalogValidation(210)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var ops = new[] { SkillProgramEffectOp.ResolveWeifuJudgment, SkillProgramEffectOp.OfferWeifuCategoryTargets, SkillProgramEffectOp.ResolveKuansaiTargetChoice };
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => ops.Contains(e.Op))) return;
        if (p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.Contributions.Count != 0 || p.CardIdentities.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Qian Zhao requires only its exact native paid judgment and finalized-target instructions.");
        if (p.Id == "ol:weifu")
        {
            if (p.Activations is not [var a] || p.Triggers is not [var t] || a.Id != "discard-and-judge" || a.UsageGroup != a.Id ||
                a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 0 || a.MaxTargets != 0 || a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null ||
                !a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) || a.TargetKind != SkillProgramTargetKind.AnyLiving || a.Condition.Kind != SkillProgramConditionKind.Always ||
                a.Effects is not [{ Op: SkillProgramEffectOp.ResolveWeifuJudgment, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
                t.Id != "issued-category-targets" || t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Optional || t.Priority != 1000 ||
                t.Effects is not [{ Op: SkillProgramEffectOp.OfferWeifuCategoryTargets, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
                throw new InvalidOperationException(path + ": Weifu needs one unlimited HE cost and its mandatory issued next-category target window.");
        }
        else if (p.Id != "ol:kuansai" || p.Activations.Count != 0 || p.Triggers is not [var k] || k.Id != "actual-target-choice" ||
            k.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || k.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !k.Optional || k.Priority != -1000 ||
            k.UsageScope != SkillUsageScope.Turn || k.UsageLimit != 1 ||
            k.Effects is not [{ Op: SkillProgramEffectOp.ResolveKuansaiTargetChoice, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Kuansai needs one optional once-per-actual-turn observer of finalized card targets.");
        foreach (var t in p.Triggers)
            if (t.Subject is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
                t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.ExcludedReasons.Count != 0 ||
                t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
                t.MarkerCost is not null || t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.DynamicUsageLimit is not null || t.GainPhaseQualification is not null ||
                t.MovementOccurrence is not null || t.DamageOccurrence is not null || t.IncludeResponseUses || t.SingleActionInstance || t.AllowNoEventTarget || t.EvaluateConditionAtResolution ||
                t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets || t.NoDyingAtActivation || t.AllowOwnDiscardPhaseEnded || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly ||
                t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
                p.Id == "ol:weifu" && (t.UsageScope is not null || t.UsageLimit is not null))
                throw new InvalidOperationException(path + ": Qian Zhao cannot add unrelated filters or usage policy to these exact entry contracts.");
    }
}
