namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsOwnPublicPileSlashMaterial(CharacterState owner, Card card) =>
        IsOwnPublicPileCard(owner, card) && GetProgramViewAsConversions(owner, card, CardKind.Slash, forResponse: false)
            .Any(source => ViewAsRule(source)?.SourcePileSkillId is not null);

    // The native action already froze and validated its exact source before payment.
    // Source-loss cleanup may remove a now-empty pile while paid children are suspended.
    private bool IsOwnPublicPileActionCost(CardActionContext action, CardActionCost cost) =>
        IsNativePublicPileOriginalActionCost(action, cost) || action.PhysicalCards is [var only] && only == cost &&
        cost.From.Zone == CardZoneKind.PublicPersistentPile && cost.From.OwnerSeat == action.ProviderSeat &&
        action.ConversionChain.Any(source => source.OwnerSeat == action.ProviderSeat &&
            ViewAsRule(source) is { SourcePileSkillId: { } skill, InputCount: 1, OutputKind: CardKind.Slash } &&
            IsSlashCard(action.EffectiveKind) &&
            (skill == source.SkillId && (cost.From.PublicPileId is null ||
                cost.From.PublicPileId == PublicPileIdentity(skill, source.SkillInstanceId)) ||
             ReferencedPublicPileSources(action.ProviderSeat, skill, source.SkillInstanceId).Any(pile =>
                pile.Location == cost.From)));

    private CardKind PhysicalSlashUseBaseKind(CharacterState owner, Card card) =>
        WuyouIdentity(owner, card)?.Identity.OutputKind is { } wuyou && IsSlashCard(wuyou) ? wuyou :
        IsOwnPublicPileCard(owner, card) && !IsNativePublicPileOriginalChoice(owner, card, card.Kind, false)
            ? CardKind.Slash : IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash;

    private void RefreshOwnPublicPileAddedTargetCandidates(CardUseFrame use, CardUseFrame previous)
    {
        if (!_contentRegistry.ProgramDependencies.UsesOwnPublicPileConversions ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost] } action ||
            previous.Action is not { } previousAction || previousAction.ActionId != action.ActionId ||
            !IsOwnPublicPileActionCost(action, cost)) return;
        var added = action.EffectiveDesignatedTargetSeats.Except(
            previousAction.EffectiveDesignatedTargetSeats).Distinct().Order().ToArray();
        if (added.Length == 0) return;
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w =>
            w.ParentFrameId == use.Id && GetCardActionWindow(w) == SkillProgramTriggerWindow.CardUseTargetsFinalized);
        if (window is null) return;
        // Additions keep the paid action and original candidates. Each new target
        // receives only its matching pile-conversion binding, once per source.
        var candidates = CollectSharedCardActionCandidates(action, SkillProgramTriggerWindow.CardUseTargetsFinalized,
            added, null, window.Continuation).Where(c =>
        {
            var trigger = _contentRegistry.GetSkill(c.SkillId).Program!.Triggers.Single(t => t.Id == c.TriggerId);
            return trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.ConversionSource &&
                !trigger.SingleActionInstance && added.Contains(c.OpponentSeat) &&
                action.ConversionChain.Any(source => source.OwnerSeat == c.OwnerSeat &&
                    source.SkillId == trigger.SourceSkillId &&
                    (trigger.SourceViewAsId is null || source.BindingId == trigger.SourceViewAsId) &&
                    ViewAsRule(source)?.SourcePileSkillId is not null) &&
                !window.Candidates.Any(old => old.OwnerSeat == c.OwnerSeat && old.SkillId == c.SkillId &&
                    old.TriggerId == c.TriggerId && old.SkillInstanceId == c.SkillInstanceId && old.OpponentSeat == c.OpponentSeat);
        }).OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId, StringComparer.Ordinal)
            .ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).ThenBy(c => c.OpponentSeat)
            .Select(c => c with { FrozenContext = c.FrozenContext! with { ParentFrameId = window.Id,
                CardUse = c.FrozenContext.CardUse! with { ParentCardUseFrameId = use.Id } } }).ToArray();
        if (candidates.Length > 0) ReplaceRuntimeFrame(window.Id, window with
            { Candidates = Array.AsReadOnly(window.Candidates.Concat(candidates).ToArray()) });
    }

    private Card? OwnPublicPileResponseMaterial(IReadOnlyList<CardActionCost> costs, CardConversionSource? source) =>
        costs is [var cost] && cost.From.Zone == CardZoneKind.PublicPersistentPile &&
        (source is not null && ViewAsRule(source)?.SourcePileSkillId is not null ||
         source is null && _resolutionStack.Any(f => f.NativePublicPilePayment is { SequenceAfter: > 0 } p &&
             p.Card.Id == cost.CardId && p.Card.Kind == cost.CardKind && p.Pile.Location == cost.From))
            ? GetAttackCard(cost.CardId) : null;

    private bool IsOwnPublicPileCard(CharacterState owner, Card card) =>
        _cardZones.GetLocation(card.Id) is { Zone: CardZoneKind.PublicPersistentPile } location &&
        location.OwnerSeat == owner.Seat;

    private bool MatchesOwnPublicPileConversion(CharacterState owner, Card card,
        IndexedSkillProgramInstance instance, SkillProgramViewAs rule)
    {
        if (rule.SourcePileSkillId is not { } sourceSkill)
            return !IsOwnPublicPileCard(owner, card);
        if (!owner.IsAlive || !IsOwnPublicPileCard(owner, card)) return false;
        return ReferencedPublicPileSources(owner.Seat, sourceSkill, instance.SkillInstanceId)
            .Any(pile => pile.Location == _cardZones.GetLocation(card.Id) &&
                (sourceSkill != instance.SkillId || pile.SkillInstanceId == instance.SkillInstanceId) &&
                GetSkillBindingShard(owner).ProgramInstances.Any(source =>
                    source.SkillId == pile.SkillId && source.SkillInstanceId == pile.SkillInstanceId));
    }

    // Public piles are conversion materials, never an extension of the hand.
    // Keep them out of direct card uses, hand costs and equipment pair costs.
    private IReadOnlyList<Card> GetOwnPublicPileConversionCards(CharacterState owner)
    {
        if (!_contentRegistry.ProgramDependencies.UsesOwnPublicPileConversions || !owner.IsAlive || _publicPersistentPiles.Count == 0) return [];
        return GetSkillBindingShard(owner).ProgramInstances
            .SelectMany(instance => instance.Program.ViewAs
                .Where(rule => rule.SourcePileSkillId is not null)
                .SelectMany(rule => ReferencedPublicPileSources(owner.Seat, rule.SourcePileSkillId!, instance.SkillInstanceId)
                    .SelectMany(PublicPileCards)
                    .Where(card => MatchesOwnPublicPileConversion(owner, card, instance, rule))))
            .DistinctBy(card => card.Id).ToArray();
    }

    private Card? FindOwnPublicPileConversionCard(CharacterState owner, int cardId) =>
        GetOwnPublicPileConversionCards(owner).SingleOrDefault(card => card.Id == cardId);

    private void ValidateOwnPublicPileConversionCommit(CardConversionSource? source,
        CharacterState provider, IReadOnlyList<Card> materials, bool forResponse)
    {
        if (source is null && materials is [var original] &&
            (IsNativePublicPileOriginalChoice(provider, original, original.Kind, forResponse) ||
             !forResponse && IsFriendYangmingOriginalUseChoice(provider, original, original.Kind))) return;
        if (!materials.Any(card => IsOwnPublicPileCard(provider, card)) &&
            (source is null || ViewAsRule(source)?.SourcePileSkillId is null)) return;
        if (source is null || source.OwnerSeat != provider.Seat || materials.Count != 1 ||
            ViewAsRule(source) is not { SourcePileSkillId: not null } rule ||
            !GetProgramViewAsConversions(provider, materials[0], rule.OutputKind, forResponse).Contains(source))
            throw new InvalidOperationException("The public pile entity or its exact live conversion source is no longer legal at payment.");
    }
}
