using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JiaXuChecks
{
    private const string General = "classic:jia-xu";
    private const string Wansha = "classic:wansha";
    private const string Luanwu = "classic:luanwu";
    private const string Weimu = "classic:weimu";
    private const string Activation = "force-nearest-slash";
    private const string SlashMode = "identity:classic-jia-xu-luanwu-slash-check";
    private const string DodgeMode = "identity:classic-jia-xu-luanwu-dodge-check";
    private const string SpadeAssaultMode = "identity:classic-jia-xu-weimu-spade-assault-check";
    private const string HeartAssaultMode = "identity:classic-jia-xu-weimu-heart-assault-check";
    private const string SpadeDuelMode = "identity:classic-jia-xu-weimu-spade-duel-check";

    public static void DefinitionAndPolicySchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "qun" } general &&
                general.SkillIds.SequenceEqual([Wansha, Luanwu, Weimu]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Jia Xu must be in the current Qun roster with three HP and his three skills.");
        Require(current.Skills[Wansha].Tags.HasFlag(SkillTag.Locked) &&
                current.Skills[Weimu].Tags.HasFlag(SkillTag.Locked) &&
                current.Skills[Luanwu].Tags.HasFlag(SkillTag.Limited),
            "Wansha and Weimu must be locked while Luanwu stays a limited skill.");

        var wansha = current.Skills[Wansha].Program!;
        Require(wansha.CardPolicies.Single() is
        {
            Kind: SkillProgramCardPolicyKind.ExclusiveDyingPeachRescue,
            CardKinds: [CardKind.Peach]
        } peachPolicy && peachPolicy.InputSuit is null && peachPolicy.Condition.Kind ==
            SkillProgramConditionKind.Always,
            "Wansha must grant the existing exclusive dying peach rescue for Peach only.");

        var luanwu = current.Skills[Luanwu].Program!;
        var activation = luanwu.Activations.Single();
        Require(activation is
        {
            Id: Activation,
            MinCards: 0,
            MaxCards: 0,
            MinTargets: 0,
            MaxTargets: 0,
            UsesPerGame: 1,
            UsesPerTurn: null,
            TargetKind: SkillProgramTargetKind.OtherLiving
        } && activation.Effects.Single() is
        {
            Op: SkillProgramEffectOp.RequestSlashByNearest,
            Target: SkillProgramEffectTarget.Owner,
            TargetKind: SkillProgramTargetKind.OtherLiving,
            Amount: 1
        } forced && forced.Condition.Kind == SkillProgramConditionKind.Always,
            "Luanwu must be a once-per-game walk that asks every other character for a nearest Slash.");

        var weimu = current.Skills[Weimu].Program!;
        Require(weimu.CardPolicies.Count == 2 &&
                weimu.CardPolicies.All(policy =>
                    policy.Kind == SkillProgramCardPolicyKind.ProhibitTargetBySuit &&
                    policy.OutputSuit is null && policy.RequiredCardKinds.Count == 0 &&
                    policy.Value == 0 && policy.Condition.Kind == SkillProgramConditionKind.Always) &&
                weimu.CardPolicies.Select(policy => policy.InputSuit)
                    .SequenceEqual([Suit.Spade, Suit.Club]) &&
                weimu.CardPolicies.All(policy =>
                    policy.CardKinds.Contains(CardKind.Duel) &&
                    policy.CardKinds.Contains(CardKind.BarbarianAssault) &&
                    policy.CardKinds.Contains(CardKind.Indulgence) &&
                    policy.CardKinds.Contains(CardKind.FiveGrains) &&
                    !policy.CardKinds.Contains(CardKind.Slash) &&
                    !policy.CardKinds.Contains(CardKind.Peach) &&
                    !policy.CardKinds.Contains(CardKind.Nullification) &&
                    !policy.CardKinds.Contains(CardKind.Lightning)),
            "Weimu must forbid both black suits over targeted tricks only, keeping basic cards, " +
            "Peach, Nullification and Lightning outside the shield.");

        const string weimuPresentation = """
            {"schemaVersion":3,"skills":{"fixture:weimu":{"name":"测试","description":"测试"}}}
            """;
        var blackTrickList = """
            "cardKinds":["duel","dismantlement","snatch","fireAttack","ironChain","borrowedSword",
            "indulgence","supplyShortage","barbarianAssault","arrowBarrage","peachGarden","fiveGrains"]
            """;
        var curtainTemplate = $$"""
            {"schemaVersion":62,"skills":[{"id":"fixture:weimu","revision":1,
            "minimumRulesVersion":190,"cardPolicies":[
            {"id":"spade","kind":"prohibitTargetBySuit","inputSuit":"spade",{{blackTrickList}}}]}]}
            """;
        Require(SkillProgramCatalog.Load(curtainTemplate, weimuPresentation)
                .Programs["fixture:weimu"].CardPolicies.Single().InputSuit == Suit.Spade,
            "The black trick target prohibition must load with its input suit.");
        Reject(curtainTemplate.Replace("\"inputSuit\":\"spade\",", string.Empty),
            weimuPresentation, "a suit prohibition requires its input suit");
        Reject(curtainTemplate.Replace("\"inputSuit\":\"spade\"", "\"inputSuit\":\"spade\",\"outputSuit\":\"heart\""),
            weimuPresentation, "a suit prohibition must not rewrite a suit");
        Reject(curtainTemplate.Replace("\"duel\"", "\"slash\""),
            weimuPresentation, "a suit prohibition must not cover a Slash");
        Reject(curtainTemplate.Replace("\"duel\"", "\"peach\""),
            weimuPresentation, "a suit prohibition must not cover a basic card or Peach");

        const string slashPresentation = """
            {"schemaVersion":3,"skills":{"fixture:luanwu":{"name":"测试","description":"测试"}}}
            """;
        const string slashTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:luanwu","revision":1,
            "minimumRulesVersion":190,"activations":[
            {"id":"force","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
            "targetKind":"otherLiving","usesPerTurn":null,"usesPerGame":1,
            "effects":[{"op":"requestSlashByNearest","target":"owner","targetKind":"otherLiving","amount":1}]}]}]}
            """;
        Require(SkillProgramCatalog.Load(slashTemplate, slashPresentation)
                .Programs["fixture:luanwu"].Activations.Single().Effects.Single()
                .Op == SkillProgramEffectOp.RequestSlashByNearest,
            "The nearest-character Slash request must load inside a once-per-game activation.");
        Reject(slashTemplate.Replace("\"targetKind\":\"otherLiving\",\"amount\":1",
                "\"targetKind\":\"anyLiving\",\"amount\":1"),
            slashPresentation, "the Slash request must walk other characters only");
        Reject(slashTemplate.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            slashPresentation, "the Slash request must be owned by its general");
        Reject(slashTemplate.Replace("\"amount\":1", "\"amount\":0"),
            slashPresentation, "the Slash request must carry a positive HP penalty");
        Reject(slashTemplate.Replace("\"amount\":1", "\"amount\":1,\"secondaryAmount\":2"),
            slashPresentation, "a suspended Slash request prompt needs one fixed HP penalty");
    }

    public static void LuanwuForcesNearestSlashAndReplays()
    {
        var registry = Registry(SlashMode, Deck("standard:slash", Suit.Spade), bankHp: 4);
        var game = Start(registry, 7, SlashMode);
        DriveToHumanPlayPhase(game);
        var action = game.GetHumanLegalActions().SingleOrDefault(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Luanwu);
        Require(action is not null, "Luanwu must advertise a cardless, targetless limited activation.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(paused, registry);
        ActivateLuanwu(game);
        DriveToHumanPlayPhase(replay);
        ActivateLuanwu(replay);
        DriveUntilSettled(game);
        DriveUntilSettled(replay);
        Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
            "The Luanwu Slash walk must replay identically from the participant prompt.");

        var hpLosses = game.Events.Select(item => item.Payload)
            .OfType<ProgramSkillHpLostEvent>().Where(item => item.SkillId == Luanwu).ToArray();
        Require(hpLosses.Length == 0,
            "Every participant held a Slash, so Luanwu must not have taken anyone's HP.");

        // In a five-seat circle every neighbour sits at distance one, so the lowest
        // seat of the tie breaks each participant's forced Slash target.
        var walk = ForcedSlashUses(game);
        Require(walk.Select(item => item.SourceSeat).SequenceEqual([1, 2, 3, 4]),
            "Luanwu must walk the field from the owner's next seat in turn order.");
        var expected = new Dictionary<int, int> { [1] = 0, [2] = 1, [3] = 2, [4] = 0 };
        foreach (var (seat, target) in expected)
        {
            var uses = walk.Where(item => item.SourceSeat == seat).ToArray();
            Require(uses.Length == 1 && uses[0].TargetSeats.SequenceEqual([target]),
                $"Seat {seat} must answer Luanwu with exactly one Slash against seat {target}.");
        }
        var snapshot = game.CreateSnapshot(0, true);
        Require(snapshot.Players[0].Hp == snapshot.Players[0].MaxHp - 2 &&
                snapshot.Players[1].Hp == snapshot.Players[1].MaxHp - 1 &&
                snapshot.Players[2].Hp == snapshot.Players[2].MaxHp - 1 &&
                snapshot.Players[3].Hp == snapshot.Players[3].MaxHp &&
                snapshot.Players[4].Hp == snapshot.Players[4].MaxHp,
            "Only the forced Slashes may move HP: the lord absorbs seats one and four.");
    }

    public static void LuanwuMakesSlashlessParticipantsLoseHpInTurnOrder()
    {
        var registry = Registry(DodgeMode, Deck("standard:dodge", Suit.Club), bankHp: 4);
        var game = Start(registry, 11, DodgeMode);
        DriveToHumanPlayPhase(game);
        Require(game.GetHumanLegalActions().Any(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Luanwu),
            "Luanwu must stay available while no participant holds a Slash.");

        var paused = RoundTrip(game.CreateCheckpoint());
        ActivateLuanwu(game);
        DriveUntilSettled(game);
        Require(ForcedSlashUses(game).Length == 0,
            "No participant may answer Luanwu with a Slash from a Dodge-only field.");
        var losses = game.Events.Select(item => item.Payload)
            .OfType<ProgramSkillHpLostEvent>().Where(item => item.SkillId == Luanwu).ToArray();
        Require(losses.Select(item => item.TargetSeat).SequenceEqual([1, 2, 3, 4]) &&
                losses.All(item => item.Amount == 1 && item.RemainingHp == 3),
            "Luanwu must drain one HP from every other character from the owner's next seat onward.");
        var snapshot = game.CreateSnapshot(0, true);
        Require(snapshot.Players[0].Hp == snapshot.Players[0].MaxHp &&
                snapshot.Players.Skip(1).All(player => player.Hp == 3),
            "The HP penalty must leave the owner untouched and every participant at three HP.");

        var continued = GameReplay.Restore(paused, registry);
        DriveToHumanPlayPhase(continued);
        ActivateLuanwu(continued);
        DriveUntilSettled(continued);
        DriveToHumanPlayPhase(continued);
        DriveToHumanPlayPhase(game);
        Require(Events(continued).SequenceEqual(Events(game)) && State(continued) == State(game),
            "The Luanwu HP walk must replay identically from the activation checkpoint.");
        Require(!game.GetHumanLegalActions().Any(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Luanwu),
            "Luanwu is a once-per-game limited skill.");
    }

    public static void WeimuExcludesBlackTrickTargets()
    {
        var blackAssaults = Registry(SpadeAssaultMode, Deck("standard:barbarian_assault", Suit.Spade), 4);
        var blackDuels = Registry(SpadeDuelMode, Deck("standard:duel", Suit.Spade), 4);
        var redAssaults = Registry(HeartAssaultMode, Deck("standard:barbarian_assault", Suit.Heart), 4);
        var blackSeen = 0;
        var duelSeen = 0;
        var redSeen = 0;
        for (var seed = 1; seed <= 24; seed++)
        {
            var assault = Start(blackAssaults, seed, SpadeAssaultMode);
            DriveGame(assault);
            var uses = assault.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                .Where(item => item.CardKind == CardKind.BarbarianAssault).ToArray();
            Require(uses.All(item => !Targets(assault, item.ResolutionId).Contains(0)),
                "A black Nanman Invasion can never choose the Curtain bearer.");
            Require(uses.All(item => Targets(assault, item.ResolutionId).Count > 0),
                "A black Nanman Invasion must still resolve against the unshielded characters.");
            if (uses.Length > 0 && assault.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.TargetSeat != 0)) blackSeen++;

            var duel = Start(blackDuels, seed, SpadeDuelMode);
            DriveGame(duel);
            var duels = duel.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                .Where(item => item.CardKind == CardKind.Duel).ToArray();
            Require(duels.All(item => !Targets(duel, item.ResolutionId).Contains(0)),
                "A black Duel can never choose the Curtain bearer.");
            if (duels.Length > 0) duelSeen++;

            var red = Start(redAssaults, seed, HeartAssaultMode);
            DriveGame(red);
            var redUses = red.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                .Where(item => item.CardKind == CardKind.BarbarianAssault).ToArray();
            Require(redUses.All(item => Targets(red, item.ResolutionId).Contains(0)),
                "A red Nanman Invasion must keep choosing the Curtain bearer like anyone else.");
            if (redUses.Length > 0 && red.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.TargetSeat == 0)) redSeen++;
        }
        Require(blackSeen > 0 && duelSeen > 0 && redSeen > 0,
            "The Curtain fixtures must actually reach black Nanman Invasion and Duel uses plus one red hit on Jia Xu.");
    }

    private static IReadOnlyList<int> Targets(GameEngine game, long resolutionId) =>
        game.Events.Select(item => item.Payload).OfType<TargetsConfirmedEvent>()
            .Single(item => item.ResolutionId == resolutionId).TargetSeats;

    private static (int SourceSeat, IReadOnlyList<int> TargetSeats)[] ForcedSlashUses(GameEngine game)
    {
        var uses = game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
            .Where(item => item.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .ToArray();
        return uses.Select(item => (item.SourceSeat, Targets(game, item.ResolutionId))).ToArray();
    }

    private static void ActivateLuanwu(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Luanwu play-phase prompt vanished.");
        Accept(game.Submit(new UseProgramSkillCommand(0, Luanwu, Activation, [], [],
            game.Revision, prompt.PromptId)));
    }

    private static void DriveGame(GameEngine game) =>
        DriveUntil(game, () => false, budget: 1600);

    private static void DriveToHumanPlayPhase(GameEngine game)
    {
        for (var step = 0; step < 600; step++)
        {
            if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("The Jia Xu fixture ended before its play phase.");
            var prompt = game.PendingDecision;
            if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.State.CurrentSeat == 0) return;
            AnswerOrAdvance(game, prompt);
        }
        throw new InvalidOperationException("The Jia Xu fixture never reached the lord's play phase.");
    }

    private static void DriveUntil(GameEngine game, Func<bool> done, int budget)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
            AnswerOrAdvance(game, game.PendingDecision);
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0, 900);

    private static void AnswerOrAdvance(GameEngine game, PendingDecision? prompt)
    {
        if (prompt is null || prompt.PlayerSeat != 0)
        {
            Advance(game);
            return;
        }
        switch (prompt.Kind)
        {
            case DecisionKind.PlayCard:
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                break;
            case DecisionKind.DiscardCards:
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                break;
            default:
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices.First();
                Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    choice.Id, game.Revision)));
                break;
        }
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck, int bankHp) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new JiaXuScenario(mode, deck, bankHp));

    private static ContentDeckRecipe Deck(string cardId, Suit suit) =>
        new("fixture:jia-xu-deck", "贾诩测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(cardId, suit, index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 14
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Jia Xu fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Jia Xu selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Jia Xu fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Jia Xu command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Jia Xu composition to be rejected: {because}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class JiaXuScenario(string modeId, ContentDeckRecipe deck, int bankHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("jia-xu-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            // Seats 1-4 are skill-less AI banks so the only live rules are Jia Xu's
            // own skills plus the forced Slash requests Luanwu puts on those seats.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:jia-xu-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "贾诩测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:jia-xu-bank-a",
                    "fixture:jia-xu-bank-b",
                    "fixture:jia-xu-bank-c",
                    "fixture:jia-xu-bank-d"]));
        }
    }
}
