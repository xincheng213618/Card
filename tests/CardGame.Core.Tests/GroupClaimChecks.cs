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
        var events = game.Events.Select(e => e.Payload).ToArray();
        var claimIndex = Array.FindIndex(events, e => e is ProgramDamageCardsClaimedEvent);
        Require(claimIndex >= 0, "A living two-HP target must legally claim the damage card.");
        var claim = (ProgramDamageCardsClaimedEvent)events[claimIndex];
        var use = events.OfType<CardUseDeclaredEvent>().Single();
        var lossIndex = Array.FindIndex(events, claimIndex + 1, e =>
            e is ProgramSkillHpLostEvent lost && lost.TargetSeat == claim.OwnerSeat &&
            lost.RemainingHp == 0);
        var dyingIndex = Array.FindIndex(events, lossIndex + 1, e =>
            e is PlayerDyingEvent dying && dying.VictimSeat == claim.OwnerSeat);
        var deathIndex = Array.FindIndex(events, claimIndex + 1, e =>
            e is PlayerDiedEvent died && died.VictimSeat == claim.OwnerSeat);
        var nextResponseIndex = Array.FindIndex(events, deathIndex + 1, e =>
            e is GroupResponseEvent response && response.ResolutionId == use.ResolutionId &&
            response.ResponderSeat != claim.OwnerSeat);
        var finishIndex = Array.FindIndex(events, e =>
            e is CardUseFinishedEvent finished && finished.ResolutionId == use.ResolutionId);
        Require(claim.CardIds.Contains(use.CardId) && use.CardKind == CardKind.ArrowBarrage &&
                lossIndex > claimIndex && dyingIndex > lossIndex && deathIndex > dyingIndex &&
                nextResponseIndex > deathIndex && finishIndex > nextResponseIndex,
            "The claimant must claim while alive, lose HP and die, then let later group targets respond before card finish.");
        Require(events.Skip(claimIndex + 1).Take(deathIndex - claimIndex)
                    .OfType<CardMovedEvent>().Any(e => e.CardId == use.CardId &&
                        e.From == CardLocation.Hand(claim.OwnerSeat) && e.To == CardLocation.DiscardPile),
            "Death must clean up the legally claimed physical card.");
        Require(events.OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.ResolutionId) == 1 &&
            game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0,
            "Group resolution must finish exactly once with no processing card.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) == SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            "Claim, death and group continuation must replay deterministically.");
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
            if (game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Any()) return game;
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
        throw new InvalidOperationException("The current-rule group card never finished.");
    }

    private sealed class ClaimScenario : IGameContentPackage
    {
        public const string ModeId = "identity:classic-group-claim-test";
        private const string FatalAfterDamageId = "fixture:claimant-after-damage-loss";
        public PackageManifest Manifest { get; } = new("group-claim-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load("""
                {"schemaVersion":59,"skills":[{"id":"fixture:claimant-after-damage-loss",
                "revision":1,"minimumRulesVersion":169,"modifiers":[],"viewAs":[],"activations":[],
                "triggers":[{"id":"lose-after-claim","window":"afterDamageApplied","subject":"owner",
                "damageOccurrence":"perDamage","optional":false,"priority":-1,
                "effects":[{"op":"loseHp","target":"owner","amount":1}]}],
                "contributions":[],"cardIdentities":[]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:claimant-after-damage-loss":
                {"name":"受伤后失去体力","description":"测试伤害后领取牌与后续失去体力的顺序。"}}}
                """);
            var program = catalog.Programs[FatalAfterDamageId];
            builder.AddSkill(new ContentSkillDefinition(FatalAfterDamageId,
                "受伤后失去体力", "测试伤害后领取牌与后续失去体力的顺序。")
            {
                Program = program,
                ExecutionForms = SkillExecutionForm.Trigger
            });
            builder.AddGeneral(new("fixture:group-source", "群体牌来源", "supporter", "standard:none", "wei"));
            var targets = Enumerable.Range(1, 7).Select(index => $"fixture:group-claimant-{index}").ToArray();
            foreach (var id in targets)
                builder.AddGeneral(new(id, "二体力奸雄目标", "supporter", "classic:jianxiong", "wei",
                    BaseHp: 2, AdditionalSkillIds: [FatalAfterDamageId]));
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
