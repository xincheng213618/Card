using CardGame.Content.Standard;
using CardGame.Core;

internal static class BaguaResponseScenario
{
    private const string Mode = "fixture:bagua-response", Owner = "fixture:bagua-owner", Driver = "fixture:bagua-equip";

    internal static (GameEngine Game, ContentRegistry Registry) Create(bool red = false, bool arrows = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Package(red, arrows));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        Accept(game, new UseProgramSkillCommand(0, Driver, "equip", [], [0], game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        Require(game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Kind == CardKind.BaguaFormation),
            "The fixed Bagua fixture must equip a real armor entity through native equipment use.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.RespondDodge);
        Require(game.PendingDecision!.IncomingCard == (arrows ? CardKind.ArrowBarrage : CardKind.Slash) &&
            game.PendingDecision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "bagua") &&
            game.PendingDecision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "dodge"),
            "The real incoming attack must offer both equipped Bagua and a physical hand Dodge.");
        return (game, registry);
    }

    internal static void Answer(GameEngine game, string response)
    {
        var prompt = game.PendingDecision!;
        var choice = prompt.Choices.First(item => item.Parameters.GetValueOrDefault("response") == response);
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
    }

    internal static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"Bagua fixture did not reach {kind}: {game.PendingDecision?.Kind}.");
    }

    internal static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Bagua command rejected.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Package(bool red, bool arrows) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-bagua-response", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"fixture:bagua-equip","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,
                "minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":1,"effects":[
                {"op":"useRandomDeckEquipment","target":"owner","resultBind":"armor"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:bagua-equip":{"name":"装备夹具","description":"使用牌堆中的防具。"}}}""");
            builder.AddSkill(new(Driver, "装备夹具", "使用实体防具") { Program = catalog.Programs[Driver] });
            builder.AddGeneral(new(Owner, "八卦响应者", "supporter", "standard:none", "wei", 4, [Driver]));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:bagua-peer-{seat}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "攻击者", "supporter", "standard:none", "qun", 4));
            builder.AddDeck(new("fixture:bagua-deck", "八卦实体牌库", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(1, 80).Select(id => new ContentDeckPhysicalCard(
                    id == 1 ? "standard:bagua" : new[] { 21, 37, 61, 80 }.Contains(id)
                        ? arrows ? "standard:arrow_barrage" : "standard:slash" : "standard:dodge",
                    red ? Suit.Heart : Suit.Spade, id % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "八卦真实响应", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:bagua-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
