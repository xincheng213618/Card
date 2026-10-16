using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DuanJiongSaoguStage { DelegateChoice, DelegateChildren, DiscardChoice, DiscardChildren, DrawChildren, UseChoice, NativeUse, NativeChildren }
public sealed record DuanJiongSaoguCard(int CardId, CardLocation From, CardKind Kind, Suit Suit);
public sealed record DuanJiongSaoguUseOrigin(long ProgramFrameId, long CardUseFrameId, long ActionId, int CardId, CardKind Kind, int ActorSeat);
public sealed record DuanJiongSaoguReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    ActualDiscardRecoveryPhaseKey Phase, bool Ending, bool Yang, int ActorSeat, DuanJiongSaoguStage Stage)
{
    public int ForbiddenSuitMask { get; init; }
    public int RequiredCount { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long PaidBatchId { get; init; }
    public int DrawCount { get; init; }
    private IReadOnlyList<DuanJiongSaoguCard> _candidates = Array.AsReadOnly(Array.Empty<DuanJiongSaoguCard>());
    private IReadOnlyList<DuanJiongSaoguCard> _selected = Array.AsReadOnly(Array.Empty<DuanJiongSaoguCard>());
    private IReadOnlyList<DuanJiongSaoguCard> _payments = Array.AsReadOnly(Array.Empty<DuanJiongSaoguCard>());
    private IReadOnlyList<int> _used = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<int> _grain = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<DuanJiongSaoguCard> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DuanJiongSaoguCard> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DuanJiongSaoguCard> Payments { get => _payments; init => _payments = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> UsedCardIds { get => _used; init => _used = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> WoodenOxGrainIds { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuanJiongSaoguCard? DelegateFee { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? SelectedUseCardId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ReservedUseId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuanJiongSaoguUseOrigin? NativeUse { get; init; }
    public bool NativeCompleted { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuanJiongSaoguReceipt? DuanJiongSaogu { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DuanJiongSaoguUseOrigin? DuanJiongSaoguUse { get; init; } }
public sealed record DuanJiongSaoguStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, bool Ending, bool Yang) : IGameEvent;
public sealed record DuanJiongSaoguDiscardSuitEvent(long BatchId, ActualDiscardRecoveryPhaseKey Phase, int OwnerSeat, int CardId, long MovementSequence, Suit Suit) : IGameEvent;
public sealed record DuanJiongSaoguEntityPaidEvent(long FrameId, bool DelegateFee, int ActorSeat, int CardId, CardLocation From, long Sequence, CardKind Kind, Suit Suit) : IGameEvent;
public sealed record DuanJiongSaoguBranchPaidEvent(long FrameId, int ActorSeat, bool Yang, bool Converted, int ActualCount, long Before, long After) : IGameEvent;
public sealed record DuanJiongSaoguNativeIssuedEvent(DuanJiongSaoguUseOrigin Origin) : IGameEvent;
public sealed record DuanJiongSaoguNativeReturnedEvent(DuanJiongSaoguUseOrigin Origin) : IGameEvent;
public sealed record DuanJiongSaoguCompletedEvent(long FrameId, int ActorSeat, bool Ending, bool Yang, int UsedCards, string Reason) : IGameEvent;
public interface IDuanJiongProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
public sealed class ResolveSaoguConversionSweepHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveSaoguConversionSweep;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IDuanJiongProgramHost>().Resolve(f);
}
internal sealed class ResolveSaoguConversionSweepDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveSaoguConversionSweep;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveSaoguConversionSweepHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
}
internal static class DuanJiongComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveSaoguConversionSweep)) && !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveSaoguConversionSweep))) return;
        bool Effect(IReadOnlyList<SkillProgramEffect> effects) => effects is [{ Op: SkillProgramEffectOp.ResolveSaoguConversionSweep, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
        if (p.Id != "ol:saogu" || p.Activations is not [var a] || p.Triggers is not [var t] || !Effect(a.Effects) || !Effect(t.Effects) ||
            a.Id != "sweep" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.TargetKind != SkillProgramTargetKind.AnyLiving ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath ||
            a.MarkerCost is not null || a.CardCountExpression is not null || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null || a.EquipmentSlots.Count != 0 || !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) ||
            t.Id != "delegate-current" || t.Window != SkillProgramTriggerWindow.TurnEnding || !t.Optional || t.Subject != SkillProgramTriggerSubject.Owner || t.OwnerRelation is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Priority != 0 || t.UsageScope is not null || t.UsageLimit is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.Suits.Count != 0 || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.DamageCardKinds.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 || t.MovementOccurrence is not null || t.DamageOccurrence is not null || t.MarkerCost is not null ||
            t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.DynamicUsageLimit is not null || t.GainPhaseQualification is not null || t.AllowNoEventTarget || t.IncludeResponseUses ||
            t.SingleActionInstance || t.DeferredTurnEndOnly || t.NoDyingAtActivation || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution ||
            p.BooleanStates is not [{ Id: "yin", InitialValue: false, Visibility: SkillProgramStateVisibility.Public, ResetScope: SkillProgramStateResetScope.Game, ReacquirePolicy: SkillProgramStateReacquirePolicy.PreserveUntilGameEnd }] ||
            p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Saogu requires its complete alternating own-Play sweep and optional Ending delegation, with exact native discarded entities.");
    }
}
