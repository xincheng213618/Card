using CardGame.Core;

namespace CardGame.Lan;

/// <summary>Publish actor-scoped input handles; clients cannot submit host commands or choose an actor.</summary>
public static class LanActions
{
    public static IReadOnlyList<LanAction> Describe(GameEngine game, int seat)
    {
        var prompt = game.CreateSnapshot(seat).PendingDecision;
        if (prompt is null) return [];
        if (prompt.Kind == DecisionKind.SelectGeneral)
            return prompt.ValidContentIds.Select(id => new LanAction("general:" + id,
                game.ContentRegistry!.Generals[id].Name + " · " + string.Join("；", game.ContentRegistry.Generals[id].SkillIds
                    .Select(skill => game.ContentRegistry.GetSkill(skill).Name + "：" + game.ContentRegistry.GetSkill(skill).Description)))).ToArray();
        if (prompt.Kind == DecisionKind.DiscardCards)
            return [new("discard", $"确认弃置 {prompt.RequiredCardCount} 张牌", prompt.RequiredCardCount,
                prompt.RequiredCardCount, CardIds: prompt.ValidCardIds)];
        if (prompt.Kind == DecisionKind.PlayCard)
            return game.GetLegalActions(seat).Select((action, index) =>
                action.Kind is LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill
                    ? new LanAction("play:" + index, action.Description,
                        action.MinCardCount, action.MaxCardCount, action.MinTargetCount, action.MaxTargetCount,
                        action.SelectableCardIds, action.SelectableTargetSeats)
                    : new LanAction("play:" + index, action.Description)).ToArray();
        return prompt.Choices.Select(choice => new LanAction("choice:" + choice.Id.Value, choice.Description)).ToArray();
    }

    public static GameCommand CreateCommand(GameEngine game, int seat, RoomInput input)
    {
        var view = game.CreateSnapshot(seat);
        var prompt = view.PendingDecision ?? throw new RoomException("现在没有轮到你操作。", 409);
        if (input.ExpectedRevision != view.Revision || input.PromptId != prompt.PromptId.Value)
            throw new RoomException("牌局已经更新，请根据最新提示重新选择。", 409);
        var action = Describe(game, seat).SingleOrDefault(a => a.Id == input.ActionId)
            ?? throw new RoomException("这项操作已不可用。", 409);
        var cards = input.CardIds ?? [];
        var targets = input.TargetSeats ?? [];
        if (cards.Length < action.MinCards || cards.Length > action.MaxCards || cards.Distinct().Count() != cards.Length ||
            targets.Length < action.MinTargets || targets.Length > action.MaxTargets || targets.Distinct().Count() != targets.Length ||
            cards.Any(id => action.CardIds?.Contains(id) != true) || targets.Any(id => action.TargetSeats?.Contains(id) != true))
            throw new RoomException("选择的牌或目标不符合当前操作要求。");
        if (input.ActionId.StartsWith("general:", StringComparison.Ordinal))
            return new SelectGeneralCommand(seat, input.ActionId[8..], view.Revision, prompt.PromptId);
        if (input.ActionId == "discard")
            return new DiscardCardsCommand(seat, cards, prompt.PromptId, view.Revision);
        if (input.ActionId.StartsWith("choice:", StringComparison.Ordinal))
            return new AnswerPromptCommand(seat, prompt.PromptId, new ChoiceId(input.ActionId[7..]), view.Revision);
        var legal = game.GetLegalActions(seat)[int.Parse(input.ActionId[5..], System.Globalization.CultureInfo.InvariantCulture)];
        return legal.Kind switch
        {
            LegalActionKind.EndPlay => new EndPlayPhaseCommand(seat, view.Revision, prompt.PromptId),
            LegalActionKind.UseEquipmentEffect => new UseEquipmentEffectCommand(seat, legal.EquipmentKind!.Value,
                cards, targets, view.Revision, prompt.PromptId),
            LegalActionKind.UseProgramSkill => new UseProgramSkillCommand(seat, legal.ProgramSkillId!, legal.ProgramActivationId!,
                cards, targets, view.Revision, prompt.PromptId) { SkillOwnerSeat = legal.ProgramSkillOwnerSeat },
            LegalActionKind.Recast => new RecastCardCommand(seat, legal.CardId!.Value, view.Revision, prompt.PromptId)
                { ConversionSource = legal.ConversionSource },
            _ => new PlayCardCommand(seat, legal.CardId!.Value, legal.TargetSeats, view.Revision, prompt.PromptId,
                legal.PlayedCardKind, legal.TargetCardId)
                { ConversionSource = legal.ConversionSource, AdditionalConversionSources = legal.AdditionalConversionSources }
        };
    }
}
