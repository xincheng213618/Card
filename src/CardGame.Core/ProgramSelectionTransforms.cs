namespace CardGame.Core;

// Selection modules receive only read contracts. The registered engine adapters
// retain ownership, provenance and rule-query checks behind these narrow APIs.
internal interface ISelectionParticipantQueries : IProgramSelectionQuery
{
    IReadOnlyList<int> Seats { get; }
    bool IsValidSeat(int seat);
    bool IsAlive(int seat);
    int HandCount(int seat);
    int EquipmentCount(int seat);
    int Hp(int seat);
}

internal interface IMatchingRecastSelectionQueries : IProgramSelectionQuery
{
    IReadOnlyList<MatchingRecastMaterial> Materials(int ownerSeat);
}

internal interface ISourceCurseSelectionQueries : IProgramSelectionQuery
{
    bool HasCurse(int targetSeat);
}

internal interface IPairObtainSelectionQueries : IProgramSelectionQuery
{
    string CurrentSkillInstance(int ownerSeat, string skillId);
    bool CanObtainFrom(int ownerSeat, string skillId, string instanceId, int targetSeat);
}

internal interface IForeignSlashSelectionQueries : IProgramSelectionQuery
{
    bool CanStart(int ownerSeat, int actorSeat);
}

internal interface IDuelSelectionQueries : IProgramSelectionQuery
{
    bool CanIssue(int ownerSeat, int actorSeat);
}

internal interface IEquipmentPlacementSelectionQueries : IProgramSelectionQuery
{
    bool CanPlace(int ownerSeat, int targetSeat, int cardId);
    bool CanPlaceCaptured(int ownerSeat, int targetSeat, int cardId, string skillId);
}

internal interface ISelectionUsageQueries : IProgramSelectionQuery
{
    bool CanUseDistinctTurnTarget(int ownerSeat, string skillId, string usageId, int targetSeat);
    bool CanUsePhaseTarget(int ownerSeat, string skillId, string usageId, int targetSeat);
    bool CanUseCategoryTarget(int ownerSeat, string skillId, string usageId, int cardId, int targetSeat);
}

internal interface IPindianSelectionQueries : IProgramSelectionQuery
{
    bool HasNextActualUseStructure(SkillProgramActivation activation);
    int EffectiveRank(int ownerSeat, int cardId);
    int HandKeepValue(int ownerSeat, int cardId);
}

[ProgramSelectionModule(ProgramSelectionStage.AfterCards, 10, SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice)]
internal sealed class MatchingRecastSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var materials = queries.Get<IMatchingRecastSelectionQueries>().Materials(context.OwnerSeat);
        var equipmentCount = materials.Count(material => EquipmentCatalog.IsEquipment(material.PrintedKind));
        var names = materials.GroupBy(material => BasicName(material.PrintedKind))
            .ToDictionary(group => group.Key, group => group.Count());
        var eligible = materials.Where(material => EquipmentCatalog.IsEquipment(material.PrintedKind)
                ? equipmentCount >= 2 : names[BasicName(material.PrintedKind)] >= 2)
            .Select(material => material.CardId);
        candidates.Cards = candidates.Cards.Intersect(eligible).ToArray();
        return true;
    }

    private static CardKind BasicName(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 20, SkillProgramEffectOp.DepositSelectedSourceCurse)]
internal sealed class SourceCurseSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var curses = queries.Get<ISourceCurseSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => !curses.HasCurse(seat)).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 30, SkillProgramEffectOp.ObtainOneFromEachSelectedTarget)]
internal sealed class PairObtainSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var obtain = queries.Get<IPairObtainSelectionQueries>();
        var instance = obtain.CurrentSkillInstance(context.OwnerSeat, context.SkillId);
        // This rule replaces the initial target domain, including a payable
        // owner. Intersecting the old candidates would remove legal pairs.
        candidates.Targets = queries.Get<ISelectionParticipantQueries>().Seats
            .Where(seat => obtain.CanObtainFrom(context.OwnerSeat, context.SkillId, instance, seat)).Order().ToArray();
        return candidates.Targets.Length >= 2;
    }
}

