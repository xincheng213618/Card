namespace CardGame.Core;

public sealed partial class GameEngine
{
    // These producers collect an unpaid set on their frame and move it only
    // after the last selection. Publish its remaining count to the chooser.
    private PendingDecision ProjectCardPaymentCount(PendingDecision decision)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame) return decision;
        if (frame.AnniversaryYangBiao is { Stage: AnniversaryYangBiaoStage.Materials, Branch: "discard" or "rescue" } yangbiao &&
            decision.PlayerSeat == frame.OwnerSeat &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "anniversary-yang-biao"))
            return ProjectCollectedOwnedCards(decision, frame, "anniversary-yang-biao-discard:" + yangbiao.Branch,
                yangbiao.Amount - yangbiao.Cards.Count, selected: yangbiao.Cards);
        if (frame.AnniversaryHeYan is { Stage: AnniversaryHeYanStage.DiscardChoice } heyan &&
            decision.PlayerSeat == heyan.SourceSeat &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "anniversary-he-yan"))
            return ProjectCollectedOwnedCards(decision, frame, "anniversary-he-yan-discard", heyan.RequiredDiscard - heyan.Selected.Count, selected: heyan.Selected);
        if (frame.OlLuShi is { Stage: OlLuShiStage.Discard } lushi &&
            decision.PlayerSeat == lushi.TargetSeat &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "ol-lushi"))
            return ProjectCollectedOwnedCards(decision, frame, "ol-lushi-discard", lushi.RequiredCount - lushi.Cards.Count, selected: lushi.Cards);
        if (frame.ShenXuChu is { Stage: ShenXuChuStage.DiscardChoice } xuchu &&
            decision.PlayerSeat == xuchu.TargetSeat &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == ShenXuChuAction))
            return ProjectCollectedOwnedCards(decision, frame, "shen-xu-chu-discard:" + xuchu.TargetIndex,
                xuchu.RequiredDiscards - xuchu.Selected.Count, selected: xuchu.Selected);
        if (frame.LianZhu is { Stage: LianZhuStage.SelectingDiscard } lianzhu &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "lianzhu"))
            return ProjectCollectedOwnedCards(decision, frame, "lianzhu-discard", LianZhuChoiceAmount(lianzhu) - lianzhu.Selected.Count, selected: lianzhu.Selected);
        if (frame.NamedTurnCountFlow is { Stage: "hand" } named &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "named-turn-flow"))
            return ProjectCollectedOwnedCards(decision, frame, "named-turn-hand", named.Required - named.SelectedIds.Count, selected: named.SelectedIds);
        if (frame.DrawAdviceReceipt is { Stage: DrawAdviceStage.ChoosingDiscard } advice &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "draw-advice-discard"))
            return ProjectCollectedOwnedCards(decision, frame, "draw-advice-discard:" + advice.ParticipantCursor,
                advice.RequiredDiscardCount - advice.SelectedDiscardCardIds.Count, selected: advice.SelectedDiscardCardIds);
        if (frame.DamageSourceHandChoice is { Stage: DamageSourceHandChoiceStage.DiscardSelection } hand &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "damage-source-hand"))
            return ProjectCollectedOwnedCards(decision, frame, "damage-source-hand-discard",
                decision.Choices.Count(choice => choice.Parameters.GetValueOrDefault("branch") == "card"),
                Math.Max(0, hand.SourceHandCount + 1 - hand.SelectedCards.Count),
                choice => choice.Parameters.GetValueOrDefault("branch") == "pay", hand.SelectedCards);
        if (frame.WeaponDamageDraft is { } weapon &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "weapon-damage") &&
            decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("branch") is "card" or "finish"))
            return ProjectCollectedOwnedCards(decision, frame, "weapon-damage-discard", weapon.RequiredCount - weapon.SelectedIds.Count,
                finish: choice => choice.Parameters.GetValueOrDefault("branch") == "finish", selected: weapon.SelectedIds);
        if (frame.BlackGiftContest is { Stage: BlackGiftContestStage.ChoosingDiscard } gift &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "black-gift-contest"))
            return ProjectCollectedOwnedCards(decision, frame, "black-gift-discard", gift.RequiredDiscards - gift.SelectedDiscardIds.Count, selected: gift.SelectedDiscardIds);
        if (frame.Kuangfu is { Stage: KuangfuStage.HandDiscardChoice } kuangfu &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "kuangfu"))
            return ProjectCollectedOwnedCards(decision, frame, "kuangfu-hand-discard", kuangfu.DiscardRequired - kuangfu.SelectedHandIds.Count, selected: kuangfu.SelectedHandIds);
        if (frame.FriendTrio is { Stage: FriendTrioStage.Discard } friend &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "friend-trio"))
            return ProjectCollectedOwnedCards(decision, frame, "friend-trio-discard",
                Math.Min(friend.Count - friend.Cards.Count, decision.Choices.Count(choice => choice.Cards.Count == 1)), selected: friend.Cards);
        if (frame.PublicSuitDiscard is { Stage: 2 } suits &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "public-suit-discard"))
            return ProjectCollectedOwnedCards(decision, frame, "public-suit-discard", suits.Required - (suits.SelectedIds?.Count ?? 0), selected: suits.SelectedIds);
        if (frame.DiscardChallenge is { Mode: "escalating" } challenge &&
            decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "discard-challenge"))
            return ProjectCollectedOwnedCards(decision, frame, "discard-challenge:" + challenge.Cursor,
                decision.Choices.Count(choice => choice.Parameters.GetValueOrDefault("branch") == "card"),
                Math.Max(0, challenge.PreviousCount + 1 - challenge.SelectedIds.Count),
                choice => choice.Parameters.GetValueOrDefault("branch") == "finish", challenge.SelectedIds);
        if (frame.PileEquipment is { Stage: "store-choice" } &&
                decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "game-domain") ||
            frame.PublicPilePreparation is { Stage: PublicPilePreparationStage.Choosing,
                Operation: SkillProgramEffectOp.StoreNonBasicOwnedPublicPile } &&
                decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "public-pile-preparation"))
            return decision with
            {
                RequiredCardCount = decision.Choices.SelectMany(choice => choice.Cards).Distinct().Count(),
                MinimumCardCount = 0
            };
        var action = decision.Choices.FirstOrDefault(choice => choice.Cards.Count == 1)?
            .Parameters.GetValueOrDefault("program-action");
        if (action == "alternating-suit-top" &&
            frame.AlternatingSuitTop is { Mode: "alternating", Stage: "discard" } alternating)
            return decision with
            {
                RequiredCardCount = alternating.Required - alternating.SelectedIds.Count,
                MinimumCardCount = 1
            };
        int? remaining = action switch
        {
            "lianzhu" when frame.LianZhu is { Stage: LianZhuStage.SelectingDiscard, CountPolicy: { } count } cost => count.Amount - cost.Selected.Count,
            "dynamic-discard-damage" when frame.DynamicDiscardDamage is { } cost => cost.FrozenTargetHp - cost.CardIds.Count,
            "game-hand-hp-discard" when frame.GameTargetHandHp is { } cost => cost.RequiredCount - cost.CardIds.Count,
            "draw-discard-category" when frame.DrawDiscardCategoryRefund is { } cost => cost.RequiredCount - cost.SelectedCardIds.Count,
            "capped-hand-discard" when frame.CappedHandRefresh is { } cost => cost.DiscardCount - cost.SelectedCardIds.Count,
            "dying-suits" when frame.DyingSuits is { } cost => cost.RequiredDiscardCount - cost.SelectedCardIds.Count,
            "prep-discard-ending" when frame.PrepDiscard is { } cost => cost.RequiredCount - cost.SelectedCardIds.Count,
            _ => null
        };
        return remaining is > 0 ? decision with { RequiredCardCount = remaining.Value } : decision;
    }

    private static PendingDecision ProjectCollectedOwnedCards(PendingDecision decision, ProgramSkillFrame frame,
        string stage, int maximum, int? minimum = null, Func<PromptChoice, bool>? finish = null,
        IReadOnlyList<int>? selected = null) => decision with
    {
        RequiredCardCount = Math.Max(0, maximum),
        MinimumCardCount = minimum ?? Math.Max(0, maximum),
        Choices = Array.AsReadOnly(decision.Choices.Select(choice =>
        {
            // The view annotates an existing command; its ID, cards, targets and
            // native parameters are retained verbatim for actual answer validation.
            var parameters = new Dictionary<string, string>(choice.Parameters)
            {
                ["owned-selection-frame"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["owned-selection-stage"] = stage
            };
            if (selected is { Count: > 0 }) parameters["owned-selection-selected"] = string.Join(',', selected);
            if (finish?.Invoke(choice) == true) parameters["owned-selection-role"] = "finish";
            else if (choice.Cards.Count == 1) parameters["owned-selection-role"] = "pick";
            return choice with { Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(parameters) };
        }).ToArray())
    };
}
