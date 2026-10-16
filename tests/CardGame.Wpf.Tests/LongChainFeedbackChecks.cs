using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class LongChainFeedbackChecks
{

    internal static (ContentRegistry Registry, GameEngine Game, CardSnapshot Slash) CreateFixture()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 2, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 8,
            ModeId = FixturePackage.Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Assert(game.Submit(new StartGameCommand()).Accepted, "The native eight-seat chain fixture did not start.");
        var setup = game.PendingDecision!;
        Assert(game.Submit(new SelectGeneralCommand(0, setup.Choices[0].ContentIds[0], game.Revision, setup.PromptId)).Accepted,
            "The native eight-seat fixture failed its actual general selection.");
        Assert(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "The native eight-seat fixture failed to enter play.");
        var publicHand = game.CreateSnapshot(0).Players[0].Hand;
        Console.WriteLine("Native eight-seat initial hand: " + string.Join(", ", publicHand.Select(card => $"{card.Id}:{card.Kind}")));
        Assert(publicHand.Count(card => card.Kind == CardKind.IronChain) >= 4 && publicHand.Any(card => card.Kind == CardKind.ThunderSlash),
            "The fixed native deck does not expose four actual chain cards and an actual elemental Slash.");
        for (var pair = 0; pair < 4; pair++)
        {
            var card = game.CreateSnapshot(0).Players[0].Hand.First(card => card.Kind == CardKind.IronChain);
            var action = game.GetHumanLegalActions().Single(candidate => candidate.Kind == LegalActionKind.IronChain &&
                candidate.CardId == card.Id && candidate.TargetSeats.SequenceEqual([pair * 2, pair * 2 + 1]));
            var decision = game.PendingDecision!;
            var result = game.Submit(new PlayCardCommand(0, card.Id, action.TargetSeats, game.Revision, decision.PromptId));
            Assert(result.Accepted, result.Error?.Message ?? "A native two-seat IronChain was rejected.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
                Assert(game.Submit(new AdvanceCommand(game.Revision)).Accepted, "The actual IronChain failed to return to play.");
        }
        var before = game.CreateSnapshot(0);
        Assert(before.Players.Count == 8 && before.Players.All(player => player.IsAlive && player.IsChained),
            "The native identity fixture did not legally chain all eight living seats.");
        var allPlayers = game.CreateSnapshot(0, revealAll: true).Players;
        Assert(allPlayers.All(player => player.Role is not null), "The diagnostic identity view omitted a native role.");
        var roles = allPlayers.GroupBy(player => player.Role!.Value).ToDictionary(group => group.Key, group => group.Count());
        Assert(roles.GetValueOrDefault(Role.Lord) == 1 && roles.GetValueOrDefault(Role.Loyalist) == 2 &&
            roles.GetValueOrDefault(Role.Rebel) == 4 && roles.GetValueOrDefault(Role.Renegade) == 1,
            "The eight-seat feedback fixture changed the required native identity counts.");
        var slash = before.Players[0].Hand.First(card => card.Kind == CardKind.ThunderSlash);
        return (registry, game, slash);
    }
    private sealed class FixturePackage : IGameContentPackage
    {
        public const string Mode = "identity:long-chain-feedback";
        public PackageManifest Manifest { get; } = new("long-chain-feedback", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var generals = Enumerable.Range(0, 8).Select(seat => $"fixture:long-chain-{seat}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new(id, "公开连环目标", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:long-chain-deck", "真实长连环实体牌", 8, 0, [])
            {
                // Verified seed 2 deals IDs 37,65,10,16,4,94,48,88 to Human0.
                // Retain physical Thunder37; make exactly the needed four chain costs 65,10,16,48.
                PhysicalCards = Enumerable.Range(0, 96).Select(index => new ContentDeckPhysicalCard(
                    index + 1 is 10 or 16 ? "standard:iron_chain" :
                    index % 3 == 0 ? "standard:thunder_slash" : "standard:iron_chain", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "八人身份长连环反馈", 8, 8,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1 },
                "fixture:long-chain-deck", GeneralCandidateCount: 8, GeneralPoolIds: generals));
        }
    }
}
