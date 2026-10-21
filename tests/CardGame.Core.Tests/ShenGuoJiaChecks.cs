using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenGuoJiaChecks
{
    private const string Owner = "fixture:shen-guo-jia-owner", Quiet = "fixture:shen-guo-jia-quiet", Driver = "fixture:shen-guo-jia-driver", Mode = "identity:classic-shen-guo-jia-fixture";
    public static void HuishiNativeRepeatedJudgmentGiftAndColdRestore()
    {
        var (game, registry) = Start(); Play(game); var initialMaximum = V(game, 0).MaxHp; Use(game, "mobile:huishi", "judgment-chain", []);
        Reach(game, () => Root(game)?.ShenGuoJia?.Stage == ShenGuoJiaStage.Repeat && P(game) is not null);
        Need(E<JudgmentResolvedEvent>(game).Count(fact => fact.Reason == "mobile-huishi") == 1 && V(game, 0).MaxHp == initialMaximum, "Huishi's first real judgment is not an automatic maximum-HP reward.");
        game = Cold(game, registry); Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "repeat");
        Reach(game, () => Root(game)?.ShenGuoJia?.Stage == ShenGuoJiaStage.Recipient && P(game) is not null);
        Need(Root(game)!.ShenGuoJia!.Judgments.Count == 2 && V(game, 0).MaxHp == initialMaximum + 1, "The second same-suit native result stops the chain after exactly one paid continuation.");
        var ids = Root(game)!.ShenGuoJia!.Cards.ToArray(); var before = V(game, 1).HandCount;
        game = Cold(game, registry); Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "recipient" && choice.Targets.SequenceEqual([1])); Play(game);
        Need(ids.Length == 2 && V(game, 1).HandCount == before + 2 && ids.All(id => game.CardMovements.Any(move => move.CardId == id && move.From == CardLocation.Processing && move.To == CardLocation.Hand(1) && move.Reason.Value == "skill-program.mobile:huishi.give")), "All retained native judgment entities move to the chosen recipient once.");
        Need(E<ShenGuoJiaMaximumPaidEvent>(game).Count(fact => fact.Purpose == "huishi-grow") == 1 && E<ShenGuoJiaMaximumPaidEvent>(game).Count(fact => fact.Purpose == "huishi-most-hand") == 1 && V(game, 0).MaxHp == initialMaximum, "A recipient tied for most hand cards causes the exact one-point maximum loss.");
        _ = Cold(game, registry);
    }
    public static void HuiShiFourDrawFallbackUsesNativeAttempt()
    {
        var (game, registry) = Start(); Play(game);
        for (var attempt = 0; attempt < 30 && V(game, 0).MaxHp >= game.CreateSnapshot(0).Players.Count(player => player.IsAlive); attempt++) { Use(game, Driver, "shrink", []); Play(game); }
        Need(V(game, 0).MaxHp == game.CreateSnapshot(0).Players.Count(player => player.IsAlive) - 1, "The actual fixture maximum must be below its current living-player threshold before Hui Shi fallback.");
        var before = V(game, 0).HandCount; var maximumBefore = V(game, 0).MaxHp;
        Use(game, "mobile:huishi-gift", "grant-awakening", [0]); Play(game);
        Need(V(game, 0).HandCount == before + 4 && V(game, 0).MaxHp == maximumBefore - 2 && !E<ShenGuoJiaAwakeningOverrideEvent>(game).Any(), "Below the living-player threshold, even an unawakened target gets four real drawn cards and the owner loses two maximum HP.");
        Need(E<ShenGuoJiaMaximumPaidEvent>(game).Single().Before == maximumBefore && E<ShenGuoJiaMaximumPaidEvent>(game).Single().After == maximumBefore - 2, "Hui Shi charges its maximum-HP payment once after the benefit.");
        _ = Cold(game, registry);
    }
    public static void HuiShiAwakensAtPreparationAndZuoxingUsesRealTrickCold()
    {
        var (game, registry) = Start(); Play(game); Use(game, Driver, "grow", []); Play(game); var maximumBeforeGift = V(game, 0).MaxHp;
        Use(game, "mobile:huishi-gift", "grant-awakening", [0]);
        Reach(game, () => Root(game)?.ShenGuoJia?.Stage == ShenGuoJiaStage.AwakeningChoice && P(game) is not null);
        game = Cold(game, registry); Answer(game, choice => choice.Parameters.GetValueOrDefault("value") == "mobile:tianyi"); Play(game);
        Need(!E<SkillAwakenedEvent>(game).Any(fact => fact.SkillId == "mobile:tianyi") && V(game, 0).MaxHp == maximumBeforeGift - 2, "Hui Shi satisfies Tianyi's condition while leaving the actual preparation timing intact.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == "mobile:tianyi" && P(game)!.Choices.Any(choice => choice.Targets.Contains(0)));
        Answer(game, choice => choice.Targets.SequenceEqual([0])); Play(game);
        Need(E<SkillAwakenedEvent>(game).Count(fact => fact.SkillId == "mobile:tianyi") == 1 && V(game, 0).MaxHp == maximumBeforeGift && V(game, 0).Skills!.Any(skill => skill.ContentId == "mobile:zuoxing"), "A true Tianyi preparation increases maximum HP by two, recovers and grants the chosen beneficiary Zuoxing.");
        var before = V(game, 0).HandCount; var maximumBeforeTrick = V(game, 0).MaxHp;
        Use(game, "mobile:zuoxing", "borrow-ordinary-trick", []); Reach(game, () => Root(game)?.ShenGuoJia?.Stage == ShenGuoJiaStage.UseChoice && P(game) is not null);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "cancel"); Play(game); game = Cold(game, registry);
        Need(V(game, 0).MaxHp == maximumBeforeTrick && !E<ShenGuoJiaNativeIssuedEvent>(game).Any(), "Unpaid cancellation consumes neither the benefactor maximum HP nor the native card use.");
        Use(game, "mobile:zuoxing", "borrow-ordinary-trick", []); Reach(game, () => Root(game)?.ShenGuoJia?.Stage == ShenGuoJiaStage.UseChoice && P(game) is not null);
        game = Cold(game, registry); Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "use" && choice.Label.Contains("无中生有", StringComparison.Ordinal)); Play(game);
        Need(V(game, 0).MaxHp == maximumBeforeTrick - 1 && V(game, 0).HandCount == before + 2 && E<ShenGuoJiaNativeIssuedEvent>(game).Single().Kind == CardKind.DrawTwo && E<CardUseFinishedEvent>(game).Any(fact => fact.CardId == 0 && fact.CardKind == CardKind.DrawTwo), "Zuoxing pays its actual benefactor once and resolves a full native zero-material DrawTwo through card-use completion.");
        _ = Cold(game, registry);
    }
    public static void HuiShiForcesActualDujiangWithoutInventingArmorCold()
    {
        var (game, registry) = Start(true); Play(game); Use(game, Driver, "grow", []); Play(game);
        Use(game, "mobile:huishi-gift", "grant-awakening", [0]);
        Reach(game, () => Root(game)?.ShenGuoJia?.Stage == ShenGuoJiaStage.AwakeningChoice && P(game) is not null);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("value") == "mou-mobile:dujiang"); Play(game); game = Cold(game, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, () => E<MouLuMengDujiangAwakenedEvent>(game).Any()); Play(game);
        Need(E<MouLuMengDujiangAwakenedEvent>(game).Single().Armor == 0 && V(game, 0).Skills!.Any(skill => skill.ContentId == "mou-mobile:duojing") &&
            !E<SkillAwakenedEvent>(game).Any(fact => fact.SkillId == "mobile:tianyi"), "Hui Shi waives Dujiang's actual three-Armor threshold only for that awakening; it neither creates Armor nor awakens unrelated Tianyi.");
        _ = Cold(game, registry);
    }
    private static ProgramSkillFrame? Root(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(frame => frame.ShenGuoJia is not null);
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static IEnumerable<T> E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(value => value.Payload).OfType<T>();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose) { var prompt = P(game)!; var choice = prompt.Choices.First(choose); Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)); }
    private static void Use(GameEngine game, string skill, string binding, IReadOnlyList<int> targets) => Accept(game, new UseProgramSkillCommand(0, skill, binding, [], targets, game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game) => Reach(game, () => P(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine game, Func<bool> done)
    {
        for (var index = 0; index < 220; index++)
        {
            if (done()) return; var prompt = P(game);
            if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "select-god-faction")) Answer(game, choice => choice.Parameters.GetValueOrDefault("faction-id") == "wei");
            else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The expected Shen Guo Jia native boundary was not reached: " + JsonSerializer.Serialize(new { Prompt = P(game), Frames = game.ResolutionStack }));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(), Movements = game.CardMovements, Events = game.Events.Select(value => JsonSerializer.Serialize(value.Payload, value.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Need(State(game) == State(restored), "Cold restore preserves Huishi final outcomes, exact physical entities, beneficiary identity and paid continuation."); return restored; }
    private static (GameEngine, ContentRegistry) Start(bool withDujiang = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(withDujiang));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, () => P(game) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId)); return (game, registry);
    }
    private sealed class Fixture(bool withDujiang) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-guo-jia", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},{"id":"{{Driver}}","revision":1,"activations":[{"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":1}]},{"id":"shrink","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"changeMaximumHp","target":"owner","amount":-1}]}]}]}""";
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Quiet] = new { name = "安静同桌", description = "跳过同桌的出牌阶段。" }, [Driver] = new { name = "真实夹具", description = "通过已授权命令增加一点体力上限。" } } });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var ownerSkills = new List<string> { "mobile:tianyi", "mobile:huishi-gift", Driver }; if (withDujiang) ownerSkills.Add("mou-mobile:dujiang");
            builder.AddGeneral(new(Owner, "神郭嘉夹具", "supporter", "mobile:huishi", "god", 3, ownerSkills, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(index => $"fixture:shen-guo-jia-peer-{index}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "安静同桌", "supporter", Quiet, "wei", 12));
            builder.AddDeck(new("fixture:shen-guo-jia-deck", "单一花色真实牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 128).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(Mode, "神郭嘉原生夹具", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:shen-guo-jia-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}

