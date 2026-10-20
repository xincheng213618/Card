using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouZuifengStage { CostChoice, TargetChoice, CostChildren, HandViewChoice, DamageChildren, AlcoholChoice, NativeIssuing, NativeUse, NativeChildren }
public sealed record MouZuifengViewedEntity(int Index, int CardId, CardKind PrintedKind);
public sealed record MouZuifengReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int InstructionIndex, int ActualTurn, int TurnOwner)
{
    public MouZuifengStage Stage { get; init; }
    public int? CostId { get; init; }
    public CardKind? CostKind { get; init; }
    public Suit CostSuit { get; init; }
    public int TargetSeat { get; init; } = -1;
    public bool Paid { get; init; }
    public long CostBatchId { get; init; }
    public long CostSequence { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int? ShownId { get; init; }
    public CardKind? ShownKind { get; init; }
    public Suit ShownSuit { get; init; }
    public bool FireIssued { get; init; }
    public bool FireReturned { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public Suit WineSuit { get; init; }
    public int WineRank { get; init; }
    public long WineBefore { get; init; }
    public long WinePaidSequence { get; init; }
    public bool UseReturned { get; init; }
    private readonly IReadOnlyList<MouZuifengViewedEntity> _viewed = Array.AsReadOnly(Array.Empty<MouZuifengViewedEntity>());
    private readonly IReadOnlyList<long> _nativeBatches = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<MouZuifengViewedEntity> Viewed { get => _viewed; init => _viewed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<long> NativeBatches { get => _nativeBatches; init => _nativeBatches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouZuifengAlcoholOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActorSeat, int TargetSeat, int CardId, CardKind PrintedKind,
    Suit EffectiveSuit, int EffectiveRank, long BeforePayment);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZuifengReceipt? MouZuifeng { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZuifengAlcoholOrigin? MouZuifengAlcohol { get; init; } }

public sealed record MouZuifengStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record MouZuifengCostChosenEvent(long FrameId, int CardId, CardKind PrintedKind, Suit EffectiveSuit) : IGameEvent;
public sealed record MouZuifengCostPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int TargetSeat, int CardId, CardKind PrintedKind, Suit EffectiveSuit,
    long BatchId, CardLocation From, CardLocation To, long Sequence, long Before, long After, int MaxHpAtPayment, int UseOrdinal) : IGameEvent;
public sealed record MouZuifengHandViewedEvent(long FrameId, int ViewerSeat, int TargetSeat, int HandCount, long Sequence) : IGameEvent;
public sealed record MouZuifengViewedEntityEvent(long FrameId, int Index, int CardId, CardKind PrintedKind) : IGameEvent;
public sealed record MouZuifengShownEvent(long FrameId, int ViewerSeat, int TargetSeat, int CardId,
    CardKind PrintedKind, Suit EffectiveSuit, bool SameSuit) : IGameEvent;
public sealed record MouZuifengFireIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int ShownCardId) : IGameEvent;
public sealed record MouZuifengFireReturnedEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record MouZuifengAlcoholChosenEvent(long FrameId, int ActorSeat, int TargetSeat, int CardId, bool Accepted) : IGameEvent;
public sealed record MouZuifengAlcoholIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat,
    int TargetSeat, int CardId, CardKind PrintedKind, Suit EffectiveSuit, int EffectiveRank) : IGameEvent;
public sealed record MouZuifengAlcoholPaidEvent(long FrameId, long UseId, long ActionId, int CardId,
    CardKind PrintedKind, int ActorSeat, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouZuifengAlcoholReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouZuifengNativeBatchSignedEvent(long FrameId, long UseId, long BatchId,
    long FirstSequence, long LastSequence, int Count) : IGameEvent;
public sealed record MouZuifengCompletedEvent(long FrameId, bool Paid, int? ShownCardId, long AlcoholUseId) : IGameEvent;

internal interface IMouZhouYuZuifengHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f); }
internal sealed class ResolveMouZhouYuZuifengDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuZuifeng;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler { get; } = new MouZhouYuZuifengHandler();
}
internal sealed class MouZhouYuZuifengHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhouYuZuifeng;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouZhouYuZuifengHost>().Resolve(f);
}
internal static class MouZuifengComposition
{
    [ProgramCatalogValidation(1130)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "mou-ol:zuifeng" && !p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveMouZhouYuZuifeng)) &&
            !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveMouZhouYuZuifeng))) return;
        if (p.Id != "mou-ol:zuifeng" || p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.Contributions.Count != 0 ||
            p.Activations is not [var a] || a.Id != "unique-suit-view-and-wine" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || a.UsesPerTurn is not null || a.UsesPerPhase is not null ||
            a.UsesPerGame is not null || a.UsageGroup != a.Id || a.MarkerCost is not null || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.CardCountExpression is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 || a.TargetRequiresEmptyEquipmentSlot ||
            a.OtherCharactersOnly || a.ContinueAfterOwnerDeath || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouZhouYuZuifeng,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Zuifeng requires its whole current-MaxHP actual-Play quota, private Hand discard/view and exact optional foreign-target native Alcohol chain.");
    }
}
