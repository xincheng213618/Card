namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AddSingleCardTrickConversionActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playable)
    {
        var kinds = GetSkillBindingShard(actor).ProgramInstances.SelectMany(instance => instance.Program.ViewAs)
            .Where(rule => rule.SingleCardTrickUse).Select(rule => rule.OutputKind).Distinct().ToArray();
        foreach (var card in playable.Concat(GetEquipment(actor)).DistinctBy(card => card.Id)
                     .Where(card => !IsTurnHandCardRestricted(actor, card)))
        foreach (var kind in kinds)
        foreach (var source in GetProgramViewAsConversions(actor, card, kind, false)
                     .Where(source => ViewAsRule(source)?.SingleCardTrickUse == true))
        {
            // These opt-in rules always use the shared ordinary-trick pipeline,
            // including kinds that also have historical specialized view-as paths.
            actions.RemoveAll(action => action.CardId == card.Id && action.ConversionSource == source);
            foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, kind,
                         EffectiveSuit(actor, card), ViewAsRule(source)!.ExcludeOwnerEffects, enforceUsePermission: true))
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
        var option = BuildProgramOrdinaryTrickUseOptions(actor, rule.OutputKind,
            EffectiveSuit(actor, card), rule.ExcludeOwnerEffects, enforceUsePermission: true).SingleOrDefault(option =>
                option.ActionKind == action.Kind && option.TargetCardId == action.TargetCardId &&
                option.TargetSeats.SequenceEqual(action.TargetSeats)) ??
            throw new InvalidOperationException("The converted ordinary trick lost its legal targets.");
        var id = BeginCardUse(card, actor.Seat, option.TargetSeats, rule.OutputKind,
            conversionSource: source);
        MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use);
        QueueGameEvent(new ProgramViewAsConvertedEvent(id, source.SkillId, source.BindingId,
            actor.Seat, new[] { card.Id }, rule.OutputKind, true, option.TargetSeats));
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, option.TargetSeats,
            option.ActionKind, option.TargetCardId, option.RequiredCardKind, rule.OutputKind);
        return true;
    }
}
