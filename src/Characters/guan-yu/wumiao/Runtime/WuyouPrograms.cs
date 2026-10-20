using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WuyouStage { ContributionChildren, ReturnChoice, ReturnChildren }
public sealed record WuyouReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int DonorSeat, int ContributionCardId,
    CardKind ContributionPrintedKind, long Before)
{
    public WuyouStage Stage { get; init; }
    public bool ContributionPaid { get; init; }
    public long ContributionBatchId { get; init; }
    public long ContributionSequence { get; init; }
    public long After { get; init; }
    public uint RandomBefore { get; init; }
    public uint RandomAfter { get; init; }
    private IReadOnlyList<CardKind> _names = Array.AsReadOnly(Array.Empty<CardKind>());
    public IReadOnlyList<CardKind> Names { get => _names; init => _names = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CardKind? ChosenKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ReturnCardId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CardKind? ReturnPrintedKind { get; init; }
    public long ReturnBefore { get; init; }
    public long ReturnBatchId { get; init; }
    public long ReturnSequence { get; init; }
    public bool ReturnPaid { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuyouReceipt? Wuyou { get; init; }
}

public sealed record WuyouHandLease(long FrameId, CardConversionSource Source, string GameplayHash,
    int HolderSeat, int CardId, CardKind PrintedKind, CardKind EffectiveKind,
    long ReturnBatchId, long MovementCutoff, bool SelfReturn);
public sealed record WuyouNativeUseReceipt(WuyouHandLease Lease, long UseId, long ActionId,
    int ActorSeat, int ProviderSeat, CardKind EffectiveKind, CardActionCost Material, long Before);
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuyouNativeUseReceipt? WuyouUse { get; init; }
}
public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuyouHandLease? Wuyou { get; init; }
}

public sealed record WuyouStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int DonorSeat, int CardId, CardKind PrintedKind, long Before) : IGameEvent;
public sealed record WuyouContributionPaidEvent(long FrameId, int DonorSeat, int RecipientSeat,
    int CardId, CardKind PrintedKind, long BatchId, long Before, long After, bool SelfGift) : IGameEvent;
public sealed record WuyouNameSampledEvent(long FrameId, int Index, CardKind Kind,
    uint RandomBefore, uint RandomAfter) : IGameEvent;
public sealed record WuyouReturnSelectedEvent(long FrameId, int CardId, CardKind PrintedKind,
    CardKind EffectiveKind, long Before) : IGameEvent;
public sealed record WuyouReturnPaidEvent(long FrameId, int CardId, long BatchId,
    long Before, long After, bool SelfGift) : IGameEvent;
public sealed record WuyouLeaseIssuedEvent(WuyouHandLease Lease) : IGameEvent;
public sealed record WuyouCompletedEvent(long FrameId, int DonorSeat, bool Returned) : IGameEvent;
public sealed record WuyouUseAcceptedEvent(WuyouNativeUseReceipt Receipt) : IGameEvent;
public sealed record WuyouDeclarationBoundEvent(long DeclarationId, long OwnerFrameId,
    int ProviderSeat, int ActorSeat, int CardId, WuyouHandLease Lease) : IGameEvent;

internal interface IWuGuanYuWuyouHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveWuyouContributionAndNamedReturnDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GiveSelected, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new WuyouHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuyouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<IWuGuanYuWuyouHost>().Resolve(frame);
}

internal static class WuyouComposition
{
    internal static readonly IReadOnlyList<CardKind> Names = Array.AsReadOnly(new[]
    {
        CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol,
        CardKind.DrawTwo, CardKind.Duel, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.PeachGarden,
        CardKind.FiveGrains, CardKind.Dismantlement, CardKind.Snatch, CardKind.FireAttack, CardKind.IronChain,
        CardKind.BorrowedSword, CardKind.Nullification, CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning
    });
    [ProgramCatalogValidation(1310)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "wumiao:wuyou" && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn)) return;
        if (p.Id != "wumiao:wuyou" || p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Activations is not [var a] || a.Id != "hand-contribution" || a.MinCards != 0 || a.MaxCards != 0 ||
            a.MinTargets != 0 || a.MaxTargets != 0 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
            !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null ||
            a.UsageGroup != a.Id || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
            a.CardCountExpression is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.EquipmentSlots.Count != 0 || a.TargetRequiresEmptyEquipmentSlot || a.OtherCharactersOnly ||
            a.ContinueAfterOwnerDeath || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveWuyouContributionAndNamedReturn,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Wuyou requires its native contribution, random five-name offer and exact returned original Hand entity.");
    }
}
