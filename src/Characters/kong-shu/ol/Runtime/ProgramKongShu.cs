using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CardGame.Core;

public sealed record ProgramLeiluanBasicUsePolicy(string StateId);
internal static class LeiluanBasicUseSchema
{
    internal static ProgramLeiluanBasicUsePolicy Parse(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 1 ||
            !node.TryGetProperty("stateId", out var state) || state.ValueKind != JsonValueKind.String ||
            state.GetString() is not { Length: > 0 and <= 128 } id || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]*\z"))
            throw new InvalidOperationException(path + ": Leiluan requires exactly one bounded stateId.");
        return new(id);
    }
    internal static void Validate(SkillProgramViewAs r, string path)
    {
        if (r.LeiluanBasicUse is null) return;
        var dodge = r.OutputKind == CardKind.Dodge;
        if (r.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol) ||
            r.InputCount != 0 || !r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.WoodenOxGrain]) ||
            r.InputKinds.Count != 0 || r.InputSuits.Count != 0 || r.InputCategories.Count != 0 ||
            !r.UseOnly || !r.ExtendedUse || !r.AllowSameKind || r.ForPlay == dodge || r.ForResponse != dodge ||
            r.Condition.Kind != SkillProgramConditionKind.Always || r.TieredRoundConversion is not null || r.RoundDistinctBasicUse is not null ||
            r.DrawFundedDistinctBasic is not null || r.RoundOnceCardlessBasic is not null || r.ChainedStateCost is not null ||
            r.RoundGainedRedHandOnly is not null || r.ConversionStateId is not null || r.MinimumTier != 0 || r.MaximumTier != 2 ||
            r.CostDestination is not null || r.UsesPerPhase is not null || r.UsesPerActualTurn is not null || r.UsageGroup is not null ||
            r.ActivationUsageGroup is not null || r.NameLedgerId is not null || r.UnusedOutputThisTurn || r.UnusedOutputNameThisGame ||
            r.SingleCardTrickUse || r.ExcludeOwnerEffects || r.DeclaredEntity || r.DeclarationValidation is not null || r.AllowChainedInput ||
            r.SameSuit || r.InheritPreviousPlaySuit || r.VariableInputCount || r.DistanceUnlimited || r.NoDying ||
            r.DamageBonus != 0 || r.RecoveryBonus != 0 || r.UseEffectiveInputSuit is not null || r.KenshangUse == true ||
            r.SourcePileSkillId is not null || r.NonBasicUnrespondableSlash || r.LastInSourceZone is not null ||
            r.DisableSkillUntilTurnEndIfNoDamage is not null || r.AllHandCardsOfSelectedSuit)
            throw new InvalidOperationException(path + ": Leiluan requires only its dynamic exact HE/grain count and native use-only basic direction.");
    }
}

