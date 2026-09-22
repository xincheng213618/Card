using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardUseModuleEffectChecks
{
    public static void DefinitionsValidateAndClassifyEffectiveCards()
    {
        Require(CardUseCategoryCatalog.Get(CardKind.Slash) == CardUseCategories.Basic &&
                CardUseCategoryCatalog.Get(CardKind.Duel) == CardUseCategories.InstantTrick &&
                CardUseCategoryCatalog.Get(CardKind.Indulgence) == CardUseCategories.DelayedTrick &&
                CardUseCategoryCatalog.Get(CardKind.Crossbow) == CardUseCategories.Equipment,
            "Card-use categories must be derived from the final effective card kind.");

        Plan(
            new BeginSkillPindian(),
            new GrantNextCardTargetAdjustment(
                CardUseCategories.Basic | CardUseCategories.InstantTrick,
                When: SkillEffectCondition.PindianWon),
            new ForbidCardUseUntilTurnEnd(
                CardUseCategories.InstantTrick,
                SkillEffectCondition.PindianNotWon)).Freeze(SkillId);

        Invalid(new ForbidCardUseUntilTurnEnd(CardUseCategories.None));
        Invalid(new ForbidCardUseUntilTurnEnd((CardUseCategories)16));
        Invalid(new ForbidCardUseUntilTurnEnd(
            CardUseCategories.InstantTrick,
            SkillEffectCondition.PindianNotWon));
        Invalid(new GrantNextCardTargetAdjustment(CardUseCategories.Basic, MinimumTargets: 0));
        Invalid(new GrantNextCardTargetAdjustment(
            CardUseCategories.Basic,
            AllowAdd: false,
            AllowRemove: false));
        Invalid(new ForbidCardUseUntilTurnEnd(
            CardUseCategories.InstantTrick,
            (SkillEffectCondition)99));

        var encoded = JsonSerializer.Serialize<SkillModuleEffect>(
            new ForbidCardUseUntilTurnEnd(
                CardUseCategories.InstantTrick | CardUseCategories.Equipment,
                SkillEffectCondition.PindianNotWon));
        var decoded = JsonSerializer.Deserialize<SkillModuleEffect>(encoded);
        Require(decoded is ForbidCardUseUntilTurnEnd
            {
                Categories: CardUseCategories.InstantTrick | CardUseCategories.Equipment,
                When: SkillEffectCondition.PindianNotWon
            }, "New phase effects must retain their polymorphic JSON identity.");
    }

    public static void TurnStateUsesActionSemanticsStableOrderAndExpiration()
    {
        var state = new TurnCardUseEffectStore();
        var firstSource = new CardUseEffectSource(
            SkillId,
            "phase-module:PlayStarting",
            0,
            $"seat-0:{SkillId}");
        var secondSource = new CardUseEffectSource(
            "fixture:second-source",
            "phase-module:PlayStarting",
            0,
            "seat-0:fixture:second-source");

        var firstAdjustment = state.GrantTargetAdjustment(
            3,
            0,
            101,
            1,
            firstSource,
            new GrantNextCardTargetAdjustment(
                CardUseCategories.Basic | CardUseCategories.InstantTrick));
        var duplicate = state.GrantTargetAdjustment(
            3,
            0,
            101,
            1,
            firstSource,
            new GrantNextCardTargetAdjustment(
                CardUseCategories.Basic | CardUseCategories.InstantTrick));
        var secondAdjustment = state.GrantTargetAdjustment(
            3,
            0,
            102,
            0,
            secondSource,
            new GrantNextCardTargetAdjustment(CardUseCategories.Basic));
        state.GrantProhibition(
            3,
            0,
            101,
            2,
            firstSource,
            new ForbidCardUseUntilTurnEnd(
                CardUseCategories.InstantTrick | CardUseCategories.Equipment));
        state.GrantProhibition(
            3,
            0,
            102,
            1,
            secondSource,
            new ForbidCardUseUntilTurnEnd(CardUseCategories.Basic));
        var damageModifier = state.GrantDamageModifier(
            3,
            0,
            104,
            0,
            firstSource,
            [CardKind.Duel, CardKind.Slash],
            1);
        var duplicateDamageModifier = state.GrantDamageModifier(
            3,
            0,
            104,
            0,
            firstSource,
            [CardKind.Slash, CardKind.Duel],
            1);

        Require(firstAdjustment == duplicate && state.TargetAdjustments.Count == 2,
            "The same frame/effect grant must be idempotent.");
        Require(damageModifier == duplicateDamageModifier && state.DamageModifiers.Count == 1 &&
                state.GetDamageModifiers(3, 0, 0, 0, CardKind.Slash, isChainPropagation: false)
                    .SequenceEqual([damageModifier]) &&
                state.GetDamageModifiers(3, 0, 1, 0, CardKind.Duel, isChainPropagation: false).Count == 0 &&
                state.GetDamageModifiers(3, 0, 0, 0, CardKind.FireSlash, isChainPropagation: false).Count == 0 &&
                state.GetDamageModifiers(3, 0, 0, 0, CardKind.Slash, isChainPropagation: true).Count == 0,
            "Card-damage modifiers must be idempotent and match only direct owner-used configured cards.");
        Require(firstAdjustment.GrantSequence < secondAdjustment.GrantSequence &&
                state.GetTargetAdjustments(3, 0, 0, CardKind.Slash)
                    .Select(item => item.GrantSequence)
                    .SequenceEqual([firstAdjustment.GrantSequence, secondAdjustment.GrantSequence]),
            "Matching target adjustments must retain stable grant order.");
        Require(state.IsCardUseForbidden(3, 0, 0, CardKind.FireAttack, CardActionType.Use) &&
                state.IsCardUseForbidden(3, 0, 0, CardKind.Nullification, CardActionType.Use) &&
                state.IsCardUseForbidden(3, 0, 0, CardKind.Crossbow, CardActionType.Use) &&
                state.IsCardUseForbidden(3, 0, 0, CardKind.Slash, CardActionType.Use),
            "Independent prohibitions must union their final effective categories.");
        Require(!state.IsCardUseForbidden(3, 0, 0, CardKind.Nullification, CardActionType.Response) &&
                !state.IsCardUseForbidden(3, 0, 1, CardKind.Nullification, CardActionType.Use),
            "A response and another actor must not inherit the owner's use prohibition.");
        Require(state.ConsumeTargetAdjustment(firstAdjustment.GrantSequence) &&
                !state.ConsumeTargetAdjustment(firstAdjustment.GrantSequence),
            "A target adjustment must be consumable exactly once.");

        var later = state.GrantProhibition(
            4,
            1,
            103,
            0,
            new CardUseEffectSource("fixture:later", "phase-module:PlayStarting", 1, "seat-1:fixture:later"),
            new ForbidCardUseUntilTurnEnd(CardUseCategories.DelayedTrick));
        var expired = state.ExpireTurn(3, 0);
        Require(expired.SequenceEqual(expired.Order()) &&
                state.Prohibitions.Count == 1 && state.Prohibitions[0] == later &&
                !state.TargetAdjustments.Any() && !state.DamageModifiers.Any(),
            "True turn end must expire exactly that turn's grants without touching a later turn.");
        state.AssertInvariants();
    }

    public static void PindianConditionFiltersNativeAndConvertedUsesAndReplays()
    {
        var registry = Registry();
        var game = Create(registry);
        var activation = Prompt(game, SkillId);
        Accept(game.Submit(new AnswerPromptCommand(
            0,
            activation.PromptId,
            activation.Choices[0].Id,
            game.Revision)));

        var sourceHand = game.CreateSnapshot(0, revealAll: true).Players[0].Hand;
        Require(sourceHand.Any(card => card.Kind == CardKind.DrawTwo),
            $"The fixed initial hand is wrong: {string.Join(',', sourceHand.Select(card => $"{card.Id}:{card.Kind}"))}; " +
            $"moves={string.Join(',', game.CardMovements.Where(move => move.To == CardLocation.Hand(0)).Select(move => $"{move.CardId}:{move.CardKind}"))}.");
        var crossbow = sourceHand.FirstOrDefault(card => card.Kind == CardKind.Crossbow) ??
            throw new InvalidOperationException("The source hand did not retain a Crossbow fixture card.");
        var sourceChoice = game.PendingDecision!.Choices.SingleOrDefault(choice =>
            choice.Cards.SequenceEqual([crossbow.Id]) && choice.Targets.SequenceEqual([1])) ??
            throw new InvalidOperationException("The shared Pindian prompt did not publish the selected source/target pair.");
        Accept(game.Submit(new AnswerPromptCommand(
            0,
            game.PendingDecision.PromptId,
            sourceChoice.Id,
            game.Revision)));
        AdvanceUntilHumanPlay(game);

        var resultEvent = game.Events.Select(item => item.Payload)
            .OfType<PindianResultDeterminedEvent>().SingleOrDefault() ??
            throw new InvalidOperationException("The fixed contest did not publish one Pindian result.");
        var result = resultEvent.Result;
        Require(result.SourceRank == result.OpponentRank && !result.SourceWon,
            "The fixed same-rank contest must take the not-won branch, including ties.");
        var prohibitionEvent = game.Events.Select(item => item.Payload)
            .OfType<CardUseProhibitionGrantedEvent>().SingleOrDefault() ??
            throw new InvalidOperationException("The not-won condition did not grant one card-use prohibition.");
        var prohibition = prohibitionEvent.Prohibition;
        Require(prohibition.Source == new CardUseEffectSource(
                    SkillId,
                    "phase-module:PlayStarting",
                    0,
                    $"seat-0:{SkillId}") &&
                prohibition is { TurnNumber: 1, TurnSeat: 0, Categories: CardUseCategories.InstantTrick },
            "The granted restriction must retain its stable content source and turn identity.");
        Require(!game.Events.Select(item => item.Payload).OfType<CardTargetAdjustmentGrantedEvent>().Any(),
            "The won-only adjustment must not be granted on a tie.");

        var hand = game.CreateSnapshot(0, revealAll: true).Players[0].Hand;
        var drawTwo = hand.FirstOrDefault(card => card.Kind == CardKind.DrawTwo) ??
            throw new InvalidOperationException(
                $"The source did not retain the native DrawTwo fixture card; hand={string.Join(',', hand.Select(card => card.Kind))}.");
        var slash = hand.FirstOrDefault(card => card.Kind == CardKind.Slash) ??
            throw new InvalidOperationException("The source did not retain the red Slash conversion fixture card.");
        var actions = game.GetHumanLegalActions();
        Require(actions.Any(action => action.Kind == LegalActionKind.Slash && action.CardId == slash.Id),
            "The unrelated Basic card must remain usable.");
        Require(actions.All(action => action.CardId != drawTwo.Id) &&
                actions.All(action => action.PlayedCardKind != CardKind.FireAttack),
            "Both a native trick and a red Basic card converted to Fire Attack must be filtered by effective kind.");

        var revision = game.Revision;
        var forged = game.Submit(new PlayCardCommand(
            0,
            drawTwo.Id,
            [],
            revision,
            game.PendingDecision!.PromptId));
        Require(!forged.Accepted && game.Revision == revision,
            "A formally submitted prohibited trick must be rejected without side effects.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) &&
                Events(game).SequenceEqual(Events(restored)) &&
                Actions(game).SequenceEqual(Actions(restored)),
            "Replay must rebuild the same restriction, private hand and legal action set.");

        Accept(game.Submit(new EndPlayPhaseCommand(
            0,
            game.Revision,
            game.PendingDecision!.PromptId)));
        for (var step = 0; step < 8 &&
             !game.Events.Select(item => item.Payload).OfType<TurnCardUseEffectsExpiredEvent>().Any(); step++)
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        var expiration = game.Events.Select(item => item.Payload)
            .OfType<TurnCardUseEffectsExpiredEvent>().SingleOrDefault() ??
            throw new InvalidOperationException("The granting turn did not publish one effect-expiration event.");
        Require(expiration is { TurnNumber: 1, TurnSeat: 0 } &&
                expiration.GrantSequences.SequenceEqual([prohibition.GrantSequence]),
            "The active prohibition must expire at the actual end of its granting turn.");
    }

    private const string SkillId = "fixture:card-use-module";
    private const string ModeId = "identity:card-use-module-fixture";

    private static SkillActivationPlan Plan(params SkillModuleEffect[] effects) => new(
        new(SkillId, "Fixture", "Card-use effect", "Use or skip"),
        "Card-use effect",
        new(new("fixture.use"), "Use", [], [], new Dictionary<string, string>()),
        new(new("fixture.skip"), "Skip", [], [], new Dictionary<string, string>()),
        effects);

    private static void Invalid(SkillModuleEffect effect)
    {
        try { Plan(effect).Freeze(SkillId); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException($"Invalid effect {effect.GetType().Name} was accepted.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new FixturePackage());

    private static GameEngine Create(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 670022,
            PlayerCount = 6,
            ModeId = ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var selection = game.PendingDecision!;
        Accept(game.Submit(new SelectGeneralCommand(
            0,
            selection.ValidContentIds[0],
            game.Revision,
            selection.PromptId)));
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision?.SkillPrompt?.SkillId == SkillId) return game;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixture never reached its PlayStarting module.");
    }

    private static void AdvanceUntilHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixture never resumed the human Play phase.");
    }

    private static PendingDecision Prompt(GameEngine game, string skillId) =>
        game.PendingDecision is { SkillPrompt: { } presentation } prompt && presentation.SkillId == skillId
            ? prompt
            : throw new InvalidOperationException($"Expected module prompt {skillId}.");

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();

    private static string[] Actions(GameEngine game) => game.GetHumanLegalActions()
        .Select(action => JsonSerializer.Serialize(action)).ToArray();

    private static void Accept(CommandResult result) =>
        Require(result.Accepted, result.Error?.Message ?? "Command rejected.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Module : IPhaseSkillModule
    {
        public string SkillId => CardUseModuleEffectChecks.SkillId;
        public int Revision => 1;
        public PhaseSkillWindow Window => PhaseSkillWindow.PlayStarting;
        public SkillActivationPlan CreatePlan(PhaseSkillContext context) => Plan(
            new BeginSkillPindian(),
            new GrantNextCardTargetAdjustment(
                CardUseCategories.Basic | CardUseCategories.InstantTrick,
                When: SkillEffectCondition.PindianWon),
            new ForbidCardUseUntilTurnEnd(
                CardUseCategories.InstantTrick,
                SkillEffectCondition.PindianNotWon));
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "fixture-card-use-module",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new(SkillId, "Fixture", "Card-use effect") { PhaseSkill = new Module() });
            var ids = Enumerable.Range(0, 6).Select(index => $"fixture:card-use-general-{index}").ToArray();
            foreach (var id in ids)
            {
                builder.AddGeneral(new(
                    id,
                    "Card Use Fixture",
                    "supporter",
                    SkillId,
                    "shu",
                    AdditionalSkillIds: ["classic:huoji"]));
            }

            var kinds = new[]
            {
                "standard:draw_two",
                "standard:slash",
                "standard:nullification",
                "standard:crossbow"
            };
            builder.AddDeck(new("fixture:card-use-deck", "Card-use fixture deck", 12, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 4)
                    .SelectMany(_ => kinds)
                    .SelectMany(kind => Enumerable.Range(0, 6)
                        .Select(_ => new ContentDeckPhysicalCard(kind, Suit.Heart, 5)))
                    .ToArray()
            });
            builder.AddMode(new(
                ModeId,
                "Card-use module fixture",
                6,
                6,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3,
                    [nameof(Role.Renegade)] = 1
                },
                "fixture:card-use-deck",
                GeneralCandidateCount: 6,
                GeneralPoolIds: ids));
        }
    }
}
