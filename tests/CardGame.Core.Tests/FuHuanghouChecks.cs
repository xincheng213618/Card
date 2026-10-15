using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FuHuanghouChecks
{
    private const string Host = "fixture:fhh-host";
    private const string DriverSkill = "fixture:fhh-driver";
    private const string Qiuyuan = "classic:qiuyuan";
    private const string Zhuikong = "classic:zhuikong";
    private const string Mode = "identity:fuhuanghou-check-5";

    public static void QiuyuanDodgePaymentAndExtraTargetBothReplay()
    {
        var registry = Registry();
        var branches = new[] { "give-dodge", "become-target" };
        foreach (var branch in branches)
        {
            var done = false;
            for (var seed = 1; seed < 400 && !done; seed++)
            {
                var game = Start(registry, seed);
                ReachPlay(game);
                Driver(game, "request-slash", [1]);
                if (!AwaitOffer(game, Qiuyuan)) continue;
                Answer(game, Pending(game)!.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate"));
                var invitePrompt = Pending(game)!;
                var invited = invitePrompt.Choices
                    .Where(choice => choice.Targets.Count == 1)
                    .Select(choice => choice.Targets.Single())
                    .FirstOrDefault(seat => seat != 0 && seat != 1);
                if (invited == 0) continue;
                var invitedDodges = game.CreateSnapshot(invited, true).Players[invited].Hand
                    .Where(card => card.Kind == CardKind.Dodge).Select(card => card.Id).ToArray();
                if (branch == "give-dodge" && invitedDodges.Length == 0) continue;
                Answer(game, invitePrompt.Choices.First(choice => choice.Targets.SequenceEqual([invited])));
                if (!AwaitOffer(game, Qiuyuan)) continue;
                var answerPrompt = Pending(game)!;
                Require(answerPrompt.PlayerSeat == invited &&
                        answerPrompt.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("option-id") == "become-target"),
                    "The invited character must always be able to take the extra Slash target.");
                if (!answerPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("option-id") == branch)) continue;
                var hostHandBefore = game.CreateSnapshot(0, true).Players[0].Hand.Count;
                var addedBefore = game.Events.Count(item => item.Payload is ProgramCardUseTargetAddedEvent);
                AnswerOption(game, branch);
                if (branch == "give-dodge")
                {
                    if (!AwaitOffer(game, Qiuyuan)) continue;
                    Answer(game, Pending(game)!.Choices.First(choice => choice.Cards.Count == 1));
                }
                else
                {
                    var use = game.ResolutionStack.OfType<CardUseFrame>().Last();
                    Require(use.TargetSeats.Contains(0) && use.TargetSeats.Contains(invited) &&
                            use.Action!.ActorSeat == 1,
                        "The same Slash must keep Fu Hehou and gain the invited character as an extra target.");
                }
                AssertReplay(game, registry);
                Finish(game);
                var paid = game.CardMovements.Any(move =>
                    move.From == CardLocation.Hand(invited) && move.To == CardLocation.Hand(0) &&
                    invitedDodges.Contains(move.CardId));
                Require(branch == "give-dodge"
                        ? paid && game.CreateSnapshot(0, true).Players[0].Hand.Count == hostHandBefore + 1 &&
                            game.Events.Count(item => item.Payload is ProgramCardUseTargetAddedEvent) == addedBefore
                        : game.Events.Count(item => item.Payload is ProgramCardUseTargetAddedEvent) == addedBefore + 1 &&
                            game.Events.Select(item => item.Payload).OfType<ProgramCardUseTargetAddedEvent>()
                                .Single(item => item.SkillId == Qiuyuan).TargetSeat == invited,
                    branch == "give-dodge"
                        ? $"Seat {invited} must keep only its target slot by handing over a real Dodge."
                        : $"Seat {invited} must become an additional target of the same Slash.");
                AssertReplay(game, registry);
                done = true;
            }
            Require(done, $"No fixture produced the Qiuyuan {branch} branch.");
        }
    }

    public static void ZhuikongPinsTheContestantNotItsOwnerAndReplay()
    {
        var registry = Registry();
        foreach (var wantsWin in new[] { true, false })
        {
            var done = false;
            for (var seed = 1; seed < 400 && !done; seed++)
            {
                var game = Start(registry, seed);
                ReachPlay(game);
                Driver(game, "hurt-owner", [1]);
                ReachPlay(game);
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Pending(game)!.PromptId)));
                if (!AwaitOffer(game, Zhuikong)) continue;
                Answer(game, Pending(game)!.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate"));
                var contestPrompt = Pending(game)!;
                if (!contestPrompt.Choices.Any(choice => choice.Targets.SequenceEqual([1]))) continue;
                Answer(game, contestPrompt.Choices.First(choice => choice.Targets.SequenceEqual([1])));
                for (var step = 0; step < 12 && Pending(game)?.PlayerSeat != 0; step++)
                    if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
                var chooser = Pending(game)!;
                var hand = game.CreateSnapshot(0, true).Players[0].Hand;
                var cards = chooser.Choices
                    .Where(choice => choice.Cards.Count == 1 &&
                        hand.Any(card => card.Id == choice.Cards.Single()))
                    .Select(choice => (Choice: choice, Rank: hand.Single(card => card.Id == choice.Cards.Single()).Rank))
                    .ToArray();
                if (cards.Length == 0) continue;
                Answer(game, (wantsWin ? cards.OrderByDescending(item => item.Rank) : cards.OrderBy(item => item.Rank))
                    .First().Choice);
                PindianResult? Contest() => game.Events.Select(item => item.Payload)
                    .OfType<PindianResultDeterminedEvent>()
                    .Where(item => item.SkillId == Zhuikong)
                    .Select(item => item.Result)
                    .LastOrDefault();
                for (var step = 0; step < 60 && Contest() is null; step++)
                    if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
                var result = Contest();
                if (result is not { SourceSeat: 0, OpponentSeat: 1 }) continue;
                if (result!.SourceWon != wantsWin) continue;
                var pinned = game.Events.Select(item => item.Payload).OfType<CardTargetRestrictionGrantedEvent>()
                    .SingleOrDefault(item => item.Restriction.Source.SkillId == Zhuikong);
                var opened = game.Events.Select(item => item.Payload).OfType<DirectedTurnCardPolicyGrantedEvent>()
                    .SingleOrDefault(item => item.Policy.Source.SkillId == Zhuikong);
                Require(wantsWin
                        ? pinned is { Restriction.Restriction: SkillProgramCardTargetRestriction.SelfOnly } &&
                            pinned.Restriction.TargetSeat is null &&
                            pinned.Restriction.SubjectSeat == 1 &&
                            pinned.Restriction.Source.OwnerSeat == 0 && opened is null
                        : opened is { Policy.ActorSeat: 1, Policy.TargetSeat: 0 } &&
                            opened.Policy.Effects == DirectedTurnCardPolicyEffect.IgnoreDistance && pinned is null,
                    wantsWin
                        ? "A winning Zhuikong must pin the turn player to self-targeting, not its owner."
                        : "A losing Zhuikong must only open the turn player's distance to Fu Hehou.");
                AssertReplay(game, registry);
                Finish(game);
                AssertReplay(game, registry);
                done = true;
            }
            Require(done, $"No fixture produced a {(wantsWin ? "won" : "lost")} Zhuikong contest.");
        }
    }

    private static bool AwaitOffer(GameEngine game, string skillId)
    {
        for (var step = 0; step < 40; step++)
        {
            if (Pending(game) is { Kind: DecisionKind.ProgramTrigger } prompt &&
                prompt.SkillPrompt?.SkillId == skillId) return true;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) return false;
        }
        return false;
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, Host, game.Revision, Pending(game)!.PromptId)));
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 120 && Pending(game)?.Kind != DecisionKind.PlayCard; step++) Advance(game);
        Require(Pending(game)?.Kind == DecisionKind.PlayCard, "Fu Hehou fixture did not reach Play.");
    }

    private static void Driver(GameEngine game, string activation, IReadOnlyList<int> targets) =>
        Accept(game.Submit(new UseProgramSkillCommand(0, DriverSkill, activation, [], targets, game.Revision,
            Pending(game)!.PromptId)));

    private static void AnswerOption(GameEngine game, string option) => Answer(game,
        Pending(game)!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == option));

    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(
        Pending(game)!.PlayerSeat, Pending(game)!.PromptId, choice.Id, game.Revision)));

    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat, true).PendingDecision)
            .FirstOrDefault(prompt => prompt is not null);

    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));

    private static void Finish(GameEngine game)
    {
        for (var step = 0; step < 200 && game.ResolutionStack.Count > 0; step++)
            if (Pending(game) is { } prompt && (prompt.Kind == DecisionKind.ProgramTrigger || prompt.PlayerSeat == 0))
                Answer(game, prompt.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "take-damage") ?? prompt.Choices.First());
            else Advance(game);
        Require(game.ResolutionStack.Count == 0, "Fu Hehou fixture resolution did not finish.");
    }

    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 5).All(seat =>
                JsonSerializer.Serialize(game.CreateSnapshot(seat, true)) ==
                JsonSerializer.Serialize(replay.CreateSnapshot(seat, true))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(replay.ResolutionStack) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements) &&
            Events(game).SequenceEqual(Events(replay)),
            "Fu Hehou checkpoint must restore state, movements and typed events identically.");
    }

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Accept(CommandResult result) =>
        Require(result.Accepted, result.Error?.Message ?? "Fu Hehou command failed.");

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fu-huanghou-check", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            const string rules = """
            {"schemaVersion":62,"skills":[{"id":"fixture:fhh-driver","revision":1,"minimumRulesVersion":190,"activations":[
            {"id":"request-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
             "effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]},
            {"id":"hurt-owner","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
             "effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]}]}
            """;
            var driver = SkillProgramCatalog.Load(rules,
                """{"schemaVersion":3,"skills":{"fixture:fhh-driver":{"name":"状态测试","description":"杀与伤害原语"}}}""");
            builder.AddSkill(new ContentSkillDefinition(DriverSkill, "状态测试", "测试")
            {
                Program = driver.Programs[DriverSkill]
            });
            builder.AddGeneral(new ContentGeneralDefinition(Host, "伏后测试角色", "supporter", DriverSkill, "qun",
                BaseHp: 8, AdditionalSkillIds: [Qiuyuan, Zhuikong], Gender: GeneralGender.Female));
            foreach (var seat in new[] { 'a', 'b', 'c', 'd' })
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:fhh-bank-{seat}", "对手", "supporter",
                    "standard:none", "qun", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe("fixture:fhh-deck", "伏后测试牌堆", 5, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 200).Select(index => new ContentDeckPhysicalCard(
                    index % 2 == 0 ? "standard:slash" : "standard:dodge",
                    (Suit)(index % 4), index % 5 == 4 ? 13 : index % 3 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(Mode, "伏后测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:fhh-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [Host, "fixture:fhh-bank-a", "fixture:fhh-bank-b",
                    "fixture:fhh-bank-c", "fixture:fhh-bank-d"]));
        }
    }
}
