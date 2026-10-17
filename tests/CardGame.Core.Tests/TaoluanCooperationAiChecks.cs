using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TaoluanCooperationAiChecks
{
    public static void PublicInferenceAndPrivateGiftChoices()
    {
        var game = GameEngine.CreateStandard(new GameOptions { PlayerCount = 8, HumanSeat = 0, HumanRole = Role.Rebel },
            StandardContentRegistry.Create());
        var view = game.CreateSnapshot(0) with { Players = game.CreateSnapshot(0).Players.Select(p => p with
        {
            Role = p.Seat == 0 ? Role.Rebel : p.Seat == 7 ? Role.Lord : null,
            Hand = [], HandCount = 3, Equipment = [], IsAlive = true
        }).ToArray() };
        var choices = new[] { 1, 2, 3 }.Select(seat => new PromptChoice(new("provider-" + seat), "索牌", [], [seat],
            new Dictionary<string, string> { ["program-action"] = "select-target" })).ToArray();
        var brain = new SimpleAiBrain(0, 17, AiDecisionPolicy.CooperativeCategoryGiftVersion);
        var neutral = brain.ChooseActionCategoryGiftProvider(view, CardKind.DrawTwo, choices, 1);
        brain.ObserveSlash(2, 7, 7, Role.Lord);
        var inferred = brain.ChooseActionCategoryGiftProvider(view, CardKind.DrawTwo, choices, 1);
        Need(neutral.Choice != inferred.Choice && inferred.Choice == choices[1].Id && view.Players[2].Role is null,
            "Public attacks on the Lord let a Rebel infer an unrevealed ally and ask that ally instead of the first seat.");
        var hiddenFaces = view with { Players = view.Players.Select(p => p.Seat == 2 ? p with
        { Hand = [Card(991, CardKind.Peach)] } : p).ToArray() };
        Need(JsonSerializer.Serialize(inferred) == JsonSerializer.Serialize(
            brain.ChooseActionCategoryGiftProvider(hiddenFaces, CardKind.DrawTwo, choices, 1)),
            "Provider decisions and diagnostics do not inspect foreign hand faces.");
        var emptyAlly = view with { Players = view.Players.Select(p => p.Seat == 2 ? p with { HandCount = 0 } : p).ToArray() };
        Need(brain.ChooseActionCategoryGiftProvider(emptyAlly, CardKind.DrawTwo, choices, 1).Choice != choices[1].Id,
            "An ally with no hand or eligible public equipment cannot supply a card.");
        var donorView = view with { HumanSeat = 2, Players = view.Players.Select(p => p.Seat == 2 ? p with
        { Role = Role.Rebel, Hand = [Card(41, CardKind.Peach), Card(42, CardKind.Slash)], HandCount = 2 } :
            p.Seat == 0 ? p with { Role = null } : p).ToArray() };
        var gifts = new[]
        {
            Gift("gift-peach", [41]), Gift("gift-slash", [42]), Gift("decline", [])
        };
        var donor = new SimpleAiBrain(2, 17, AiDecisionPolicy.CooperativeCategoryGiftVersion);
        donor.ObserveSlash(0, 7, 7, Role.Lord);
        Need(donor.ChooseActionCategoryGift(donorView, 0, gifts) == gifts[1].Id &&
            donor.ChooseActionCategoryGift(donorView, 7, gifts) == gifts[2].Id &&
            donor.ChooseActionCategoryGift(donorView, 0, [gifts[2]]) == gifts[2].Id,
            "An inferred ally gives its lowest-value published card; an enemy or empty legal gift menu declines.");
    }

    private static CardSnapshot Card(int id, CardKind kind) => new(id, kind, Suit.Heart, 7, CardCatalog.Get(kind).DisplayName, "7");
    private static PromptChoice Gift(string id, int[] cards) => new(new(id), id, cards, [],
        new Dictionary<string, string> { ["program-action"] = cards.Length == 0 ? "different-action-category-decline" : "different-action-category-gift" });
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
