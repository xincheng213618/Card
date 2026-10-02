using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record StoneAxeBoundary(
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int TargetSeat,
    int StoneAxeCardId);

internal static class StoneAxeScenario
{
    private const string Mode = "identity:classic-stone-axe-fixture-5";

    public static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new StoneAxeFixturePackage());

    public static StoneAxeBoundary FindHumanTrigger(int seed = 1)
    {
        var registry = CreateRegistry();
        // The small pool/deck keeps seed 1 independent of ordinary roster growth.
        // Real Ma Su retains Zhiman's existing optional pre-damage child.
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            PlayerCount = 5,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 220,
            AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted,
            "Stone Axe fixture failed to start.");
        var generalChoice = game.PendingDecision?.Choices
            .FirstOrDefault(choice =>
                choice.ContentIds.Count == 1 &&
                choice.ContentIds[0] == "classic:ma-su");
        if (generalChoice is null)
        {
            throw new InvalidOperationException("The fixed Stone Axe witness no longer satisfies its setup boundary.");
        }

        Require(game.Submit(new SelectGeneralCommand(
            0,
            generalChoice.ContentIds[0],
            game.Revision,
            game.PendingDecision!.PromptId)).Accepted,
            "Stone Axe fixture could not select a general.");
        Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
            "Stone Axe fixture did not reach play.");
        if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
        {
            throw new InvalidOperationException("The fixed Stone Axe witness no longer satisfies its setup boundary.");
        }

        var full = game.CreateSnapshot(0, revealAll: true);
        var source = full.Players[0];
        var stoneAxe = source.Hand.FirstOrDefault(card => card.Kind == CardKind.StoneAxe);
        if (stoneAxe is null ||
            source.Hand.Count(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == 0)
        {
            throw new InvalidOperationException("The fixed Stone Axe witness no longer satisfies its setup boundary.");
        }

        var equipResult = game.Submit(new PlayCardCommand(
            0,
            stoneAxe.Id,
            [],
            game.Revision,
            play.PromptId));
        if (!equipResult.Accepted)
        {
            // A fixed witness must expose a real equip action; conversion-locked
            // generals cannot silently replace this fixture with another seed.
            throw new InvalidOperationException("The fixed Stone Axe witness no longer satisfies its setup boundary.");
        }
        var playAfterEquip = game.PendingDecision;
        if (playAfterEquip?.Kind != DecisionKind.PlayCard)
        {
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Stone Axe fixture did not return to play after equipping.");
            playAfterEquip = game.PendingDecision;
        }
        if (playAfterEquip?.Kind != DecisionKind.PlayCard)
        {
            throw new InvalidOperationException("The fixed Stone Axe witness no longer satisfies its setup boundary.");
        }

        full = game.CreateSnapshot(0, revealAll: true);
        source = full.Players[0];
        var slashAction = game.GetHumanLegalActions()
            .Where(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId is not null &&
                action.TargetSeat is not null &&
                source.Hand.Any(card =>
                    card.Id == action.CardId &&
                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
            .Select(action => new
            {
                Action = action,
                Target = full.Players.Single(player => player.Seat == action.TargetSeat)
            })
            .Where(item =>
                item.Target.Hp > 1 &&
                item.Target.Hand.Any(card => card.Kind == CardKind.Dodge) &&
                item.Target.Equipment.All(card =>
                    card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                item.Target.Skills?.All(skill =>
                    skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
            .OrderBy(item => item.Action.CardId)
            .ThenBy(item => item.Action.TargetSeat)
            .FirstOrDefault();
        if (slashAction is null)
        {
            throw new InvalidOperationException("The fixed Stone Axe witness no longer satisfies its setup boundary.");
        }

        var beforeSlash = game.CreateCheckpoint();
        var played = game.Submit(new PlayCardCommand(
            0,
            slashAction.Action.CardId!.Value,
            slashAction.Action.TargetSeats,
            game.Revision,
            playAfterEquip.PromptId,
            slashAction.Action.PlayedCardKind));
        Require(played.Accepted, played.Error?.Message ??
            "Stone Axe fixture could not use Slash.");
        for (var step = 0; step < 16 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.StoneAxe, PlayerSeat: 0 })
            {
                return new StoneAxeBoundary(
                    game,
                    beforeSlash,
                    slashAction.Action,
                    slashAction.Target.Seat,
                    stoneAxe.Id);
            }

            if (game.PendingDecision is { PlayerSeat: 0 })
            {
                break;
            }

            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Stone Axe fixture could not advance the target's Dodge response.");
        }

        throw new InvalidOperationException(
            $"The verified seed {seed} classic Stone Axe witness did not reach its human trigger.");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StoneAxeFixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-stone-axe", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:stone-axe-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new(target, "贯石斧对手", "supporter", "standard:none", "qun", BaseHp: 4));
            builder.AddDeck(new("fixture:stone-axe-deck", "贯石斧支付与伤害", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 60).Select(index => new ContentDeckPhysicalCard(
                    (index % 3) switch { 0 => "classic:stone-axe", 1 => "standard:slash", _ => "standard:dodge" },
                    Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "贯石斧支付与伤害", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:stone-axe-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["classic:ma-su", .. targets]));
        }
    }
}
