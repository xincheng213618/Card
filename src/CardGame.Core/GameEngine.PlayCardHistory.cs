namespace CardGame.Core;

public sealed partial class GameEngine
{
    // The content fingerprint opts into the new facts. Historical catalogs keep
    // their original event shape and replay behavior.
    private bool TracksPlayCardHistory => _contentRegistry?.Skills.Values.Any(skill =>
        skill.Program is { } program &&
        (program.Triggers.Any(t=>t.Effects.Any(e=>e.Op is SkillProgramEffectOp.ReplaceAllSlashTargets or SkillProgramEffectOp.GrantRandomSkillAndSuitShield)) || program.ViewAs.Any(rule => rule.InheritPreviousPlaySuit) ||
         program.Triggers.Any(trigger => HasTriggerCondition(trigger.Condition,
             SkillProgramTriggerConditionKind.CardActionMatchesPreviousPlayCard) ||
             HasTriggerCondition(trigger.Condition, SkillProgramTriggerConditionKind.CardActionSuitIs)))) == true;

    private static bool HasTriggerCondition(SkillProgramTriggerCondition condition,
        SkillProgramTriggerConditionKind kind) => condition.Kind == kind ||
        condition.Children.Any(child => HasTriggerCondition(child, kind));

    private static bool HasTriggerValue(SkillProgramTriggerCondition condition,
        SkillProgramTriggerValueKind kind) => condition.Left?.Kind == kind || condition.Right?.Kind == kind ||
        condition.Children.Any(child => HasTriggerValue(child, kind));

    private IEnumerable<IGameEvent> EventsSinceLastBoundary(Func<IGameEvent, bool> boundary)
    {
        var pendingBoundary = _pendingEvents.FindLastIndex(item => boundary(item));
        if (pendingBoundary >= 0) return _pendingEvents.Skip(pendingBoundary + 1);
        var flushedBoundary = _events.FindLastIndex(item => boundary(item.Payload));
        return _events.Skip(flushedBoundary + 1).Select(item => item.Payload).Concat(_pendingEvents);
    }

    private CardActionContext? PreviousPlayCard(int actorSeat, long beforeActionId = long.MaxValue)
    {
        if (_phase != TurnPhase.Play || actorSeat != _currentSeat) return null;
        return EventsSinceLastBoundary(item => item is PhaseChangedEvent or TurnStartedEvent)
            .OfType<CardUseAppearanceCapturedEvent>()
            .Select(item => item.Action)
            .Where(action => action.Type == CardActionType.Use && action.ActorSeat == actorSeat &&
                             action.ActionId < beforeActionId)
            .DistinctBy(action => action.ActionId)
            .MaxBy(action => action.ActionId);
    }

    private Suit? CaptureUsedCardSuit(int actorSeat, IReadOnlyList<int> physicalIds, Card card)
    {
        if (physicalIds.Count == 0) return null;
        if (physicalIds.Count == 1) return EffectiveSuit(_players[actorSeat], card);
        var suits = physicalIds.Select(id => EffectiveSuit(_players[actorSeat],
            _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(item => item.Id == id))).Distinct().ToArray();
        return suits.Length == 1 ? suits[0] : null;
    }

    private bool MatchesPreviousPlayCard(CardActionContext action)
    {
        var previous = PreviousPlayCard(action.ActorSeat, action.ActionId);
        return previous is not null &&
            (action.EffectiveSuit is { } suit && previous.EffectiveSuit == suit ||
             action.EffectiveRank is > 0 && previous.EffectiveRank == action.EffectiveRank);
    }

    private int DamageInstancesTakenThisTurn(int ownerSeat) =>
        EventsSinceLastBoundary(item => item is TurnStartedEvent)
            .OfType<DamageAppliedEvent>().Count(item => item.TargetSeat == ownerSeat && item.Amount > 0);

    private string ViewAsUsageId(CardConversionSource source, SkillProgramViewAs rule) =>
        $"view-as:{rule.UsageGroup ?? rule.Id}@{source.SkillInstanceId}";

