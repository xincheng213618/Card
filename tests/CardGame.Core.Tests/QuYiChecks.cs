using CardGame.Content.Standard;
using CardGame.Core;

internal static class QuYiChecks
{
    private const string General = "classic:qu-yi";
    private const string DamageGeneral = "fixture:qu-yi-damage";

    public static void NearbySlashCannotRespondBeforeDamageBonus()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 120; seed++)
        {
            var game = Start(registry, seed, General, "fixture:qu-yi-mixed");
            ReachPlay(game);
            var target = game.CreateSnapshot(0, true).Players[1];
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
            if (slash is null || !target.Hand.Any(card => card.Kind == CardKind.Dodge)) continue;
            Play(game, slash);
            FinishResolution(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(!events.OfType<ResponseRequestedEvent>().Any(item =>
                    item.TargetSeat == 1 && item.IncomingCard is CardKind.Slash or
                        CardKind.FireSlash or CardKind.ThunderSlash),
                "Fuqi must remove the nearby target's Dodge response before damage.");
            Require(events.OfType<DamageRequestedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 2) &&
                    events.OfType<ProgramCardDamageModifiedEvent>().Any(item =>
                        item.Source.SkillId == "classic:jiaozi" && item.ModifiedAmount == 2),
                "Jiaozi must increase damage once when Qu Yi alone has the most hand cards.");
            return;
        }
        throw new InvalidOperationException("No nearby Slash fixture with a Dodge was found.");
    }

    public static void GlobalTrickResponseUsesEachTargetsDistance()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 120; seed++)
        {
            var game = Start(registry, seed, General, "fixture:qu-yi-assault");
            ReachPlay(game);
            var snapshot = game.CreateSnapshot(0, true);
            var assault = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.BarbarianAssault);
            if (assault is null || !snapshot.Players[1].Hand.Any(card => card.Kind == CardKind.Slash) ||
                !snapshot.Players[2].Hand.Any(card => card.Kind == CardKind.Slash)) continue;
            Play(game, assault);
            FinishResolution(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(!events.OfType<ResponseRequestedEvent>().Any(item =>
                    item.IncomingCard == CardKind.BarbarianAssault && item.TargetSeat == 1) &&
                    events.OfType<ResponseRequestedEvent>().Any(item =>
                        item.IncomingCard == CardKind.BarbarianAssault && item.TargetSeat == 2),
                "Fuqi must block a nearby Assault response while still offering a distant target's response.");
            Require(events.OfType<DamageRequestedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 2),
                "Jiaozi must apply to Barbarian Assault damage after Fuqi's response check.");
            return;
        }
        throw new InvalidOperationException("No global trick fixture with both response ranges was found.");
    }

    public static void ArrowBarrageResponseUsesEachTargetsDistance()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 120; seed++)
        {
            var game = Start(registry, seed, General, "fixture:qu-yi-barrage");
            ReachPlay(game);
            var snapshot = game.CreateSnapshot(0, true);
            var barrage = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.ArrowBarrage);
            if (barrage is null || !snapshot.Players[1].Hand.Any(card => card.Kind == CardKind.Dodge) ||
                !snapshot.Players[2].Hand.Any(card => card.Kind == CardKind.Dodge)) continue;
            Play(game, barrage);
            FinishResolution(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(!events.OfType<ResponseRequestedEvent>().Any(item =>
                    item.IncomingCard == CardKind.ArrowBarrage && item.TargetSeat == 1) &&
                    events.OfType<ResponseRequestedEvent>().Any(item =>
                        item.IncomingCard == CardKind.ArrowBarrage && item.TargetSeat == 2),
                "Fuqi must block a nearby Barrage response while offering a distant target's response.");
            Require(events.OfType<DamageRequestedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 2),
                "Jiaozi must apply to Arrow Barrage damage after Fuqi's response check.");
            return;
        }
        throw new InvalidOperationException("No Arrow Barrage fixture with both response ranges was found.");
    }

    public static void NearbyDuelCannotRequestSlash()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 120; seed++)
        {
            var game = Start(registry, seed, General, "fixture:qu-yi-duel");
            ReachPlay(game);
            var target = game.CreateSnapshot(0, true).Players[1];
            var duel = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Duel && action.TargetSeat == 1);
            if (duel is null || !target.Hand.Any(card => card.Kind == CardKind.Slash)) continue;
            Play(game, duel);
            FinishResolution(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(!events.OfType<ResponseRequestedEvent>().Any(item =>
                    item.IncomingCard == CardKind.Duel && item.TargetSeat == 1) &&
                    events.OfType<DuelResponseEvent>().Any(item =>
                        item.ResponderSeat == 1 && !item.UsedSlash) &&
                    events.OfType<DamageRequestedEvent>().Any(item =>
                        item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 2),
                "Fuqi must settle whether Duel can be answered before Jiaozi modifies its damage.");
            return;
        }
        throw new InvalidOperationException("No nearby Duel fixture with a Slash was found.");
    }

    public static void CardlessDamageUsesTheSameDamageModifier()
    {
        var registry = Registry();
        var game = Start(registry, 1, DamageGeneral, "fixture:qu-yi-mixed");
        ReachPlay(game);
        var result = game.Submit(new UseProgramSkillCommand(0, "fixture:damage-driver", "hit",
            [], [1], game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "The cardless damage skill failed.");
        FinishResolution(game);
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(events.OfType<DamageRequestedEvent>().Any(item =>
                item.SourceSeat == 0 && item.TargetSeat == 1 && item.SourceCard is null && item.Amount == 2) &&
                events.OfType<ProgramCardDamageModifiedEvent>().Any(item =>
                    item.Source.SkillId == "classic:jiaozi" && item.CardKind is null &&
                    item.ModifiedAmount == 2),
            "Jiaozi must modify damage with no source card through the shared damage pipeline.");
    }

    public static void OriginalTrickNullificationSkipsNearbyTarget()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 120; seed++)
        {
            var game = Start(registry, seed, General, "fixture:qu-yi-nullification");
            ReachPlay(game);
            var snapshot = game.CreateSnapshot(0, true);
            var dismantlement = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Dismantlement && action.TargetSeat == 1);
            if (dismantlement is null ||
                !snapshot.Players[1].Hand.Any(card => card.Kind == CardKind.Nullification) ||
                !snapshot.Players[2].Hand.Any(card => card.Kind == CardKind.Nullification)) continue;
            Play(game, dismantlement);
            for (var step = 0; step < 30 && !game.Events.Select(item => item.Payload)
                     .OfType<NullificationRequestedEvent>().Any(item =>
                         item.ResponderSeat == 2 && item.ChainDepth == 0); step++)
                Advance(game);
            var requests = game.Events.Select(item => item.Payload)
                .OfType<NullificationRequestedEvent>()
                .Where(item => item.ChainDepth == 0 && item.EffectCardKind == CardKind.Dismantlement)
                .ToArray();
            Require(requests.Any(item => item.ResponderSeat == 2) &&
                    requests.All(item => item.ResponderSeat != 1),
                "A nearby trick target cannot nullify the original trick, but a distant character can.");
            return;
        }
        throw new InvalidOperationException("No Dismantlement fixture with near and distant Nullification was found.");
    }

    public static void TiedHandCountDoesNotIncreaseCardlessDamage()
    {
        var registry = Registry();
        var game = Start(registry, 1, DamageGeneral, "fixture:qu-yi-mixed");
        ReachPlay(game);
        var cards = game.CreateSnapshot(0, true).Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
        Require(cards.Length == 2, "The damage-cost fixture needs two hand cards.");
        var result = game.Submit(new UseProgramSkillCommand(0, "fixture:damage-driver",
            "hit-after-discard", cards, [1], game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "The paid damage skill failed.");
        FinishResolution(game);
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(events.OfType<DamageRequestedEvent>().Any(item =>
                item.SourceSeat == 0 && item.TargetSeat == 1 && item.SourceCard is null && item.Amount == 1) &&
                !events.OfType<ProgramCardDamageModifiedEvent>().Any(item =>
                    item.Source.SkillId == "classic:jiaozi"),
            "Jiaozi must check current hand counts after the cost and reject a tie.");
    }

    public static void IncomingDamageChecksTheTargetOwner()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 80; seed++)
        {
            var game = Start(registry, seed, DamageGeneral, "fixture:qu-yi-incoming");
            ReachPlay(game);
            var owner = game.CreateSnapshot(0, true).Players.FirstOrDefault(player =>
                player.GeneralId == General);
            if (owner is null) continue;
            var cards = game.CreateSnapshot(0, true).Players[0].Hand.Take(2)
                .Select(card => card.Id).ToArray();
            var used = game.Submit(new UseProgramSkillCommand(0, "fixture:damage-driver",
                "feed-and-hit", cards, [owner.Seat], game.Revision, game.PendingDecision!.PromptId));
            Require(used.Accepted, used.Error?.Message ?? "The incoming damage fixture failed.");
            FinishResolution(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(events.OfType<DamageRequestedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == owner.Seat &&
                    item.SourceCard is null && item.Amount == 2) &&
                    events.OfType<ProgramCardDamageModifiedEvent>().Any(item =>
                        item.Source.SkillId == "classic:jiaozi" &&
                        item.Source.OwnerSeat == owner.Seat && item.ModifiedAmount == 2),
                "Jiaozi must increase incoming cardless damage when its owner alone has the most cards.");
            return;
        }
        throw new InvalidOperationException("No incoming damage fixture selected Qu Yi as an opponent.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId, string modeId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Qu Yi fixture did not start.");
        var prompt = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Qu Yi selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Qu Yi fixture did not reach Play.");
    }

    private static void FinishResolution(GameEngine game)
    {
        for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
            Advance(game);
        Require(game.ResolutionStack.Count == 0, "Qu Yi card or damage resolution remained active.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Qu Yi card use failed.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Qu Yi fixture could not advance.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("qu-yi-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 148, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            const string rules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:damage-driver","revision":1,
                "minimumRulesVersion":178,"activations":[{"id":"hit","usesPerTurn":1,
                "minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"otherLiving","effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"hit-after-discard","usesPerTurn":1,
                "minCards":2,"maxCards":2,"sourceZones":["hand"],
                "minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "effects":[{"op":"discardSelected","target":"owner","amount":2},
                {"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"feed-and-hit","usesPerTurn":1,
                "minCards":2,"maxCards":2,"sourceZones":["hand"],
                "minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "effects":[{"op":"giveSelected","target":"selectedTarget"},
                {"op":"damage","target":"selectedTarget","amount":1}]}]}]}
                """;
            const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:damage-driver":{"name":"伤害驱动","description":"测试"}}}
                """;
            var driver = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new ContentSkillDefinition("fixture:damage-driver", "伤害驱动", "测试")
            { Program = driver.Programs["fixture:damage-driver"] });
            builder.AddGeneral(new ContentGeneralDefinition(DamageGeneral, "麹义伤害测试", "qu_yi",
                "classic:fuqi", "qun", BaseHp: 4,
                AdditionalSkillIds: ["classic:jiaozi", "fixture:damage-driver"]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:qu-yi-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 8));
            var mixed = new[] { "standard:slash", "standard:slash", "standard:dodge",
                "standard:barbarian_assault", "standard:slash", "standard:dodge",
                "standard:draw_two", "standard:nullification" };
            var assault = new[] { "standard:slash", "standard:slash", "standard:slash",
                "standard:barbarian_assault" };
            var barrage = new[] { "standard:slash", "standard:dodge", "standard:dodge",
                "standard:arrow_barrage" };
            var duel = new[] { "standard:slash", "standard:slash", "standard:slash",
                "standard:duel" };
            var nullification = new[] { "standard:dismantlement", "standard:nullification",
                "standard:slash", "standard:nullification" };
            AddMode("fixture:qu-yi-mixed", mixed);
            AddMode("fixture:qu-yi-assault", assault);
            AddMode("fixture:qu-yi-barrage", barrage);
            AddMode("fixture:qu-yi-duel", duel);
            AddMode("fixture:qu-yi-nullification", nullification);
            AddMode("fixture:qu-yi-incoming", mixed);

            void AddMode(string id, string[] kinds)
            {
                var deckId = id + "-deck";
                builder.AddDeck(new ContentDeckRecipe(deckId, "麹义测试牌堆", 4, 2, [])
                {
                    PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                        new ContentDeckPhysicalCard(kinds[index % kinds.Length],
                            (Suit)(index % 4), index % 13 + 1)).ToArray()
                });
                builder.AddMode(new ContentModeDefinition(id, "麹义测试", 5, 5,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                    }, deckId, GeneralCandidateCount: id == "fixture:qu-yi-incoming" ? 5 : 6,
                    GeneralPoolIds: id == "fixture:qu-yi-incoming"
                        ? [DamageGeneral, General, .. targets.Skip(1)]
                        : [General, DamageGeneral, .. targets]));
            }
        }
    }
}