internal static class DeferredHandSelection
{
    internal static bool IsPayable(ISelectionParticipantQueries players, int owner, int first, int second) =>
        players.IsValidSeat(first) && players.IsValidSeat(second) && first != second && first != owner && second != owner &&
        players.IsAlive(first) && players.IsAlive(second) &&
        Math.Abs(players.HandCount(first) - players.HandCount(second)) <= players.HandCount(owner) + players.EquipmentCount(owner);
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 40, SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt)]
internal sealed class DeferredHandPairSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var players = queries.Get<ISelectionParticipantQueries>();
        var seats = players.Seats;
        if (!seats.Where(seat => players.IsAlive(seat) && seat != context.OwnerSeat)
                .Any(first => seats.Any(second => DeferredHandSelection.IsPayable(players, context.OwnerSeat, first, second))))
            return false;
        var targets = candidates.Targets;
        candidates.Targets = targets.Where(first => targets.Any(second =>
            DeferredHandSelection.IsPayable(players, context.OwnerSeat, first, second))).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 50, SkillProgramEffectOp.UseSelectedForeignCardAsSlash)]
internal sealed class ForeignSlashSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var slash = queries.Get<IForeignSlashSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => slash.CanStart(context.OwnerSeat, seat)).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 60, SkillProgramEffectOp.RecastOwnedCardsAndDuel)]
internal sealed class RecastDuelSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var duel = queries.Get<IDuelSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => duel.CanIssue(seat, context.OwnerSeat)).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 70, SkillProgramEffectOp.UseSelectedActorDuel)]
internal sealed class SelectedActorDuelSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var duel = queries.Get<IDuelSelectionQueries>();
        if (OrderedSelectedActorDuelContract.IsOptIn(context.Features.First(SkillProgramEffectOp.UseSelectedActorDuel)!))
        {
            // Publish complete directed pairs. A seat may be a legal actor but
            // an illegal recipient (for example an empty-handed Kongcheng owner).
            var pairs = candidates.Targets.SelectMany(actor => candidates.Targets
                .Where(target => actor != target && actor != context.OwnerSeat && target != context.OwnerSeat && duel.CanIssue(target, actor))
                .Select(target => (IReadOnlyList<int>)Array.AsReadOnly(new[] { actor, target }))).ToArray();
            candidates.TargetSequences = Array.AsReadOnly(pairs);
            candidates.Targets = candidates.Targets.Where(seat => pairs.Any(pair => pair.Contains(seat))).ToArray();
            return pairs.Length > 0;
        }
        var forward = OrderedSelectedActorDuelContract.IsOwnerToSelected(context.Features.First(SkillProgramEffectOp.UseSelectedActorDuel)!);
        candidates.Targets = candidates.Targets.Where(seat => forward ? duel.CanIssue(seat, context.OwnerSeat) : duel.CanIssue(context.OwnerSeat, seat)).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 80, SkillProgramEffectOp.ChooseOwnerHpLoss)]
internal sealed class OwnerHpLossSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries) =>
        queries.Get<ISelectionParticipantQueries>().Hp(context.OwnerSeat) > 0;
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 90, SkillProgramEffectOp.PlaceSelectedEquipment)]
internal sealed class EquipmentPlacementSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var equipment = queries.Get<IEquipmentPlacementSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => candidates.Cards.Any(id =>
            equipment.CanPlace(context.OwnerSeat, seat, id))).ToArray();
        candidates.Cards = candidates.Cards.Where(id => candidates.Targets.Any(seat =>
            equipment.CanPlace(context.OwnerSeat, seat, id))).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 100, SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw)]
internal sealed class CapturedEquipmentSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var equipment = queries.Get<IEquipmentPlacementSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => candidates.Cards.Any(id =>
            equipment.CanPlaceCaptured(context.OwnerSeat, seat, id, context.SkillId))).ToArray();
        candidates.Cards = candidates.Cards.Where(id => candidates.Targets.Any(seat =>
            equipment.CanPlaceCaptured(context.OwnerSeat, seat, id, context.SkillId))).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 110, SkillProgramEffectOp.ConsumeDistinctTurnTarget)]
