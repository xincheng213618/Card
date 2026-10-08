namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<(int Owner, string Skill, string Instance), PublicPersistentPileSource> _publicPersistentPiles = [];
    private bool SupportsMultiplePublicPiles => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StoreNonBasicOwnedPublicPile) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StoreArbitraryOwnedPublicPile) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.CollectFinalTargetCardInPublicPile) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StoreBoundHandInPublicPile) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.StoreBoundCardsInPublicPile) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.CashOutPublicPile);
    private static string PublicPileIdentity(string skill, string instance) => $"{skill.Length}:{skill}{instance.Length}:{instance}";
    private IEnumerable<PublicPersistentPileSource> PublicPileSources(int seat) => _publicPersistentPiles.Values.Where(s => s.OwnerSeat == seat);
    private PublicPersistentPileSource? SinglePublicPileSource(int seat) => PublicPileSources(seat).Take(2).ToArray() is [var source] ? source : null;
    private static CardLocation PublicPileLocation(int seat) => new(CardZoneKind.PublicPersistentPile, seat);
    private IReadOnlyList<Card> PublicPileCards(PublicPersistentPileSource source) => _cardZones.CardsAt(source.Location);
    // Legacy scalar view is populated only when it can describe exactly one source.
    private IReadOnlyList<Card> PublicPileCards(int seat) => SinglePublicPileSource(seat) is { } source ? PublicPileCards(source) : [];
    private PublicPersistentPileSource EnsurePublicPileSource(ProgramSkillFrame frame, int capacity)
    {
        var key = (frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId);
        if (_publicPersistentPiles.TryGetValue(key, out var found))
        {
            if (found.Capacity != capacity) throw new InvalidOperationException("A public pile source cannot change capacity.");
            return found;
        }
        var existing = PublicPileSources(frame.OwnerSeat).ToArray();
        if (!SupportsMultiplePublicPiles && existing.Length > 0)
            throw new InvalidOperationException("A public pile cannot mix source skill instances or capacities.");
        var pileId = existing.Any(s => s.PublicPileId is null) ? PublicPileIdentity(frame.SkillId, frame.SkillInstanceId) : null;
        var source = new PublicPersistentPileSource(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, capacity, pileId);
        _cardZones.EnsurePublicPersistentPile(source.Location);
        _publicPersistentPiles.Add(key, source);
        return source;
    }
    private IReadOnlyList<PublicPersistentPileSource> ReferencedPublicPileSources(int seat, string skill, string? consumerInstance = null)
    {
        var sources = PublicPileSources(seat).Where(s => s.SkillId == skill).ToArray();
        if (consumerInstance is not null)
        {
            var own = sources.Where(s => s.SkillInstanceId == consumerInstance).ToArray();
            if (own.Length > 0) return own;
            var consumer = _players[seat].SkillGrants.Grants.FirstOrDefault(g => g.SkillInstanceId == consumerInstance);
            if (consumer is not null)
            {
                var same = sources.Where(s => _players[seat].SkillGrants.Grants.Any(g => g.SkillId == skill &&
                    g.SkillInstanceId == s.SkillInstanceId && g.SourceId == consumer.SourceId)).ToArray();
                if (same.Length > 0) return same;
            }
        }
        return sources;
    }
    private PublicPersistentPileSource? PublicPileDraftSource(ProgramPublicPileDraft draft) =>
        draft.SourceSkillInstanceId is { } instance
            ? _publicPersistentPiles.GetValueOrDefault((draft.OwnerSeat, draft.SourceSkillId, instance)) is { } exact && exact.Location == draft.SourceLocation ? exact : null
            : ReferencedPublicPileSources(draft.OwnerSeat, draft.SourceSkillId).SingleOrDefault();
    private IReadOnlyList<Card> PublicPileDraftCards(ProgramPublicPileDraft draft) =>
        PublicPileDraftSource(draft) is { } source ? PublicPileCards(source) : [];
    private CardLocation PublicPileDraftLocation(ProgramPublicPileDraft draft) =>
        PublicPileDraftSource(draft)?.Location ?? throw new InvalidOperationException("Public pile draft lost its exact source location.");
    private IReadOnlyList<PublicPersistentPileSnapshot>? CreatePublicPersistentPileSnapshots(int seat)
    {
        var sources = PublicPileSources(seat).ToArray();
        if (sources.Length < 2) return null;
        return Array.AsReadOnly(sources.Select(s => new PublicPersistentPileSnapshot(seat, s.SkillId, s.SkillInstanceId,
            _contentRegistry.GetSkill(s.SkillId).ProgramPresentation?.AuthorityName,
            Array.AsReadOnly(PublicPileCards(s).Select(ToSnapshot).ToArray()), PublicPileCards(s).Count, s.Location)).ToArray());
    }
    private int PublicPileProgramCount(int seat, string skill, string instance)
    {
        var program = _contentRegistry.GetSkill(skill).Program!;
        if (program.Triggers.SelectMany(trigger => trigger.Effects)
            .Any(effect => PublicPileCashOutContract.IsOperation(effect.Op)))
            return _publicPersistentPiles.GetValueOrDefault((seat, skill, instance)) is { } exact
                ? PublicPileCards(exact).Count : 0;
        var sourceSkills = program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects))
            .Where(e => e.Op is SkillProgramEffectOp.ExchangePublicPile or SkillProgramEffectOp.DistributePublicPileIfAllSuits or
                SkillProgramEffectOp.RemovePublicPileAfterAttackDamage or SkillProgramEffectOp.ResolvePreparationPublicPile or
                SkillProgramEffectOp.ExchangePublicPileHand or SkillProgramEffectOp.ObtainPublicPileCard or SkillProgramEffectOp.PublicPileColorDamage or SkillProgramEffectOp.RewardDiscardedActionColor or SkillProgramEffectOp.ResolveFirstGameDomainCrossing)
            .SelectMany(e => e.SkillIds).Distinct(StringComparer.Ordinal).ToArray();
        if (sourceSkills.Length == 0) sourceSkills = [skill];
        return sourceSkills.SelectMany(id => ReferencedPublicPileSources(seat, id, instance))
            .Select(s => PublicPileCards(s).Count).DefaultIfEmpty().Max();
    }
    private IReadOnlyDictionary<string, int>? CapturePublicPileProgramCounts(CharacterState owner)
    {
        if (!SupportsMultiplePublicPiles) return null;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
            counts[PublicPileIdentity(instance.SkillId, instance.SkillInstanceId)] = PublicPileProgramCount(owner.Seat, instance.SkillId, instance.SkillInstanceId);
        foreach (var source in PublicPileSources(owner.Seat))
            counts[PublicPileIdentity(source.SkillId, source.SkillInstanceId)] = PublicPileCards(source).Count;
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(counts);
    }
}
