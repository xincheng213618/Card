using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushuangModeChecks
{
    private const string Driver = "fixture:wushuang-driver";
    private const string Human = "fixture:wushuang-human";

    public static void NativeShopGrowthAcquiredSkillsReplacementAndReplay()
    {
        var realRegistry = StandardWushuangContentRegistry.Create();
        var realMode = realRegistry.Modes[WushuangMode.ModeId];
        Require(realMode.MinPlayers == 2 && realMode.MaxPlayers == 2 && realMode.TeamCounts!.Values.All(c => c == 1),
            "The actual opt-in content package registers a genuine two-seat duel.");
        // The reported desktop crash: opening-hand confirmation, a host clock
        // debit, then a paced step that creates the initial shopping frame.
        // Keep the reported host ordering and seed, with the existing bounded
        // native fixture so an expanding general pool cannot change this setup.
        var (reported, reportedRegistry) = Start(clock: false, seed: 956567926, confirmOpeningHand: true);
        Accept(reported, new AdvanceOneStepCommand(reported.Revision));
        Accept(reported, new AdvanceOneStepCommand(reported.Revision));
        var confirm = P(reported)!;
        Accept(reported, new AnswerPromptCommand(0, confirm.PromptId,
            confirm.Choices.Single(c => c.Id.Value == "setup.opening.confirm").Id, reported.Revision));
        Accept(reported, new TickWushuangClockCommand(247, reported.Revision));
        Accept(reported, new AdvanceOneStepCommand(reported.Revision));
        Require(!reported.IsFaulted && Shop(reported) is { CanShop: true, Phase: WushuangShopPhase.OpeningTactic },
            "The actual crash sequence accepts its new intermission frame without faulting the command session.");
        Buy(reported, Shop(reported).Offers[0].Id);
        Play(reported);
        Require(!reported.IsFaulted && !Shop(reported).CanShop && Shop(reported).TacticIds.Count == 1,
            "Choosing a real opening tactic continues to a playable human turn without a frozen or stale shop prompt.");
        var publicTactics = reported.CreateSnapshot(0).Players;
        Require(publicTactics.All(player => player.WushuangTacticIds!.SequenceEqual(reported.CreateWushuangSnapshot(player.Seat)!.TacticIds)) &&
            publicTactics.All(player => player.WushuangTacticIds is IList<int> { IsReadOnly: true }),
            "Both acquired tactic lists are frozen public player state, separate from private shop offers and currency.");
        var nextPage = Shop(reported).Offers.Select(offer => offer.Id).ToArray();
        Require(nextPage.Length == 4 && !reported.Submit(new BuyWushuangOfferCommand(0, nextPage[0], null, reported.Revision)).Accepted,
            "The next page is prepared for in-turn preview, but cannot be purchased during play.");
        Shopping(reported);
        Require(Shop(reported).Offers.Select(offer => offer.Id).SequenceEqual(nextPage),
            "The ending intermission opens the exact page previewed during the preceding turn.");
        Cold(reported, reportedRegistry);
        var (game, registry) = Start(clock: false);
        Drive(game, () => Shop(game).Phase == WushuangShopPhase.OpeningTactic && Shop(game).CanShop);
        Require(Shop(game).Offers.Count == 3 && Shop(game).Offers.All(o => o.Cost == 0 && o.Kind == WushuangOfferKind.Tactic) &&
            !game.Submit(new FinishWushuangShopCommand(0, game.Revision)).Accepted,
            "Opening is a mandatory free three-choice tactic window before ordinary play.");
        Cold(game, registry);
        Buy(game, Shop(game).Offers[0].Id);
        Play(game);
        var initiative = E<TurnStartedEvent>(game).First().ActorSeat;
        Require(game.CardMovements.Count(m => m.To == CardLocation.Hand(initiative) && m.Reason == CardMoveReasons.InitialDeal) == 4 &&
            game.CardMovements.Count(m => m.To == CardLocation.Hand(1 - initiative) && m.Reason == CardMoveReasons.InitialDeal) == 5,
            "The randomly selected first actor receives four opening cards and the second actor receives five.");
        Require(!Shop(game).CanShop && Shop(game).TacticIds.Count == 1 && Shop(game).ExtraSkillIds.Count == 0,
            "The free tactic does not consume a skill slot and ordinary play cannot buy goods.");
        // Pend a genuine extra turn, then observe income and windows around it.
        Accept(game, new UseProgramSkillCommand(0, Driver, "extra", [], [], game.Revision, P(game)!.PromptId));
        Play(game);
        var firstTurn = game.State.TurnNumber;
        End(game);
        Drive(game, () => P(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && game.State.TurnNumber > firstTurn);
        var afterNormal = E<WushuangTokensChangedEvent>(game).Count(e => e.Seat == 0 && e.Reason == "normal-turn-start");
        var shopsBeforeExtra = E<WushuangShopRefreshedEvent>(game).Count(e => e.Seat == 0);
        var extraTurn = game.State.TurnNumber;
        End(game);
        Drive(game, () => game.State.TurnNumber > extraTurn && P(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var next = E<WushuangTokensChangedEvent>(game).Count(e => e.Seat == 0 && e.Reason == "normal-turn-start");
        Require(next == afterNormal + 2 && E<WushuangShopRefreshedEvent>(game).Count(e => e.Seat == 0) == shopsBeforeExtra + 1,
            "The extra turn adds no payday or shop; the AI normal turn shops and the next human normal turn adds income.");
        Shopping(game);
        var first = Shop(game);
        Require(first.Offers.Count == 4 && first.Offers.Count(o => o.Kind == WushuangOfferKind.Skill) == 1 &&
            first.Offers.Count(o => o.Kind == WushuangOfferKind.Card) == 2 && first.Offers.Count(o => o.Kind == WushuangOfferKind.Tactic) == 1 &&
            first.Experience == 0 && first.ExperienceToNextLevel == 0 && first.LevelUpCost == 0 &&
            E<WushuangTokensChangedEvent>(game).Any(e => e.Reason == "round-start" && e.Delta == 1) &&
            !game.Submit(new UpgradeWushuangShopCommand(0, game.Revision)).Accepted,
            "Four goods appear only in the ending intermission; round income is separate, with no XP or shop leveling.");
        var skill = first.Offers.Single(o => o.SkillId is not null);
        Buy(game, skill.Id);
        Require(Shop(game).ExtraSkillIds.Contains(skill.SkillId!) && Shop(game).PurchasedOffers.Single().Id == skill.Id &&
            game.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == skill.SkillId),
            "A purchase grants a real skill and retains its sold slot for display.");
        var card = Shop(game).Offers.First(o => o.CardKind is not null);
        var hand = game.CreateSnapshot(0).Players[0].HandCount;
        Buy(game, card.Id);
        Require(game.CreateSnapshot(0).Players[0].HandCount == hand + 1, "A card offer places an actual owned physical card.");
        Accept(game, new ToggleWushuangShopLockCommand(0, game.Revision));
        var lockedIds = Shop(game).Offers.Select(o => o.Id).ToArray();
        Cold(game, registry);
        Accept(game, new FinishWushuangShopCommand(0, game.Revision));
        Shopping(game);
        Require(Shop(game).IsLocked && Shop(game).Offers.Select(o => o.Id).SequenceEqual(lockedIds) && Shop(game).PurchasedOffers.Count == 2,
            "Locking retains the entire page, including sold positions, across the next automatic refresh.");
        var staleOffer = Shop(game).Offers.First().Id;
        var tokens = Shop(game).Tokens;
        Accept(game, new RerollWushuangShopCommand(0, game.Revision));
        Require(Shop(game).CanShop && P(game)?.Kind == DecisionKind.ProgramTrigger && Shop(game).Tokens == tokens - 1 &&
            Shop(game).PurchasedOffers.Count == 0 && Shop(game).IsLocked &&
            !game.Submit(new BuyWushuangOfferCommand(0, staleOffer, null, game.Revision)).Accepted,
            "Manual refresh costs exactly one token, resets sold positions and retires the old page, while preserving the lock.");
        Buy(game, Shop(game).Offers.Single(o => o.SkillId is not null).Id);
        Accept(game, new FinishWushuangShopCommand(0, game.Revision));
        Shopping(game);
        Accept(game, new RerollWushuangShopCommand(0, game.Revision));
        var third = Shop(game).Offers.First(o => o.SkillId is not null && o.Cost <= Shop(game).Tokens);
        var before = Views(game);
        Require(!game.Submit(new BuyWushuangOfferCommand(0, third.Id, null, game.Revision)).Accepted && Views(game) == before &&
            !game.Submit(new BuyWushuangOfferCommand(0, third.Id, Driver, game.Revision)).Accepted && Views(game) == before,
            "A full shop skill rack requires an explicit owned extra skill; a printed skill cannot be replaced.");
        var removed = Shop(game).ExtraSkillIds[0];
        Buy(game, third.Id, removed);
        Require(Shop(game).ExtraSkillIds.Count == 2 && Shop(game).ExtraSkillIds.Contains(third.SkillId!) &&
            !Shop(game).ExtraSkillIds.Contains(removed) && game.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == Driver) &&
            game.CreateSnapshot(0).Players[0].Skills!.All(s => s.ContentId != removed),
            "An explicit replacement removes exactly one shop grant while preserving all printed skills.");
        Cold(game, registry);
    }

    public static void ExplicitClockExpiresPaidChildAndColdReplay()
    {
        var (game, registry) = Start(clock: true);
        Play(game);
        Require(game.IsWushuangClockRunning && game.WushuangRemainingMilliseconds == WushuangMode.TimeLimitMilliseconds,
            "Selection and native commands never infer wall-clock elapsed time.");
        var before = Views(game);
        Require(!game.Submit(new TickWushuangClockCommand(0, game.Revision)).Accepted && Views(game) == before &&
            !game.Submit(new TickWushuangClockCommand(WushuangMode.TimeLimitMilliseconds + 1, game.Revision)).Accepted && Views(game) == before,
            "Invalid clock data rejects without entering the command journal.");
        Require(!game.Submit(new TickWushuangClockCommand(1, game.Revision) { ActorSeat = 0 }).Accepted && Views(game) == before,
            "A player cannot impersonate the trusted clock host through inherited command metadata.");
        Accept(game, new TickWushuangClockCommand(WushuangMode.TimeLimitMilliseconds - 1, game.Revision));
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, P(game)!.PromptId, slash.PlayedCardKind));
        Drive(game, () => P(game)?.SkillPrompt?.SkillId == Driver &&
            game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any());
        var paid = slash.CardId.Value;
        Require(game.CreateCardZoneDiagnostics().Single(c => c.CardId == paid).Location == CardLocation.Processing,
            "The deadline fixture reaches a real paid Slash suspended at its optional after-damage child.");
        Cold(game, registry);
        Accept(game, new TickWushuangClockCommand(1, game.Revision));
        Require(game.State.Status == EngineStatus.Completed && game.WushuangRemainingMilliseconds == 0 &&
            game.PendingDecision is null && game.ResolutionStack.Count == 0 &&
            game.CreateCardZoneDiagnostics().Single(c => c.CardId == paid).Location == CardLocation.DiscardPile &&
            E<WushuangClockExpiredEvent>(game).Count() == 1 &&
            game.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "The explicit deadline retires the exact pending subtree and already-paid material once, then records deterministic scoring.");
        Require(!game.Submit(new TickWushuangClockCommand(1, game.Revision)).Accepted,
            "A finished challenge cannot be reopened with another clock tick.");
        Cold(game, registry);
    }

    private static (GameEngine, ContentRegistry) Start(bool clock, int seed = 17, bool confirmOpeningHand = false)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new Fixture(clock)]);
        var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(seed) with
        { ConfirmOpeningHand = confirmOpeningHand, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false }, registry);
        Require(!game.IsWushuangClockRunning && !game.Submit(new TickWushuangClockCommand(1, game.Revision)).Accepted,
            "A started, selected game is required before the trusted host clock can advance.");
        Accept(game, new StartGameCommand());
        Drive(game, () => P(game)?.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Human, game.Revision, P(game)!.PromptId));
        return (game, registry);
    }

    private static WushuangSnapshot Shop(GameEngine g) => g.CreateSnapshot(0).Wushuang!;
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Buy(GameEngine g, string id, string? replacement = null)
    {
        Accept(g, new BuyWushuangOfferCommand(0, id, replacement, g.Revision));
    }
    private static void Shopping(GameEngine g) => Drive(g, () => Shop(g) is { Phase: WushuangShopPhase.Shopping, CanShop: true });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextHumanTurn(GameEngine g)
    {
        var oldTurn = g.State.TurnNumber;
        End(g);
        Drive(g, () => g.State.TurnNumber > oldTurn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    }
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            if (P(g) is { Kind: DecisionKind.PlayCard }) End(g);
            else if (P(g) is { Kind: DecisionKind.DiscardCards } discard)
                Accept(g, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, g.Revision));
            else if (P(g) is { } prompt && prompt.Kind != DecisionKind.SelectGeneral)
            {
                var choice = prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") == "pass") ?? prompt.Choices.First();
                Accept(g, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Bounded Wushuang fixture missed its native boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static string Views(GameEngine g) => JsonSerializer.Serialize(Enumerable.Range(0, 2)
        .Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray());
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Views(g) == Views(restored) && JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Native commands replay every player view, private shop revision, currency, granted skills, exact card movement and explicit remaining time.");
    }
    private static void Accept(GameEngine g, GameCommand c)
    { var r = g.Submit(c); Require(r.Accepted, r.Error?.Message ?? "Wushuang command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool clock) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("wushuang-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,
               "activations":[{"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}],
               "triggers":[{"id":"paid-child","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "无双小型验证", description = "原生额外回合与付费后窗口。" } } }));
            builder.AddSkill(new(Driver, "无双小型验证", "原生额外回合与付费后窗口。")
            { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            builder.AddGeneral(new(Human, "无双主将", "", Driver, "qun", BaseHp: 30,
                AdditionalSkillIds: ["classic:qingguo", "classic:lianying"]));
            builder.AddGeneral(new("fixture:wushuang-ai", "无双对手", "", "classic:qianxun", "qun", BaseHp: 30));
            builder.AddDeck(new("fixture:wushuang-deck", "Bounded native shop material", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 240).Select(_ => new ContentDeckPhysicalCard(clock ? "standard:slash" : "standard:dodge", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(WushuangMode.ModeId, "无双小型验证", 2, 2, new Dictionary<string, int>(),
                "fixture:wushuang-deck", 2, [Human, "fixture:wushuang-ai"], ContentModeKind.Team,
                new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 }));
        }
    }
}
