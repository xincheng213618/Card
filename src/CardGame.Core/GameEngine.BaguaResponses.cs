namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static PromptChoice BaguaSkipChoice(bool factionDefense = false) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_BaguaResponses.BaguaSkipChoice(factionDefense);

    private void SkipBaguaResponseCore()
    {
        RequireHumanDecision(DecisionKind.RespondDodge);
        var decision = _pendingDecision!;
        _pendingDecision = decision with
        {
            PromptId = CreatePromptId(),
            Prompt = "你未发动【八卦阵】，是否打出【闪】？",
            Choices = decision.Choices.Where(choice => choice.Parameters.GetValueOrDefault("response") is not
                ("bagua" or "faction-defense-bagua" or "bagua-skip")).ToArray()
        };
        AdvanceRulesAndPublishState();
        if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
    }

    // A failed armor judgment has not supplied a Dodge. Reopen the same response
    // opportunity without offering another armor judgment for that Dodge.
    private void PublishFailedBaguaResponse(CardAttackHandle attack)
    {
        var defense = ActiveFactionDefense;
        var responder = _players[defense?.CurrentCandidateSeat ?? attack.TargetSeat];
        var cards = GetResponseCards(responder, CardKind.Dodge);
        if (defense is null && ConvertedSlashSameColorResponseColor(attack) is { } requireRed)
            cards = cards.Where(card => IsRedSuit(EffectiveSuit(responder, card)) == requireRed).ToList();
        if (defense is null)
            PushResponseWindow(attack.ResolutionId, attack.SourceSeat, responder.Seat,
                attack.EffectiveCardKind ?? CardKind.Slash, CardKind.Dodge);
        var choices = defense is not null
            ? CreateFactionDefenseResponseChoices(responder, defense, cards, includeBagua: false)
            : CreateResponseChoices(cards, CardKind.Dodge, attack.EffectiveCardKind,
                card => GetEffectiveResponseKind(responder, card, CardKind.Dodge),
                includeBagua: false, includeFactionDefense: CanRequestFactionDefense(responder, attack),
                responder: responder);
        _pendingDecision = new PendingDecision(DecisionKind.RespondDodge, responder.Seat,
            "【八卦阵】判定失败，是否继续打出【闪】？", cards.Select(card => card.Id).ToArray(), [],
            attack.SourceSeat, attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(), RequiredCardKind = CardKind.Dodge,
            TargetSeat = defense?.OwnerSeat, Choices = choices
        };
        _status = responder.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
}
