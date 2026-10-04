using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramAdjacentDiscardOriginEvent(int MovementSequence, int CardId, int SourceSeat,
    int PreviousLivingSeat, int NextLivingSeat, CardKind EffectiveKind) : IGameEvent;
public sealed record ProgramAdjacentDiscardStoredEvent(long ProgramFrameId, CardConversionSource Source,
    int CardId, int DiscardMovementSequence, int StoreMovementSequence, CardZoneKind Zone) : IGameEvent;
public sealed record ProgramCompletedUsePaymentEvent(long ProgramFrameId, long CardUseFrameId,
    int OwnerSeat, int? DiscardedCardId, int HpLost) : IGameEvent;
public sealed record ProgramRoundPileAlcoholIssuedEvent(long ProgramFrameId, long CardUseFrameId,
    long DyingFrameId, CardConversionSource Source, string StateId, int ActualRoundNumber,
    int Price, int VictimSeat) : IGameEvent;
public sealed record ProgramRoundPileAlcoholMaterialPaidEvent(long ProgramFrameId, int CardId,
    CardLocation From, int MovementSequence) : IGameEvent;

// Private until the recorded entity actually enters the public discard pile.
public sealed record PendingAdjacentDiscardOrigin(long OwnerFrameId, int EntryMovementSequence, int CardId,
    CardLocation From, string Reason, int SourceSeat, int PreviousLivingSeat, int NextLivingSeat, CardKind EffectiveKind);
public sealed record PendingAdjacentDiscardOrigins
{
    [JsonConstructor]
    public PendingAdjacentDiscardOrigins(IReadOnlyList<PendingAdjacentDiscardOrigin> entries) => Entries = Array.AsReadOnly(entries.ToArray());
    public IReadOnlyList<PendingAdjacentDiscardOrigin> Entries { get; }
}
public sealed record CurrentSlashFireDraft(long CardUseFrameId);
public sealed record CurrentSlashFirePolicy(long ProgramFrameId, CardConversionSource Source, CardActionContext OriginalAction,
    CardKind OriginalKind, int? ExtraTargetSeat, bool Converted);
public sealed record ProgramCurrentSlashFireChangedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    CardConversionSource Source, CardKind OriginalKind, CardKind FinalKind, int? ExtraTargetSeat) : IGameEvent;
public sealed record ProgramCurrentSlashFireRedirectedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    CardConversionSource Source, int TargetIndex, int OriginalTargetSeat, int NewTargetSeat) : IGameEvent;

public sealed record ProgramAdjacentDiscardReceipt(int InstructionIndex, int CardId,
    int DiscardMovementSequence, int StoreMovementSequence, CardZoneKind Zone);
public enum CompletedUsePaymentStage { Choosing, Paid }
public sealed record ProgramCompletedUsePaymentReceipt(int InstructionIndex, long CardUseFrameId,
    CompletedUsePaymentStage Stage, int? CardId = null, CardLocation? From = null,
    int MovementSequence = 0, int HpBefore = 0, bool LostHp = false);

public enum RoundPileAlcoholStage { Choosing, Paid, Issued, Finished }
public sealed record RoundPileAlcoholMaterial(int CardId, CardKind CardKind, CardLocation From, bool IsRed);

// Constructor-owned collections stay immutable in diagnostics, checkpoints and prepared views.
public sealed record ProgramRoundPileAlcoholReceipt
{
    [JsonConstructor]
    public ProgramRoundPileAlcoholReceipt(int instructionIndex, long dyingFrameId, int victimSeat,
        string stateId, int actualRoundNumber, int price, CardZoneKind zone,
        RoundPileAlcoholStage stage, IReadOnlyList<int> selectedIds,
        IReadOnlyList<RoundPileAlcoholMaterial> materials, long? cardUseFrameId = null)
    {
        InstructionIndex = instructionIndex; DyingFrameId = dyingFrameId; VictimSeat = victimSeat;
        StateId = stateId; ActualRoundNumber = actualRoundNumber; Price = price; Zone = zone;
        Stage = stage; SelectedIds = Array.AsReadOnly(selectedIds.ToArray());
        Materials = Array.AsReadOnly(materials.ToArray()); CardUseFrameId = cardUseFrameId;
    }
    public int InstructionIndex { get; init; }
    public long DyingFrameId { get; init; }
    public int VictimSeat { get; init; }
    public string StateId { get; init; }
    public int ActualRoundNumber { get; init; }
    public int Price { get; init; }
    public CardZoneKind Zone { get; init; }
    public RoundPileAlcoholStage Stage { get; init; }
    public IReadOnlyList<int> SelectedIds { get; }
    public IReadOnlyList<RoundPileAlcoholMaterial> Materials { get; }
    public long? CardUseFrameId { get; init; }
}

