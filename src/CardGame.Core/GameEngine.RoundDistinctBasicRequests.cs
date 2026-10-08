namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RoundDistinctBasicRequestMarker = "round-distinct-basic-request";

    private IReadOnlyList<PromptChoice> RoundDistinctBasicRequestedSlashChoices(ProgramSkillFrame frame,
        int actorSeat, IReadOnlyList<int> targetSeats, string? resultBind = null)
    {
        if (!_contentRegistry.ProgramDependencies.UsesRoundDistinctBasicUse || !IsValidPlayerSeat(actorSeat) ||
            !_players[actorSeat].IsAlive) return [];
        var actor = _players[actorSeat];
        var choices = new List<PromptChoice>();
        var nearest = resultBind is null;
        var action = nearest ? "request-slash-nearest" : "request-slash";
        foreach (var selection in GetProgramMultiCardViewAsSelections(actor, CardKind.Slash, forResponse: false)
                     .Where(s => ViewAsRule(s.Source)?.RoundDistinctBasicUse is not null && IsSlashCard(s.OutputKind))
                     .DistinctBy(s => (s.Source, s.OutputKind, Cards: string.Join('-', s.Cards.Select(c => c.Id).Order()))))
        foreach (var targetSeat in targetSeats)
        {
            if (!RoundDistinctBasicRequestedSlashTarget(actor, selection, targetSeat)) continue;
            var ids = selection.Cards.Select(card => card.Id).ToArray();
            var parameters = new Dictionary<string, string>
            {
                ["program-action"] = action,
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["card-id"] = ids[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["effective-kind"] = selection.OutputKind.ToString(),
                [RoundDistinctBasicRequestMarker] = "true"
            };
            if (nearest)
            {
                parameters["seat"] = actorSeat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                parameters["target-seat"] = targetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else parameters["result-bind"] = resultBind!;
            AddConversionParameters(parameters, selection.Source);
            choices.Add(FreezeDirectedDistanceChoice(new PromptChoice(
                new ChoiceId($"program-{action}.frame-{frame.Id}.pair.{selection.Source.SkillId}.{selection.Source.BindingId}.{selection.Source.SkillInstanceId}.{selection.OutputKind}.{string.Join('-', ids)}.seat-{targetSeat}"),
                DescribeConversion(selection.Source, $"将两张牌当【{CardCatalog.Get(selection.OutputKind).DisplayName}】使用，攻击 {_players[targetSeat].Name}。"),
                ids, [targetSeat], parameters)));
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private bool RoundDistinctBasicRequestedSlashTarget(CharacterState actor,
        ProgramMultiCardViewAsSelection selection, int targetSeat) =>
        IsValidPlayerSeat(targetSeat) && actor.IsAlive &&
        // These two existing requests supply their own fixed/nearest target
        // frontier and do not spend the normal Play Slash quota or add a range
        // precheck. Preserve that contract while validating this new true Use.
        CanUseSlashTarget(actor, _players[targetSeat], selection.Cards[0], selection.Source,
            selection.OutputKind, ignoreDistance: true, noEffectiveRank: true,
            physicalCardIds: selection.Cards.Select(card => card.Id).ToArray()) &&
        !IsCardTargetProhibited(_players[targetSeat], selection.OutputKind,
            PhysicalGroupSuit(actor, selection.Cards), PhysicalGroupColor(actor, selection.Cards));

    private ProgramMultiCardViewAsSelection RequireRoundDistinctBasicRequestedSlash(ProgramSkillFrame frame,
        PendingDecision decision, PromptChoice selected, int actorSeat, IReadOnlyList<int> targetSeats,
        string? resultBind = null)
    {
        if (selected.Parameters.GetValueOrDefault(RoundDistinctBasicRequestMarker) != "true" ||
            decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate || decision.PlayerSeat != actorSeat ||
            decision.SourceSeat != frame.OwnerSeat || decision.TargetSeat != actorSeat || decision.SkillPrompt?.SkillId != frame.SkillId ||
            decision.Choices.SingleOrDefault(choice => choice.Id == selected.Id) is not { } published ||
            !AssistedChoicesEqual([published], [selected]) ||
            RoundDistinctBasicRequestedSlashChoices(frame, actorSeat, targetSeats, resultBind)
                .SingleOrDefault(choice => choice.Id == selected.Id) is not { } canonical ||
            !AssistedChoicesEqual([canonical], [selected]) || selected.Cards.Count != 2 || selected.Targets.Count != 1 ||
            !Enum.TryParse<CardKind>(selected.Parameters.GetValueOrDefault("effective-kind"), out var kind) || !IsSlashCard(kind))
            throw new InvalidOperationException("A requested pair Slash changed its published source, materials, kind or native target frontier.");
        var source = RequireConversionSource(selected);
        if (ViewAsRule(source)?.RoundDistinctBasicUse is null ||
            FindProgramMultiCardViewAsSelection(_players[actorSeat], selected.Cards, kind, false, source) is not { } selection ||
            selection.OutputKind != kind || !RoundDistinctBasicRequestedSlashTarget(_players[actorSeat], selection, selected.Targets[0]))
            throw new InvalidOperationException("A requested pair Slash lost its exact currently legal conversion payment.");
        return selection;
    }
}