    private bool CanUsePhaseLimitedViewAs(IndexedSkillProgramInstance instance,
        SkillProgramViewAs rule, CharacterState owner) => rule.UsesPerPhase is not { } limit ||
        (_phase == TurnPhase.Play && owner.Seat == _currentSeat &&
         _skillRuntimeState.GetUsage(owner.Seat, instance.SkillId,
             ViewAsUsageId(new(instance.SkillId, rule.Id, owner.Seat, instance.SkillInstanceId), rule),
             SkillUsageScope.Phase) < limit);

    private SkillProgramViewAs? ViewAsRule(CardConversionSource source) =>
        _contentRegistry?.Skills.GetValueOrDefault(source.SkillId)?.Program?.ViewAs
            .SingleOrDefault(rule => rule.Id == source.BindingId);

    private void ConsumeProgramViewAsUsage(IReadOnlyList<CardConversionSource> sources)
    {
        foreach (var source in sources)
        {
            ConsumeNamedUseConversion(source);
            if (ViewAsRule(source) is not { UsesPerPhase: { } limit } rule) continue;
            if (rule.ActivationUsageGroup is { } activationGroup)
            {
                var key = (source.OwnerSeat, source.SkillId, activationGroup);
                if (_programPhaseUses.GetValueOrDefault(key) >= limit) throw new InvalidOperationException("The shared activation allowance was consumed.");
                _programPhaseUses[key] = _programPhaseUses.GetValueOrDefault(key) + 1;
            }
            if (!_skillRuntimeState.TryConsumeUsage(source.OwnerSeat, source.SkillId,
                    ViewAsUsageId(source, rule), SkillUsageScope.Phase, limit))
                throw new InvalidOperationException("The view-as phase allowance was already consumed.");
            AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(source.OwnerSeat, source.SkillId,
                source.BindingId, SkillUsageScope.Phase, 1));
        }
    }

    private Card ApplyProgramUseAppearance(CharacterState owner, Card card, CardConversionSource? source) =>
        source is not null && ViewAsRule(source)?.InheritPreviousPlaySuit == true &&
        PreviousPlayCard(owner.Seat)?.EffectiveSuit is { } suit
            ? card with { Suit = suit }
            : card;

    private void AddPhaseLimitedBasicCardActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playableCards)
    {
        foreach (var card in playableCards.Concat(GetEquipment(actor)).DistinctBy(card => card.Id)
                     .Where(card => !IsTurnHandCardRestricted(actor, card)))
            foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
                         CardKind.Peach, CardKind.Alcohol })
                foreach (var source in GetProgramViewAsConversions(actor, card, kind, forResponse: false)
                             .Where(source => ViewAsRule(source) is { } rule && (rule.UsesPerPhase is not null || rule.UseOnly)))
                {
                    if (actions.Any(action => action.CardId == card.Id && action.ConversionSource == source &&
                        action.PlayedCardKind == kind)) continue;
                    var usedCard = ApplyProgramUseAppearance(actor, card, source);
                    var name = CardCatalog.Get(kind).DisplayName;
                    if (IsSlashCard(kind))
                    {
                        var targets = GetFangtianOrderedSlashTargets(actor, usedCard, source, effectiveKind: kind);
                        foreach (var target in targets)
                            actions.Add(new LegalAction(LegalActionKind.Slash, card.Id, target.Seat,
                                DescribeConversion(source, $"将【{card.DisplayName}】当作【{name}】对 {target.Name} 使用"),
                                PlayedCardKind: kind) { ConversionSource = source });
                        AddFangtianHalberdSlashActions(actions, actor, usedCard, targets, name, kind, source);
                        AddProgramTargetCountSlashActions(actions, actor, usedCard, targets, name, kind, source);
                    }
                    else if (kind == CardKind.Peach && actor.Hp < actor.MaxHp ||
                             kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !actor.UsedPlayPhaseAlcoholThisTurn)
                        actions.Add(new LegalAction(kind == CardKind.Peach ? LegalActionKind.Peach : LegalActionKind.Alcohol,
                            card.Id, kind == CardKind.Peach ? actor.Seat : null,
                            DescribeConversion(source, $"将【{card.DisplayName}】当作【{name}】使用"),
                            PlayedCardKind: kind) { ConversionSource = source });
                }
    }
}