public sealed record LeiluanMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record LeiluanNativePayment(long Id, long UseId, CardConversionSource Source, string GameplayHash,
    int OwnerSeat, int ActualTurn, int TurnOwner, int Round, CardKind Kind, bool ResponseUse, int FrozenX, long Before)
{
    public long ActionId { get; init; }
    public long After { get; init; }
    public bool Paid { get; init; }
    public bool LastHand { get; init; }
    private readonly IReadOnlyList<LeiluanMaterial> _materials = Array.AsReadOnly(Array.Empty<LeiluanMaterial>());
    private readonly IReadOnlyList<int> _handBefore = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<LeiluanMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> HandBefore { get => _handBefore; init => _handBefore = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<LeiluanNativePayment>? _leiluanPayments;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<LeiluanNativePayment>? LeiluanPayments
    { get => _leiluanPayments; init => _leiluanPayments = value is null ? null : Array.AsReadOnly(value.Select(p => p with { Materials = p.Materials, HandBefore = p.HandBefore, Movements = p.Movements }).ToArray()); }
}
public enum KongShuStage { BenefitDraw, BenefitDrawChildren, BenefitChooseTrick, BenefitGainChildren, ChainChildren, RoundDraw, RoundDrawChildren, RoundDiscard, RoundDiscardChildren }
public sealed record KongShuEntity(int CardId, CardKind Kind, CardLocation From);
public sealed record KongShuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int ActualTurn, int TurnOwner, int Round, KongShuStage Stage)
{
    public bool Paid { get; init; }
    public long PaymentId { get; init; }
    public long NativeUseId { get; init; }
    public long ActionId { get; init; }
    public int Cursor { get; init; }
    public int Payer { get; init; }
    public int DiscardCount { get; init; }
    public int ActualDrawCount { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    private readonly IReadOnlyList<int> _drawRecipients = Array.AsReadOnly(Array.Empty<int>()), _discardRecipients = Array.AsReadOnly(Array.Empty<int>()), _selected = Array.AsReadOnly(Array.Empty<int>()), _grain = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<KongShuEntity> _materials = Array.AsReadOnly(Array.Empty<KongShuEntity>());
    public IReadOnlyList<int> DrawRecipients { get => _drawRecipients; init => _drawRecipients = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> DiscardRecipients { get => _discardRecipients; init => _discardRecipients = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Grain { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<KongShuEntity> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KongShuReceipt? KongShu { get; init; } }

// New rule facts are scalar; private collections stay frozen on their real owning frames.
public sealed record LeiluanPaymentPreparedEvent(long PaymentId, long UseId, CardConversionSource Source, string GameplayHash, int OwnerSeat, int ActualTurn, int TurnOwner, int Round, CardKind Kind, bool ResponseUse, int X, int HandCount, long Before) : IGameEvent;
public sealed record LeiluanMaterialFrozenEvent(long PaymentId, int Index, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record LeiluanHandFrozenEvent(long PaymentId, int Index, int CardId) : IGameEvent;
public sealed record LeiluanMaterialPaidEvent(long PaymentId, int Index, int CardId, long Sequence, long BatchId) : IGameEvent;
public sealed record LeiluanPaymentBoundEvent(long PaymentId, long UseId, long ActionId, bool LastHand, long After) : IGameEvent;
public sealed record LeiluanBenefitIssuedEvent(long ProgramId, long PaymentId, long UseId, long ActionId, int OwnerSeat) : IGameEvent;
public sealed record LeiluanDisabledEvent(long ProgramId, int OwnerSeat, string SkillId, string StateId) : IGameEvent;
public sealed record KongShuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, int Round) : IGameEvent;
public sealed record KongShuDrawRecipientFrozenEvent(long FrameId, int Index, int RecipientSeat) : IGameEvent;
public sealed record KongShuDiscardRecipientFrozenEvent(long FrameId, int Index, int RecipientSeat, int X) : IGameEvent;
public sealed record KongShuDrawIssuedEvent(long FrameId, int Cursor, int RecipientSeat, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record KongShuMaterialFrozenEvent(long FrameId, int Cursor, int Index, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record KongShuGrainFrozenEvent(long FrameId, int Cursor, int Index, int CardId) : IGameEvent;
public sealed record KongShuMaterialMovedEvent(long FrameId, int Cursor, long BatchId, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record KongShuMovementIssuedEvent(long FrameId, int Cursor, int Payer, int Count, long Before, long After) : IGameEvent;
public sealed record KongShuCompletedEvent(long FrameId, bool Applied) : IGameEvent;

internal interface IKongShuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class KongShuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit ? SkillProgramTriggerWindow.CardUseCommitted : op == SkillProgramEffectOp.ResolveFuchaoCurrentTurnChain ? SkillProgramTriggerWindow.CardUseCompleted : SkillProgramTriggerWindow.RoundEnding)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new KongShuHandler(op);
}
internal sealed class ResolveLeiluanLastHandUseBenefitDescriptor() : KongShuDescriptor(SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit) {
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class ResolveFuchaoCurrentTurnChainDescriptor() : KongShuDescriptor(SkillProgramEffectOp.ResolveFuchaoCurrentTurnChain) { }
internal sealed class ResolveFuchaoRoundEndingDrawAndDiscardDescriptor() : KongShuDescriptor(SkillProgramEffectOp.ResolveFuchaoRoundEndingDrawAndDiscard) {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
 }
internal sealed class KongShuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IKongShuHost>().Resolve(f, op);
}
internal sealed class LeiluanCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "ol:leiluan";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted || !t.IncludeResponseUses ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource || t.SourceSkillId != SkillId ||
            !t.AllowNoEventTarget || !t.SingleActionInstance || t.Optional || t.Subject is not null ||
            t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Leiluan requires its exact original-payer committed basic-use benefit.");
    }
}
internal static class KongShuComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit or SkillProgramEffectOp.ResolveFuchaoCurrentTurnChain or SkillProgramEffectOp.ResolveFuchaoRoundEndingDrawAndDiscard;
    [ProgramCatalogValidation(710)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.ViewAs.Any(v => v.LeiluanBasicUse is not null) && !p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op))) && !p.Activations.Any(a => a.Effects.Any(e => Handles(e.Op)))) return;
        if (p.Id is not ("ol:leiluan" or "ol:fuchao") || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Kong Shu requires her exact complete native basic and round-ending program.");
        if (p.Id == "ol:leiluan")
        {
            var kinds = new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol };
            if (p.ViewAs.Count != 6 || !p.ViewAs.Select(v => v.OutputKind).ToHashSet().SetEquals(kinds) || p.ViewAs.Any(v => v.LeiluanBasicUse?.StateId != "leiluan") || p.Triggers.Count != 1)
                throw new InvalidOperationException(path + ": Leiluan requires all six effective basics and its one committed-use benefit.");
            foreach (var v in p.ViewAs) LeiluanBasicUseSchema.Validate(v, path);
        }
        else if (p.ViewAs.Count != 0 || p.Triggers.Count != 2) throw new InvalidOperationException(path + ": Fuchao requires both native basic completion and real round ending.");
        foreach (var t in p.Triggers)
        {
            if (t.Effects is not [var e] || !Handles(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always || t.Optional || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.MarkerCost is not null || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.Suits.Count != 0 || t.RequireNoCardConversion is not null || t.MovementReasons.Count != 0 || t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own)
                throw new InvalidOperationException(path + ": Kong Shu cannot weaken her native trigger or payment contract.");
            if (e.Op == SkillProgramEffectOp.ResolveFuchaoRoundEndingDrawAndDiscard)
            { if (t.Window != SkillProgramTriggerWindow.RoundEnding || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null || t.IncludeResponseUses || t.CardCategories.Count != 0) throw new InvalidOperationException(path + ": Fuchao requires the true RoundEnding mother."); }
            else if (t.OwnerRelation != (e.Op == SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit ? SkillProgramCardActionOwnerRelation.ConversionSource : SkillProgramCardActionOwnerRelation.Actor) || t.SourceSkillId != (e.Op == SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit ? "ol:leiluan" : null) || e.Op == SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit && (!t.AllowNoEventTarget || !t.SingleActionInstance) || !t.IncludeResponseUses || (e.Op == SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit ? t.CardCategories.Count != 0 : !t.CardCategories.SequenceEqual([SkillProgramCardCategory.Basic])) || t.Window != (e.Op == SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit ? SkillProgramTriggerWindow.CardUseCommitted : SkillProgramTriggerWindow.CardUseCompleted))
                throw new InvalidOperationException(path + ": Kong Shu's card trigger requires a completed/committed true basic use, including qualified responses only.");
        }
    }
}
