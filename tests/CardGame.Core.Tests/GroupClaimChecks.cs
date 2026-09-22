using CardGame.Content.Standard;
using CardGame.Core;

internal static class GroupClaimChecks
{
    public static void OnlyClaimedCardsMayFinishFromTheDrawPile()
    {
        var claimedIds = new HashSet<int>();
        var physicalCards = new[]
        {
            new Card(101, CardKind.Slash, Suit.Spade, 1),
            new Card(102, CardKind.Dodge, Suit.Spade, 2)
        };
        Require(GameEngine.IsResolvedGroupPhysicalCardDestinationAllowed(
                    CardLocation.DrawPile, damageCardClaimed: true, [1, 2]) &&
                !GameEngine.IsResolvedGroupPhysicalCardDestinationAllowed(
                    CardLocation.DrawPile, damageCardClaimed: false, [1, 2]) &&
                GameEngine.IsResolvedGroupPhysicalCardDestinationAllowed(
                    CardLocation.DiscardPile, damageCardClaimed: false, [1, 2]) &&
                GameEngine.IsResolvedGroupPhysicalCardDestinationAllowed(
                    CardLocation.Hand(2), damageCardClaimed: false, [1, 2]) &&
                !GameEngine.IsResolvedGroupPhysicalCardDestinationAllowed(
                    CardLocation.Hand(3), damageCardClaimed: true, [1, 2]) &&
                GameEngine.RecordClaimedGroupPhysicalCard(
                    claimedIds, 17, physicalCards, 17, physicalCards[1].Id) &&
                claimedIds.SetEquals([physicalCards[1].Id]) &&
                !GameEngine.RecordClaimedGroupPhysicalCard(
                    claimedIds, 17, physicalCards, 18, physicalCards[0].Id) &&
                !GameEngine.RecordClaimedGroupPhysicalCard(
                    claimedIds, 17, physicalCards, 17, 999),
            "Only a corresponding physical group card claimed by a damage skill may finish from a reshuffled draw pile; hand destinations remain target-scoped.");
    }

    public static void ClaimantDeathContinues()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new ClaimScenario());
        var game = CreateCurrentFixture(registry);
        var claim = game.Events.Select(e => e.Payload).OfType<ProgramDamageCardsClaimedEvent>().Last();
        var use = game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Last();
        Require(claim.CardIds.Contains(use.CardId) && use.CardKind == CardKind.ArrowBarrage &&
            game.State.Players.Single(player => player.Seat == claim.OwnerSeat) is { IsAlive: true, Hp: 0 },
            "Fixture must suspend a real claimed group card before its owner dies.");
        var claimedCardId = use.CardId;
        var start = game.Events.Count;
        for (var step = 0; step < 100 && !game.Events.Skip(start).Any(e =>
            e.Payload is CardUseFinishedEvent finished && finished.ResolutionId == use.ResolutionId); step++)
        {
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
                ? new AnswerPromptCommand(0, prompt.PromptId, (prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) ?? prompt.Choices[0]).Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            Require(game.Submit(command).Accepted, "Claimed group effect could not continue after the claimant's death.");
        }
        Require(!game.State.Players.Single(player => player.Seat == claim.OwnerSeat).IsAlive, "Claimant did not die.");
        var events = game.Events.Skip(start).Select(e => e.Payload).ToArray();
        Require(events.OfType<CardMovedEvent>().Any(e => e.CardId == claimedCardId &&
            e.From == CardLocation.Hand(claim.OwnerSeat) && e.To == CardLocation.DiscardPile), "Death did not clean up the claimed physical card.");
        Require(events.OfType<GroupResponseEvent>().Any(e => e.ResolutionId == use.ResolutionId && e.ResponderSeat == (claim.OwnerSeat + 1) % 8),
            "The surviving next target never responded to the existing group effect.");
        Require(events.OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.ResolutionId) == 1 &&
            game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0, "Group resolution did not finish exactly once.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "The corrected group continuation did not replay deterministically.");
    }

    private static GameEngine CreateCurrentFixture(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 8, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = ClaimScenario.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Claim scenario did not start.");
        var setup = game.PendingDecision!;
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:group-source", game.Revision, setup.PromptId)).Accepted,
            "Claim scenario could not select its source.");
        for (var step = 0; step < 32 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Claim scenario did not reach Play.");
        var play = game.PendingDecision!;
        var arrow = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.ArrowBarrage);
        Require(game.Submit(new PlayCardCommand(0, arrow.CardId!.Value, arrow.TargetSeats, game.Revision, play.PromptId)).Accepted,
            "Claim scenario could not declare Arrow Barrage.");
        for (var step = 0; step < 128; step++)
        {
            if (game.Events.Select(item => item.Payload).OfType<ProgramDamageCardsClaimedEvent>().LastOrDefault() is { } claim &&
                game.State.Players[claim.OwnerSeat] is { IsAlive: true, Hp: 0 }) return game;
            var result = game.PendingDecision is { PlayerSeat: 0 } prompt
                ? game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
                    (prompt.Kind == DecisionKind.ProgramTrigger &&
                     prompt.SkillPrompt?.SkillId == "classic:jianxiong"
                        ? prompt.Choices.Single(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "activate")
                        : prompt.Choices.FirstOrDefault(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                          prompt.Choices.First(choice => choice.Cards.Count == 0)).Id,
                    game.Revision))
                : game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Claim scenario could not advance.");
        }
        throw new InvalidOperationException("The fixed current-rule scenario never paused after a fatal damage-card claim.");
    }

    private sealed class ClaimScenario : IGameContentPackage
    {
        public const string ModeId = "identity:classic-group-claim-test";
        public PackageManifest Manifest { get; } = new("group-claim-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new("fixture:group-source", "群体牌来源", "supporter", "standard:none", "wei"));
            var targets = Enumerable.Range(1, 7).Select(index => $"fixture:group-claimant-{index}").ToArray();
            foreach (var id in targets)
                builder.AddGeneral(new(id, "一体力奸雄目标", "supporter", "classic:jianxiong", "wei", BaseHp: 1));
            builder.AddDeck(new("fixture:group-claim-deck", "固定群体牌场景", 0, 1,
                [new ContentDeckCardCount("standard:arrow_barrage", 96)]));
            builder.AddMode(new(ModeId, "群体牌死亡续接", 8, 8,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 4, [nameof(Role.Renegade)] = 1
                }, "fixture:group-claim-deck", GeneralCandidateCount: 8,
                GeneralPoolIds: ["fixture:group-source", .. targets]));
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
