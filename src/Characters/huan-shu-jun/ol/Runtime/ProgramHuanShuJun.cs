using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum HuanShuJunStage { AdjustHand, AdjustmentChildren, BasicChoice, WenyiChoice, WenyiPeachRequest, EquipmentGiftChildren, NativeIssuing, NativeUse, NativeChildren, FaceChildren, ChainChildren, Drawing, DrawChildren }
public enum HuanpeiIntent { Play, OwnSlashDodge, Dying, BorrowedSword, Qinglong, ProgramSlash, ProgramNearestSlash, AssistedSlash, NearestLegalSlash, WenyiPeach, FixedSlash, KeBiNengBasic }
public sealed record HuanpeiNativeRequest(HuanpeiIntent Intent, long? ParentFrameId, long? RequestFrameId,
    long? ParentActionId, int ActorSeat, int Cursor, CardKind? RequiredKind, int? FixedTargetSeat,
    PromptId OriginalPromptId, long OriginalRevision)
{
    public string? ParentSkillId { get; init; }
    public string? ParentSkillInstanceId { get; init; }
    public string? ParentGameplayHash { get; init; }
    public SkillProgramEffectOp? ParentOperation { get; init; }
}
public sealed record HuanShuJunMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record HuanpeiPaidOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int Round, int ActualTurn, int TurnOwner, HuanpeiNativeRequest Request, int FrozenHandCount,
    int FrozenHp, int AdjustmentCount, long SequenceBefore, long SequenceAfter, CardKind Kind)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HuanpeiUseReceipt(HuanpeiPaidOrigin Payment, long OwnerFrameId, long CardActionId);