internal sealed class DistinctTurnTargetSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var ledger = context.Activation.Effects.SingleOrDefault(effect => effect.Op == SkillProgramEffectOp.ConsumeDistinctTurnTarget)?.StateId;
        if (ledger is null) return true;
        var usage = queries.Get<ISelectionUsageQueries>();
        candidates.Targets = candidates.Targets.Where(seat =>
            usage.CanUseDistinctTurnTarget(context.OwnerSeat, context.SkillId, ledger, seat)).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 120)]
internal sealed class PhaseTargetLedgerSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        if (context.Activation.TargetPhaseLedgerId is not { } ledger) return true;
        var usage = queries.Get<ISelectionUsageQueries>();
        candidates.Targets = candidates.Targets.Where(seat =>
            usage.CanUsePhaseTarget(context.OwnerSeat, context.SkillId, ledger, seat)).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 130)]
internal sealed class CategoryTargetLedgerSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        if (context.Activation.CategoryTargetLedgerId is not { } ledger) return true;
        var usage = queries.Get<ISelectionUsageQueries>();
        candidates.Cards = candidates.Cards.Where(id => candidates.Targets.Any(seat =>
            usage.CanUseCategoryTarget(context.OwnerSeat, context.SkillId, ledger, id, seat))).ToArray();
        candidates.Targets = candidates.Targets.Where(seat => candidates.Cards.Any(id =>
            usage.CanUseCategoryTarget(context.OwnerSeat, context.SkillId, ledger, id, seat))).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterAvailability, 10)]
internal sealed class NextActualUsePindianSelectionOrder : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var pindian = queries.Get<IPindianSelectionQueries>();
        if (pindian.HasNextActualUseStructure(context.Activation))
            candidates.Cards = candidates.Cards.OrderByDescending(id => pindian.EffectiveRank(context.OwnerSeat, id))
                .ThenBy(id => pindian.HandKeepValue(context.OwnerSeat, id)).ThenBy(id => id).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterAvailability, 20, SkillProgramEffectOp.StartSimultaneousPindian)]
internal sealed class SimultaneousPindianSelectionOrder : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        if (context.Activation.Effects is not [{ Op: SkillProgramEffectOp.StartSimultaneousPindian }]) return true;
        var pindian = queries.Get<IPindianSelectionQueries>();
        candidates.Cards = candidates.Cards.OrderByDescending(id => pindian.EffectiveRank(context.OwnerSeat, id))
            .ThenBy(id => id).ToArray();
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.Relationships, 10, SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt)]
internal sealed class DeferredHandPairSelectionRelationships : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var players = queries.Get<ISelectionParticipantQueries>();
        candidates.TargetSequences = Array.AsReadOnly(candidates.Targets.SelectMany(first => candidates.Targets
            .Where(second => DeferredHandSelection.IsPayable(players, context.OwnerSeat, first, second))
            .Select(second => (IReadOnlyList<int>)Array.AsReadOnly(new[] { first, second }))).ToArray());
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.Relationships, 20,
    SkillProgramEffectOp.PlaceSelectedEquipment, SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw)]
internal sealed class EquipmentPlacementSelectionRelationships : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var selected = context.Features.HasOperation(SkillProgramEffectOp.PlaceSelectedEquipment);
        var captured = context.Features.HasOperation(SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw);
        var equipment = queries.Get<IEquipmentPlacementSelectionQueries>();
        candidates.CardTargets = Array.AsReadOnly(candidates.Cards.SelectMany(id => candidates.Targets
            .Where(seat => (!selected || equipment.CanPlace(context.OwnerSeat, seat, id)) &&
                (!captured || equipment.CanPlaceCaptured(context.OwnerSeat, seat, id, context.SkillId)))
            .Select(seat => new ActiveSkillCardTarget(id, seat))).ToArray());
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.Relationships, 30, SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice)]
internal sealed class MatchingRecastSelectionRelationships : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        candidates.SelectedCardsSameNameOrAllEquipment = true;
        return true;
    }
}
