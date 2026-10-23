using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class MouSimaYiGiveScenario
{
    private const string Hero = "mou:sima-yi", Plain = "fixture:quanmou-giver", Deck = "fixture:quanmou-give-deck";

    public static (GameEngine Game, ContentRegistry Registry) Start(bool singleCard = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "quanmou-human-give", Plain, 12, 0, 0, 0, 0, [],
            singleCard ? [new(CardKind.CixiongDoubleSwords, Suit.Spade, 2)] :
            [new(CardKind.CixiongDoubleSwords, Suit.Spade, 2), new(CardKind.CixiongDoubleSwords, Suit.Club, 2),
             new(CardKind.ZhuqueFan, Suit.Diamond, 1)], singleCard ? [] : [new(CardKind.RenwangShield, Suit.Club, 2)],
            [new(Hero, Hp: 3) { StartingHand = [] }], [], new string('e', 64));
        var game = GameEngine.CreateStandard(new()
        {
            Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        for (var step = 0; step < 100; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt?.SkillPrompt?.SkillId == "mou:quanmou" && prompt.Choices.All(c => c.Cards.Count == 1))
                return (game, registry);
            if (prompt is { Kind: DecisionKind.PlayCard })
                Accept(game, new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
            else if (prompt is not null)
                throw new InvalidOperationException("Unexpected human choice before Quanmou Give: " + prompt.Kind);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The actual AI Quanmou did not reach its human Give.");
    }

    public static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-quanmou-human-give", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            builder.AddSkill(new("fixture:quanmou-giver-skill", "交牌者", "无主动技能。"));
            builder.AddGeneral(new(Plain, "交牌者", "", "fixture:quanmou-giver-skill", "shu", 12));
            foreach (var kind in new[] { CardKind.Dodge, CardKind.CixiongDoubleSwords, CardKind.ZhuqueFan, CardKind.RenwangShield })
            {
                var card = CardCatalog.Get(kind);
                builder.AddCard(new("fixture:quanmou-give-" + kind, card.DisplayName, card.CategoryName, card.Description, kind));
            }
            builder.AddDeck(new(Deck, "固定牌堆", 0, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard(
                    "fixture:quanmou-give-" + CardKind.Dodge, Suit.Club, 2)).ToArray()
            });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "权谋玩家交牌", 2, 2, new Dictionary<string, int>(),
                Deck, GeneralPoolIds: [Plain, Hero], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
