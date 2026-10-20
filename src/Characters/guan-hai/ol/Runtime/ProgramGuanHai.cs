using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramXieweiBaitBasicUsePolicy(string PileSkillId);
internal static class XieweiBaitBasicUseSchema
{
    internal static ProgramXieweiBaitBasicUsePolicy Parse(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 1 ||
            !node.TryGetProperty("pileSkillId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != "ol:xiewei")
            throw new InvalidOperationException(path + ": Xiewei requires its exact bait-pile skill.");
        return new("ol:xiewei");
    }
    internal static void Validate(SkillProgramViewAs r, string path)
    {
        if (r.XieweiBaitBasicUse is null) return;
        var dodge = r.OutputKind == CardKind.Dodge;
        if (r.XieweiBaitBasicUse.PileSkillId != "ol:xiewei" || r.OutputKind is not (CardKind.Slash or CardKind.Dodge) ||
            r.InputCount != 2 || r.VariableInputCount || !r.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            r.InputKinds.Count != 0 || r.InputSuits.Count != 0 || r.InputCategories.Count != 0 ||
            !r.UseOnly || !r.ExtendedUse || !r.AllowSameKind || r.ForPlay == dodge || r.ForResponse != dodge ||
            r.Condition.Kind != SkillProgramConditionKind.Always || r.LeiluanBasicUse is not null || r.TieredRoundConversion is not null ||
            r.RoundDistinctBasicUse is not null || r.RoundOnceCardlessBasic is not null || r.DrawFundedDistinctBasic is not null ||
            r.ChainedStateCost is not null || r.RoundGainedRedHandOnly is not null || r.SourcePileSkillId is not null ||
            r.ConversionStateId is not null || r.CostDestination is not null || r.UsesPerPhase is not null || r.UsesPerActualTurn is not null ||
            r.UsageGroup is not null || r.SameSuit || r.AllHandCardsOfSelectedSuit || r.AllowChainedInput || r.DistanceUnlimited ||
            r.DamageBonus != 0 || r.RecoveryBonus != 0 || r.LastInSourceZone is not null || r.NoDying || r.DeclaredEntity ||
            r.DeclarationValidation is not null || r.SingleCardTrickUse || r.UnusedOutputThisTurn || r.UnusedOutputNameThisGame ||
            r.NameLedgerId is not null || r.DisableSkillUntilTurnEndIfNoDamage is not null || r.InheritPreviousPlaySuit ||
            r.NonBasicUnrespondableSlash || r.UseEffectiveInputSuit is not null || r.MinimumTier != 0 || r.MaximumTier != 2 ||
            r.ActivationUsageGroup is not null || r.ExcludeOwnerEffects)
            throw new InvalidOperationException(path + ": Xiewei requires exactly two Hand costs, an ordinary true Slash/Dodge and its original public-pile payment.");
    }
}

public sealed record GuanHaiMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record XieweiNativePayment(long Id, long UseId, CardConversionSource Source, string GameplayHash,
    int OwnerSeat, int ActorSeat, int? RequesterSeat, int ActualTurn, int TurnOwner, bool ResponseUse,
    CardKind Kind, PublicPersistentPileSource Pile, long Before)
{
    public long ActionId { get; init; }
    public long After { get; init; }
    public bool Paid { get; init; }
    private IReadOnlyList<GuanHaiMaterial> _materials = Array.AsReadOnly(Array.Empty<GuanHaiMaterial>());
    private IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<GuanHaiMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{
    private IReadOnlyList<XieweiNativePayment>? _xieweiPayments;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<XieweiNativePayment>? XieweiPayments
    { get => _xieweiPayments; init => _xieweiPayments = value is null ? null : Array.AsReadOnly(value.Select(p => p with { Materials = p.Materials, Movements = p.Movements }).ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YouqueSlashOrigin? YouqueSlash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? XieweiTargetMaximum { get; init; }
    private IReadOnlyList<int>? _xieweiOriginalTargets;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<int>? XieweiOriginalTargets { get => _xieweiOriginalTargets; init => _xieweiOriginalTargets = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public enum GuanHaiStage { XieweiChildren, ChooseBait, Pindian, PindianChildren, DiscardChildren, SlashIssuing, NativeSlash, NativeChildren, RewardDraw, RewardChildren }
public sealed record YouqueSlashOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActorSeat, int TargetSeat, long UseId, long ActionId);
public sealed record GuanHaiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActualTurn, int TurnOwner, GuanHaiStage Stage)
{
    public bool Paid { get; init; }
    public long ParentId { get; init; }
    public long PaymentId { get; init; }
    public int OpponentSeat { get; init; } = -1;
    public int BaitCardId { get; init; }
    public int BaitRank { get; init; }
    public long PindianId { get; init; }
    public long NativeUseId { get; init; }
    public long ActionId { get; init; }
    public long DamageFrameId { get; init; }
    public int LoserSeat { get; init; } = -1;
    public int WinnerSeat { get; init; } = -1;
    public bool NativeReturned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int ActualDrawCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PublicPersistentPileSource? Pile { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PindianResult? Result { get; init; }
    private IReadOnlyList<GuanHaiMaterial> _materials = Array.AsReadOnly(Array.Empty<GuanHaiMaterial>());
    public IReadOnlyList<GuanHaiMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GuanHaiReceipt? GuanHai { get; init; } }

public sealed record XieweiPaymentPreparedEvent(long PaymentId, long UseId, CardConversionSource Source, string GameplayHash, int OwnerSeat, int ActorSeat, int? RequesterSeat, int ActualTurn, int TurnOwner, bool ResponseUse, CardKind Kind, PublicPersistentPileSource Pile, long Before) : IGameEvent;
public sealed record XieweiMaterialFrozenEvent(long PaymentId, int Index, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record XieweiBaitStoredEvent(long PaymentId, int Index, int CardId, CardLocation From, CardLocation Pile, long Sequence, long BatchId) : IGameEvent;
public sealed record XieweiOutputBoundEvent(long PaymentId, long UseId, long ActionId, int ActorSeat, int ProviderSeat, int? RequesterSeat, CardKind Kind, bool ResponseUse) : IGameEvent;
public sealed record XieweiCommittedEvent(long ProgramId, long PaymentId, long UseId, long ActionId) : IGameEvent;
public sealed record XieweiTargetFrozenEvent(long UseId, long ActionId, int Index, int TargetSeat, int Maximum) : IGameEvent;
public sealed record GuanHaiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn, int TurnOwner, long ParentId) : IGameEvent;
public sealed record YouquePindianIssuedEvent(long FrameId, long PindianId, CardConversionSource Source, string GameplayHash, PublicPersistentPileSource Pile, int CardId, int Rank, int OpponentSeat, ActualDiscardRecoveryPhaseKey Phase, long Before) : IGameEvent;
public sealed record YouquePindianReturnedEvent(long FrameId, long PindianId, int SourceCardId, int OpponentCardId, int SourceRank, int OpponentRank) : IGameEvent;
public sealed record YouqueDiscardMaterialEvent(long FrameId, int Index, int LoserSeat, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record YouqueDiscardPaidEvent(long FrameId, int Index, long BatchId, int CardId, long Sequence) : IGameEvent;
public sealed record YouqueSlashIssuedEvent(YouqueSlashOrigin Origin) : IGameEvent;
public sealed record YouqueSlashReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record YouqueBenefitIssuedEvent(long FrameId, long ParentProgramId, long UseId, long DamageFrameId, int RecipientSeat) : IGameEvent;
public sealed record YouqueRewardDrawReturnedEvent(long FrameId, long UseId, long DamageFrameId, int RecipientSeat, int Requested, int Actual, long Before, long After, bool Replaced) : IGameEvent;
public sealed record YouquePhaseDisabledEvent(long FrameId, int OwnerSeat, ActualDiscardRecoveryPhaseKey Phase, CardConversionSource Source) : IGameEvent;
public sealed record GuanHaiCompletedEvent(long FrameId, bool Applied) : IGameEvent;

internal interface IGuanHaiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class GuanHaiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op == SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian ? [] : [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new GuanHaiHandler(op);
}
internal sealed class ResolveXieweiTwoHandBaitBasicUseDescriptor() : GuanHaiDescriptor(SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.GuanHai is { Paid: true } || context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveYouqueMinimumBaitPindianDescriptor() : GuanHaiDescriptor(SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian){
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.GuanHai is { Paid: true };
}
internal sealed class ResolveYouqueIssuedSlashDamageBenefitDescriptor() : GuanHaiDescriptor(SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.GuanHai is { Paid: true };
}
internal sealed class GuanHaiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IGuanHaiHost>().Resolve(f, op);
}
internal sealed class XieweiCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "ol:xiewei";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted || !t.IncludeResponseUses || t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource || t.SourceSkillId != SkillId || !t.AllowNoEventTarget || !t.SingleActionInstance || t.Optional || t.Subject is not null || t.CardCategories.Count != 0 || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Xiewei requires one exact paid native basic-use commitment.");
    }
}
internal static class GuanHaiComposition
{
    [ProgramCatalogValidation(720)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.ViewAs.Any(v => v.XieweiBaitBasicUse is not null) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse or SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian or SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit)) return;
        if (p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Guan Hai requires his complete native public-pile skills.");
        if (p.Id == "ol:xiewei")
        {
            if (p.Activations.Count != 0 || p.Triggers.Count != 1 || p.ViewAs.Count != 2 || !p.ViewAs.Select(v => v.OutputKind).ToHashSet().SetEquals([CardKind.Slash, CardKind.Dodge])) throw new InvalidOperationException(path + ": Xiewei requires both native basic directions.");
            foreach (var r in p.ViewAs) XieweiBaitBasicUseSchema.Validate(r, path);
            new XieweiCommittedUseContract().ValidateTrigger(p.Triggers[0]);
        }
        else if (p.Id != "ol:youque" || p.ViewAs.Count != 0 || p.Activations is not [var a] || a.Id != "minimum-bait-pindian" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian, Target: SkillProgramEffectTarget.Owner }] || p.Triggers is not [var t] || t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Any || t.Optional || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException(path + ": Youque requires native minimum-bait Pindian and its exact issued Slash damage benefit.");
    }
}