public sealed record HuanpeiProgramReturn(HuanpeiPaidOrigin Payment, long UseId, long ActionId, bool Returned = false);
public sealed record WenyiNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActorSeat, int ProviderSeat, int TargetSeat, CardKind Kind, long UseId, long ActionId, CardActionContext OriginalAction);
public sealed record HuanShuJunReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int ActualTurn, int TurnOwner, HuanShuJunStage Stage)
{
    public bool Paid { get; init; }
    public int TargetSeat { get; init; } = -1;
    public int FrozenHandCount { get; init; }
    public int FrozenHp { get; init; }
    public int RequestedCount { get; init; }
    public bool RefreshWenyi { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int Round { get; init; }
    public CardKind? Kind { get; init; }
    public HuanpeiNativeRequest? Request { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public WenyiNativeOrigin? Native { get; init; }
    public bool NativeReturned { get; init; }
    private readonly IReadOnlyList<CardConversionSource> _sources = Array.AsReadOnly(Array.Empty<CardConversionSource>());
    public IReadOnlyList<CardConversionSource> Sources { get => _sources; init => _sources = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<HuanShuJunMaterial> _materials = Array.AsReadOnly(Array.Empty<HuanShuJunMaterial>());
    public IReadOnlyList<HuanShuJunMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    private PendingDecision? _originalDecision;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PendingDecision? OriginalDecision { get => _originalDecision; init => _originalDecision = value is null ? null : RequestedDeckBasicFrame.FreezeDecision(value); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuanShuJunReceipt? HuanShuJun { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuanpeiProgramReturn? HuanpeiReturn { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuanpeiUseReceipt? HuanpeiUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuanpeiUseReceipt? HuanpeiResponse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WenyiNativeOrigin? WenyiUse { get; init; }
}
public sealed record HuanShuJunStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, int TargetSeat, int FrozenHandCount, int FrozenHp) : IGameEvent;
public sealed record HuanpeiAdjustmentAcceptedEvent(long FrameId, int OwnerSeat, int Round, int HandCount, int Hp, int RequestedCount, long Before) : IGameEvent;
public sealed record HuanpeiCostEntityEvent(long FrameId, int Index, int CardId, CardKind Kind, CardLocation From, long Sequence) : IGameEvent;
public sealed record HuanpeiAdjustmentCompletedEvent(long FrameId, long Before, long After, int ActualDrawCount, int ReplacementCount) : IGameEvent;
public sealed record HuanpeiOutputChosenEvent(long FrameId, CardKind Kind, int TargetCount) : IGameEvent;
public sealed record HuanpeiTargetChosenEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record HuanpeiIssuedEvent(long PaymentFrameId, long UseId, long ActionId, int ActorSeat, CardKind Kind, HuanpeiIntent Intent) : IGameEvent;
public sealed record HuanpeiReturnedEvent(long PaymentFrameId, long UseId, long ActionId) : IGameEvent;
public sealed record HuanpeiRequestCapturedEvent(long FrameId, HuanpeiNativeRequest Request, string DecisionHash) : IGameEvent;
public sealed record HuanpeiKeActionAcceptedEvent(long ParentFrameId, int Cursor, CardConversionSource ParentSource, string ParentHash, long PaymentFrameId, CardKind Kind) : IGameEvent;
public sealed record HuanpeiCancelledEvent(long FrameId, bool Paid) : IGameEvent;
public sealed record WenyiPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, long HealthWindowId, string Branch) : IGameEvent;
public sealed record WenyiEquipmentGivenEvent(long FrameId, int CardId, int RecipientSeat, long Sequence) : IGameEvent;
public sealed record WenyiNativeIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat, int ProviderSeat, int TargetSeat, CardKind Kind) : IGameEvent;
public sealed record WenyiNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record WenyiRestorationIssuedEvent(long FrameId, int TargetSeat, bool FaceState) : IGameEvent;
public sealed record MiaoxinBenefitIssuedEvent(long FrameId, int OwnerSeat, long DamageWindowId, int Occurrence, int DrawCount, bool RefreshWenyi) : IGameEvent;
public sealed record MiaoxinWenyiRefreshedEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record HuanShuJunDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record HuanShuJunCompletedEvent(long FrameId, SkillProgramEffectOp Op, bool Paid) : IGameEvent;

internal interface IHuanShuJunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class HuanShuJunDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch
    {
        SkillProgramEffectOp.ResolveWenyiHealthOneAid => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHealthChanged)],
        SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        _ => []
    };
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new HuanShuJunHandler(op);
}
internal sealed class ResolveHuanpeiHandAdjustmentAndBasicUseDescriptor() : HuanShuJunDescriptor(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse){
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveWenyiHealthOneAidDescriptor() : HuanShuJunDescriptor(SkillProgramEffectOp.ResolveWenyiHealthOneAid){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveMiaoxinDamageDrawAndRefreshDescriptor() : HuanShuJunDescriptor(SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class HuanShuJunHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IHuanShuJunHost>().Resolve(frame, op);
}
internal static class HuanShuJunComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse or SkillProgramEffectOp.ResolveWenyiHealthOneAid or SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh;
    [ProgramCatalogValidation(800)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        if (p.Id is not ("ol:huanpei" or "ol:wenyi" or "ol:miaoxin") || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0)
            throw new InvalidOperationException(path + ": Huan Shu Jun requires her complete independent adjustment, aid and damage-point contracts.");
        if (p.Id == "ol:huanpei")
        {
            if (p.Triggers.Count != 0 || p.Activations is not [{ Id: "adjust-and-use-basic", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerTurn: null, Effects: [{ Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }])
                throw new InvalidOperationException(path + ": Huanpei requires its own zero-input adjustment activation and a genuine native basic request.");
            return;
        }
        if (p.Activations.Count != 0 || p.Triggers is not [var t] || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null || t.MarkerCost is not null || t.OwnerRelation is not null || t.IncludeResponseUses || t.SingleActionInstance || t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            (p.Id == "ol:wenyi" ? t.Id != "health-becomes-one" || t.Window != SkillProgramTriggerWindow.AfterHealthChanged || t.Subject != SkillProgramTriggerSubject.Owner || !t.Optional || t.DamageOccurrence is not null || e.Op != SkillProgramEffectOp.ResolveWenyiHealthOneAid : t.Id != "each-damage-point" || t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamagePoint || e.Op != SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh))
            throw new InvalidOperationException(path + ": Wenyi and Miaoxin require exact genuine health-one and per-point native damage windows.");
    }
}
