using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Wpf.Training;

public enum TutorialAction { Attack, Dodge, Recover, Discard }

public sealed record TutorialLesson(TutorialAction Action, string Title, string Instruction, string Success, int Seed);

/// <summary>Short practice positions reached by ordinary commands, with no private state editing.</summary>
public static class TutorialScenario
{
    public static IReadOnlyList<TutorialLesson> Lessons { get; } =
    [
        new(TutorialAction.Attack, "使用杀", "选中一张「杀」，点击亮起的目标，再确认出牌。亮起表示符合出牌条件；实战还要判断敌友。", "杀已交给对手响应。对手若打出闪，就能抵消这次伤害。", 3),
        new(TutorialAction.Dodge, "用闪保护自己", "对手正在攻击你。选中手牌中亮起的「闪」，再点击「打出闪」或按 Enter。", "你已打出闪，抵消这次攻击；响应也会消耗一张手牌。", 39),
        new(TutorialAction.Recover, "用桃回复体力", "你已受伤。选中一张「桃」，确认使用，观察自己的体力。", "桃已回复 1 点体力。满体力时不能主动用桃，但可留给濒死救援。", 23),
        new(TutorialAction.Discard, "决定留下哪些牌", "手牌超过体力上限。选够提示要求的张数，再确认弃牌；可随时取消重选。", "弃牌已完成。你已练习攻击、响应、回复和弃牌，可以返回原局或从开局面板开始实战。", 13)
    ];

    public static GameEngine Create(TutorialLesson lesson)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = lesson.Seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2
        }, StandardContentRegistry.Create());
        Submit(game, new StartGameCommand());
        // Prepare a real point in a reproducible match. Bound the preparation in case rules drift.
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (IsReady(game, lesson.Action)) return game;
            var prompt = game.PendingDecision;
            GameCommand command = prompt?.Kind switch
            {
                DecisionKind.SelectGeneral => new SelectGeneralCommand(0, prompt.Choices[0].ContentIds[0], game.Revision, prompt.PromptId),
                DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                DecisionKind.DiscardCards => new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision),
                null => new AdvanceOneStepCommand(game.Revision),
                _ => new AnswerPromptCommand(0, prompt.PromptId, ChoosePreparationResponse(prompt).Id, game.Revision)
            };
            Submit(game, command);
        }
        throw new InvalidOperationException($"The {lesson.Action} practice position is no longer reachable with its recorded setup.");
    }

    private static PromptChoice ChoosePreparationResponse(PendingDecision prompt) =>
        prompt.Kind == DecisionKind.RescueDying ? prompt.Choices[0] :
            prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ?? prompt.Choices[0];

    private static bool IsReady(GameEngine game, TutorialAction action)
    {
        var prompt = game.PendingDecision;
        if (prompt is null) return false;
        var human = game.State.Players.Single(player => player.IsHuman);
        bool HasPlayable(CardKind kind) => game.GetHumanLegalActions().Any(legal =>
            legal.CardId is { } id && human.Hand.Any(card => card.Id == id && card.Kind == kind) &&
            (legal.PlayedCardKind is null || legal.PlayedCardKind == kind));
        return action switch
        {
            TutorialAction.Attack => prompt.Kind == DecisionKind.PlayCard && HasPlayable(CardKind.Slash),
            TutorialAction.Dodge => prompt.Kind == DecisionKind.RespondDodge && prompt.Choices.Any(choice =>
                choice.Cards.Count == 1 && human.Hand.Any(card => card.Id == choice.Cards[0] && card.Kind == CardKind.Dodge)),
            TutorialAction.Recover => prompt.Kind == DecisionKind.PlayCard && human.Hp < human.MaxHp && HasPlayable(CardKind.Peach),
            TutorialAction.Discard => prompt.Kind == DecisionKind.DiscardCards && prompt.RequiredCardCount >= 2,
            _ => false
        };
    }

    private static void Submit(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException($"Practice preparation failed: {result.Error?.Message}");
    }
}
