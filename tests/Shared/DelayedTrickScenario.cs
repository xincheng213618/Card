using CardGame.Content.Standard;
using CardGame.Core;

internal static class DelayedTrickScenario
{
    internal const string Owner = "fixture:delayed-owner";
    internal static (GameEngine Game, ContentRegistry Registry) Create(bool unrespondable = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(unrespondable));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "identity:classic-delayed-fixture", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        for (var i = 0; i < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "Fixed delayed fixture did not reach human play.");
        Require(game.CreateSnapshot(0).Players[0].Hand.Count(c => c.Kind == CardKind.Nullification) == 2 &&
            Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).Players[seat].Hand.All(c => c.Kind != CardKind.Nullification)),
            "The fixed seed must give exactly two counterspells to the human and none to AI peers.");
        return (game, registry);
    }

    internal static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Delayed fixture command rejected.");
    }
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(bool unrespondable) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-delayed-resolution", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "真实断粮目标", "supporter", "boundary:duanliang-current", "wei", 12,
                unrespondable ? ["boundary:kanpo-current"] : []));
            var quiet = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:delayed-quiet","revision":1,
                "triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["indulgence","supplyShortage","lightning","fireAttack"],"actionTypes":["use"]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:delayed-quiet":{"name":"安静判定夹具","description":"AI只推进真实回合"}}}""");
            builder.AddSkill(new("fixture:delayed-quiet", "安静判定夹具", "AI只推进真实回合") { Program = quiet.Programs["fixture:delayed-quiet"] });
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:delayed-target-{seat}").ToArray();
            foreach (var id in peers) builder.AddGeneral(new(id, "真实判定目标", "supporter", "fixture:delayed-quiet", "shu", 12));
            string[] kinds = ["standard:indulgence", "standard:supply_shortage", "standard:lightning",
                "standard:nullification", "standard:dodge", "standard:fire_attack"];
            builder.AddDeck(new("fixture:delayed-deck", "固定延时实体", 12, 0, [])
            {
                // Verified seed 31 deals entities 76 and 95 only to the owner.
                PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(i + 1 is 76 or 95 ? "standard:nullification" :
                    i % kinds.Length == 3 ? "standard:dodge" : kinds[i % kinds.Length],
                    i % kinds.Length == 5 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-delayed-fixture", "延时触发回归", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:delayed-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
