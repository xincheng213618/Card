namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 20)]
    private void AddSingleCardTrickConversionActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        var kinds = GetSkillBindingShard(actor).ProgramInstances.SelectMany(instance => instance.Program.ViewAs)
            .Where(rule => rule.SingleCardTrickUse).Select(rule => rule.OutputKind).Distinct().ToArray();
        if (kinds.Length == 0) return;
        foreach (var card in playable.Concat(GetEquipment(actor)).DistinctBy(card => card.Id)
                     .Where(card => selectedPhysicalCardId is null || card.Id == selectedPhysicalCardId)
                     .Where(card => !IsTurnHandCardRestricted(actor, card)))
        foreach (var kind in kinds)
        foreach (var source in GetProgramViewAsConversions(actor, card, kind, false)
                     .Where(source => ViewAsRule(source)?.SingleCardTrickUse == true))
        {
            // These opt-in rules always use the shared ordinary-trick pipeline,
            // including kinds that also have historical specialized view-as paths.
            actions.RemoveAll(action => action.CardId == card.Id && action.ConversionSource == source);
            foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind,
                         EffectiveSuit(actor, card), ViewAsRule(source)!.ExcludeOwnerEffects, enforceUsePermission: true, beneficiaryShieldSuit: EffectiveSuit(actor, ApplyProgramUseAppearance(actor, card, source)), actualEffectiveColor: SuitColor(EffectiveSuit(actor, ApplyProgramUseAppearance(actor, card, source))), hasActualColor: true, physicalCardIds:[card.Id], conversionSource:source))
                actions.Add(new LegalAction(option.ActionKind, card.Id,
                    option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,
                    DescribeConversion(source, option.Description), kind, option.TargetCardId,
                    option.TargetSeats) { ConversionSource = source });
        }
    }

    private bool TryExecuteSingleCardTrickConversion(CharacterState actor, Card card, LegalAction action)
    {
        if (action.ConversionSource is not { } source ||
            ViewAsRule(source) is not { SingleCardTrickUse: true } rule) return false;
        if (action.PlayedCardKind != rule.OutputKind ||
            !GetProgramViewAsConversions(actor, card, rule.OutputKind, false).Contains(source))
            throw new InvalidOperationException("The physical trick conversion is no longer available.");
        // ExecuteAction captured the exact published adjustment before this
        // conversion rechecks its ordinary base option. BeginCardUse consumes it.
        var adjustedTargets = IsTargetAdjustmentAction(actor, action) &&
            _selectedNextCardTargetSeats?.SequenceEqual(action.TargetSeats) == true;
        var option = BuildProgramOrdinaryTrickUseOptions(actor, rule.OutputKind,
            EffectiveSuit(actor, card), rule.ExcludeOwnerEffects, enforceUsePermission: true, beneficiaryShieldSuit: EffectiveSuit(actor, ApplyProgramUseAppearance(actor, card, source)), actualEffectiveColor: SuitColor(EffectiveSuit(actor, ApplyProgramUseAppearance(actor, card, source))), hasActualColor: true, physicalCardIds:[card.Id], conversionSource:source).Where(option =>
                option.ActionKind == action.Kind && option.TargetCardId == action.TargetCardId &&
                (option.TargetSeats.SequenceEqual(action.TargetSeats) || adjustedTargets)).OrderByDescending(option=>option.TargetSeats.Count).FirstOrDefault() ??
            throw new InvalidOperationException("The converted ordinary trick lost its legal targets.");
        var id = BeginCardUse(card, actor.Seat, option.TargetSeats, rule.OutputKind,
            conversionSource: source,
            designatedTargetSeats: rule.OutputKind == CardKind.BorrowedSword &&
                adjustedTargets && action.TargetSeats.Count >= 4
                ? action.TargetSeats.Where((_,index)=>index%2==0).ToArray() : null);
        if (action.TargetHandPreselection is not null)
            UpdateLifecycleCardUse(id, frame => frame with { TargetHandPreselection = action.TargetHandPreselection });
        var actualTargets = _resolutionStack.OfType<CardUseFrame>().Single(frame=>frame.Id==id).TargetSeats;
        MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, source.SkillId, source.BindingId,
            actor.Seat, new[] { card.Id }, rule.OutputKind, true, actualTargets));
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, actualTargets,
            option.ActionKind, option.TargetCardId, option.RequiredCardKind, rule.OutputKind);
        return true;
    }
}