public sealed record RoundPileAlcoholReturn(long ProgramFrameId, int InstructionIndex,
    long DyingFrameId, CardConversionSource Source, string StateId, int ActualRoundNumber,
    int Price, int VictimSeat);

public interface IAdjacentDiscardAndRoundAlcoholHost
{
    SkillProgramStepOutcome StoreAdjacentDiscardedSlash(ProgramSkillFrame frame, CardZoneKind zone);
    SkillProgramStepOutcome OfferCurrentSlashFireAndExtraTarget(ProgramSkillFrame frame);
    SkillProgramStepOutcome PayCompletedUseDiscardOrLoseHp(ProgramSkillFrame frame);
    SkillProgramStepOutcome UseRoundPricedPileDyingAlcohol(ProgramSkillFrame frame, string stateId, CardZoneKind zone);
}

internal abstract class AdjacentDiscardAndRoundAlcoholDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new AdjacentDiscardAndRoundAlcoholHandler(Op);
    internal static CardZoneKind ReadPile(ProgramOperationNodeReader r, string property)
    {
        var zone = r.RequiredEnum<CardZoneKind>(property);
        if (zone is not (CardZoneKind.Chunlao or CardZoneKind.Authority))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.{property}: requires a public owner persistent zone.");
        return zone;
    }
}

internal sealed class OfferCurrentSlashFireAndExtraTargetDescriptor : AdjacentDiscardAndRoundAlcoholDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireContext(ProgramContextCapability.CardAction)];
}

internal sealed class StoreAdjacentDiscardedSlashDescriptor : AdjacentDiscardAndRoundAlcoholDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StoreAdjacentDiscardedSlash;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ClaimMovedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "destinationZone", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), destination: SkillProgramCardDestination.OwnerPersistentZone, destinationZone: ReadPile(r, "destinationZone"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

internal sealed class PayCompletedUseDiscardOrLoseHpDescriptor : AdjacentDiscardAndRoundAlcoholDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (_, c) => c.LoseHp(new(SkillProgramEffectOp.LoseHp, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}

internal sealed class UseRoundPricedPileDyingAlcoholDescriptor : AdjacentDiscardAndRoundAlcoholDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DyingRescue,
        static (e, c) => c.DyingRescue(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "stateId", "condition");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count != 1 || zones[0] is not (CardZoneKind.Chunlao or CardZoneKind.Authority))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: requires one public owner persistent zone.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            zones: zones, stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingResponse), new RequireContext(ProgramContextCapability.Dying)];
}

public sealed class AdjacentDiscardAndRoundAlcoholHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        Op switch
        {
            SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget => ((IAdjacentDiscardAndRoundAlcoholHost)host).OfferCurrentSlashFireAndExtraTarget(f),
            SkillProgramEffectOp.StoreAdjacentDiscardedSlash => ((IAdjacentDiscardAndRoundAlcoholHost)host).StoreAdjacentDiscardedSlash(f, e.DestinationZone!.Value),
            SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp => ((IAdjacentDiscardAndRoundAlcoholHost)host).PayCompletedUseDiscardOrLoseHp(f),
            SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol => ((IAdjacentDiscardAndRoundAlcoholHost)host).UseRoundPricedPileDyingAlcohol(f, e.StateId!, e.Zones[0]),
            _ => throw new InvalidOperationException("Unknown adjacent-discard/round-Alcohol operation.")
        };
}

internal static class AdjacentDiscardAndRoundAlcoholComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window)
    {
        var special = effects.Where(e => e.Op is SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget or SkillProgramEffectOp.StoreAdjacentDiscardedSlash or
            SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp or SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol).ToArray();
        if (special.Length == 0) return;
        if (special.Length != 1 || effects.Count != 1 || window != (special[0].Op switch
            { SkillProgramEffectOp.OfferCurrentSlashFireAndExtraTarget => SkillProgramTriggerWindow.CardUseCommitted,
              SkillProgramEffectOp.StoreAdjacentDiscardedSlash => SkillProgramTriggerWindow.DiscardPileReceived,
              SkillProgramEffectOp.PayCompletedUseDiscardOrLoseHp => SkillProgramTriggerWindow.CardUseCompleted,
              _ => SkillProgramTriggerWindow.DyingResponse }))
            throw new InvalidOperationException($"Invalid skill program at {path}: this operation requires its standalone exact owning trigger window.");
    }
}
