using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record DiouUseClaim(CardConversionSource Source, string GameplayHash, long NativeParentId,
    long ActionId, int ActorSeat, int ProviderSeat, CardKind EffectiveKind, int ActualTurn, int TurnOwner,
    int MaterialCount, int TanbanMaterialCount, long MovementSequence);
public enum DiouStage { ShowChoice, UseChoice, NativeIssuing, NativeUse, NativeChildren, Drawing, DrawChildren }
public sealed record DiouUsePlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId,
    CardKind? RequiredKind, string Description, bool Adjusted = false)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DiouReceipt(DiouUseClaim Claim, long WindowId)
{
    public DiouStage Stage { get; init; }
    public bool Paid { get; init; }
    public int ShownCardId { get; init; }
    public CardKind ShownPrintedKind { get; init; }
    public CardKind OutputKind { get; init; }
    public bool FirstShow { get; init; }
    public bool SameName { get; init; }
    public int ActualTurn { get; init; }
    public int TurnOwner { get; init; }
    public long ShowSequence { get; init; }
    public DiouUsePlan? Plan { get; init; }
    public int CarrierId { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<long> _nativeBatchIds = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatchIds { get => _nativeBatchIds; init => _nativeBatchIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record DiouNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActorSeat, CardKind Kind, int CarrierId);
public abstract partial record ResolutionFrame
{
    private readonly IReadOnlyList<DiouUseClaim>? _diouClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DiouUseClaim>? DiouClaims
    { get => _diouClaims; init => _diouClaims = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DiouReceipt? Diou { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DiouNativeOrigin? DiouOrigin { get; init; } }
public sealed partial record DeclaredCardPayment
{
    private readonly IReadOnlyList<DiouUseClaim>? _diouDeclarationClaims;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DiouUseClaim>? DiouDeclarationClaims
    { get => _diouDeclarationClaims; init => _diouDeclarationClaims = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record DiouQualificationAcceptedEvent(long NativeParentId, long ActionId, int ActorSeat,
    int ProviderSeat, CardKind Kind, int MaterialCount) : IGameEvent;
public sealed record DiouUseClaimedEvent(DiouUseClaim Claim) : IGameEvent;
public sealed record DiouMaterialQualifiedEvent(long NativeParentId, long ActionId, CardConversionSource Source,
    int Index, int CardId, CardKind PrintedKind, CardLocation From, bool Tanban) : IGameEvent;
public sealed record DiouPublicHandShownEvent(int ActualTurn, int TurnOwner, long RevealFrameId,
    int HandOwnerSeat, int CardId, CardKind PrintedKind, long MovementSequence) : IGameEvent;
public sealed record DiouStartedEvent(long FrameId, DiouUseClaim Claim, long WindowId) : IGameEvent;
public sealed record DiouShownEvent(long FrameId, int OwnerSeat, int CardId, CardKind PrintedKind,
    CardKind OutputKind, int ActualTurn, int TurnOwner, long MovementSequence, bool FirstShow, bool SameName) : IGameEvent;
public sealed record DiouCarrierCreatedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int CardId, CardKind Kind) : IGameEvent;
public sealed record DiouPlanAcceptedEvent(long FrameId, CardKind Kind, LegalActionKind ActionKind,
    int? TargetCardId, CardKind? RequiredKind, bool Adjusted, int TargetCount) : IGameEvent;
public sealed record DiouTargetAcceptedEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record DiouNativeIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat,
    CardKind Kind, int CarrierId) : IGameEvent;
public sealed record DiouNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record DiouParentAttackRestoredEvent(long FrameId, long ChildUseId, long OriginalUseId,
    long OriginalActionId) : IGameEvent;
public sealed record DiouRescueAttackReturnedEvent(long FrameId, long ChildUseId, long DyingFrameId,
    long AttackOwnerFrameId, long? AttackActionId) : IGameEvent;
public sealed record DiouNativeBatchSignedEvent(long FrameId, long UseId, long BatchId,
    long? ParentBatchId, int Count) : IGameEvent;
public sealed record DiouNativeMovementSignedEvent(long FrameId, long UseId, long BatchId, int CardId,
    CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record DiouDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount,
    int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record DiouCompletedEvent(long FrameId, long OriginalActionId, bool Shown, bool Used,
    bool Reward) : IGameEvent;

internal interface ILeDiaoChanDiouHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveDiouTanbanUseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDiouTanbanUse;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new DiouHandler();
}
internal sealed class DiouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDiouTanbanUse;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<ILeDiaoChanDiouHost>().Resolve(frame);
}
internal static class LeDiaoChanDiouComposition
{
    [ProgramCatalogValidation(1160)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "le:diou" && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveDiouTanbanUse)) return;
        if (p.Id != "le:diou" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers is not [var t] || t.Id != "completed-tanban-use" ||
            t.Window != SkillProgramTriggerWindow.CardUseCompleted || !t.IncludeResponseUses || !t.SingleActionInstance ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Subject is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.UsageLimit is not null || t.UsageScope is not null || t.DynamicUsageLimit is not null ||
            t.NamedUsageGroup is not null || t.MarkerCost is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveDiouTanbanUse, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Diou requires one completed actual Tanban Use, including true response Uses, its public retained-Hand reveal and a genuine same-name native Use without added quotas.");
    }
}
