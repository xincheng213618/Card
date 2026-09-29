using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoZhiChecks
{
    private const string General = "classic:cao-zhi";
    private const string Luoying = "classic:luoying";
    private const string Jiushi = "classic:jiushi";
    private const string LuoyingMode = "identity:classic-cao-zhi-luoying-check-5";
    private const string JiushiMode = "identity:classic-cao-zhi-jiushi-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([Luoying, Jiushi]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Cao Zhi must be in the current Wei roster with three HP.");

        var luoying = current.Skills[Luoying].Program!;
        var claim = luoying.Triggers.Single();
        Require(claim.Window == SkillProgramTriggerWindow.DiscardPileReceived &&
                claim.Subject == SkillProgramTriggerSubject.Owner &&
                claim.Optional &&
                claim.Suits.SequenceEqual([Suit.Club]) &&
                claim.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                claim.ExcludedMovementReasons.Contains("card.use-finished") &&
                claim.ExcludedMovementReasons.Contains("card.respond.nullification-finished"),
            "Luoying must observe every other player's club discards except use-resolution leftovers.");
        var claimEffect = claim.Effects.Single();
        Require(claimEffect.Op == SkillProgramEffectOp.ClaimMovedCards &&
                claimEffect.Target == SkillProgramEffectTarget.Owner &&
                claimEffect.Condition.Kind == SkillProgramConditionKind.Always,
            "Luoying must claim the observed movement with a single unconditional owner effect.");

        var jiushi = current.Skills[Jiushi].Program!;
        var flip = jiushi.Triggers.Single(item => item.Id == "flip-for-virtual-alcohol");
        Require(flip.Window == SkillProgramTriggerWindow.SelfDyingResponse &&
                flip.Subject == SkillProgramTriggerSubject.Owner &&
                !flip.Optional &&
                flip.Condition.Kind == SkillProgramTriggerConditionKind.Always,
            "Jiushi must bind its dying rescue unconditionally and gate the flip on the choice.");
        Require(flip.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChooseOption,
                SkillProgramEffectOp.TurnOver,
                SkillProgramEffectOp.UseVirtualDyingAlcohol]),
            "Jiushi must ask, then flip the general card and use the virtual dying alcohol.");
        var ask = flip.Effects[0];
        Require(ask.ResultBind == "jiushi-choice" &&
                ask.Options.Select(item => item.Id).SequenceEqual(["flip", "pass"]) &&
                ask.Options[0].Condition is
                {
                    Kind: SkillProgramConditionKind.Not,
                    Children: [{ Kind: SkillProgramConditionKind.FaceDown }]
                },
            "Jiushi must offer the flip option only while the general card is face up.");
        Require(flip.Effects[1].Condition is
            {
                Kind: SkillProgramConditionKind.ChoiceIs,
                SourceBind: "jiushi-choice",
                OptionId: "flip"
            } &&
            flip.Effects[2].Condition is
            {
                Kind: SkillProgramConditionKind.ChoiceIs,
                SourceBind: "jiushi-choice",
                OptionId: "flip"
            },
            "Both rescue effects must stay bound to the recorded flip choice.");

        var flipBack = jiushi.Triggers.Single(item => item.Id == "flip-back-after-damage");
        Require(flipBack.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                flipBack.Subject == SkillProgramTriggerSubject.Owner &&
                flipBack.Optional &&
                flipBack.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                flipBack.Condition.Kind == SkillProgramTriggerConditionKind.FaceDown &&
                flipBack.Effects.Single() is { Op: SkillProgramEffectOp.TurnOver } backEffect &&
                backEffect.Condition.Kind == SkillProgramConditionKind.FaceDown,
            "Jiushi must offer one face flip back per damage taken while face down.");

        const string claimTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:claim","revision":1,
            "minimumRulesVersion": 189,
            "triggers":[{"id":"take","window":"discardPileReceived","subject":"owner","suits":["club"],
            "optional":true,"priority":0,
            "excludedMovementReasons":["card.use-finished"],
            "effects":[{"op":"claimMovedCards","target":"owner"}]}]}]}
            """;
        const string claimPresentation = """
            {"schemaVersion":3,"skills":{"fixture:claim":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(claimTemplate, claimPresentation)
                .Programs["fixture:claim"].Triggers.Single().Effects.Count == 1,
            "The discard-pile claim trigger must load with its movement filters.");
        Reject(claimTemplate.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            claimPresentation, "the claim must target its owner");
        Reject(claimTemplate.Replace(
                "{\"op\":\"claimMovedCards\",\"target\":\"owner\"",
                "{\"op\":\"claimMovedCards\",\"target\":\"owner\",\"condition\":{\"kind\":\"faceDown\"}"),
            claimPresentation, "the claim effect must stay unconditional");
        Reject(claimTemplate.Replace("\"window\":\"discardPileReceived\"", "\"window\":\"turnEnding\""),
            claimPresentation, "the discard-pile claim trigger must keep its window");

        const string rescueTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:rescue","revision":1,
            "minimumRulesVersion": 189,
            "triggers":[{"id":"flip","window":"selfDyingResponse","subject":"owner","optional":false,
            "priority":0,
            "effects":[
            {"op":"chooseOption","target":"owner","resultBind":"choice","options":[
            {"id":"flip","condition":{"kind":"not","children":[{"kind":"faceDown"}]}},
            {"id":"pass","condition":{"kind":"always"}}]},
            {"op":"turnOver","target":"owner",
            "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"flip"}},
            {"op":"useVirtualDyingAlcohol","target":"owner",
            "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"flip"}}]}]}]}
            """;
        const string rescuePresentation = """
            {"schemaVersion":3,"skills":{"fixture:rescue":{"name":"测试","description":"测试",
            "optionLabels":{"flip":"翻面","pass":"放弃"}}}}
            """;
        Require(SkillProgramCatalog.Load(rescueTemplate, rescuePresentation)
                .Programs["fixture:rescue"].Triggers.Count == 1,
            "The self-dying rescue chain must load with its choice gate.");
        Reject(rescueTemplate.Replace("\"op\":\"useVirtualDyingAlcohol\",\"target\":\"owner\"",
                "\"op\":\"useVirtualDyingAlcohol\",\"target\":\"selectedTarget\""),
            rescuePresentation, "the virtual dying alcohol must act on the owner");
        Reject(rescueTemplate.Replace(
                "{\"op\":\"useVirtualDyingAlcohol\",\"target\":\"owner\"",
                "{\"op\":\"useVirtualDyingAlcohol\",\"target\":\"owner\",\"condition\":{\"kind\":\"faceDown\"}"),
            rescuePresentation,
            "the virtual dying alcohol must stay unconditional or choice-gated");
        const string damageTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:dmg","revision":1,
            "minimumRulesVersion": 189,
            "triggers":[{"id":"back","window":"afterDamageApplied","subject":"owner",
            "optional":true,"effects":[{"op":"turnOver","target":"owner"}]}]}]}
            """;
        const string damagePresentation = """
            {"schemaVersion":3,"skills":{"fixture:dmg":{"name":"测试","description":"测试"}}}
            """;
        Reject(damageTemplate, damagePresentation,
            "an after-damage trigger must declare its damage occurrence");
        Reject(rescueTemplate.Replace("\"priority\":0,",
                "\"priority\":0,\"condition\":{\"kind\":\"faceDown\"},"),
            rescuePresentation,
            "the self-dying window does not freeze trigger-condition facts yet");
        const string suitTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:suit","revision":1,
            "minimumRulesVersion": 189,
            "triggers":[{"id":"take","window":"turnEnding","subject":"owner","suits":["club"],
            "optional":true,"priority":0,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string suitPresentation = """
            {"schemaVersion":3,"skills":{"fixture:suit":{"name":"测试","description":"测试"}}}
            """;
        Reject(suitTemplate, suitPresentation,
            "a lifecycle window must not declare a suit filter");
    }

    public static void LuoyingClaimsAnotherPlayersDiscardedClubAndReplays()
    {
        var registry = Registry(LuoyingMode, ClubDeck(), bankHp: 3);
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, LuoyingMode);
            DriveUntil(game, () => false, stopAtSkills: [Luoying]);
            if (!IsProgramPrompt(game, Luoying)) continue;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Luoying claim must replay identically from the paused prompt.");
            var claimed = game.Events.Select(item => item.Payload).OfType<ProgramMovedCardsClaimedEvent>()
                .Single(item => item.SkillId == Luoying);
            Require(claimed.OwnerSeat == 0 && claimed.SourceSeat != 0 &&
                    claimed.BindingId == "claim-discarded-club",
                "Luoying must claim another player's discarded club for its owner.");
            var movement = game.CardMovements.Single(item =>
                item.CardId == claimed.CardId &&
                item.From == CardLocation.DiscardPile &&
                item.To == CardLocation.Hand(0));
            Require(movement.Reason.Value.Contains(Luoying, StringComparison.Ordinal),
                "The claimed club must move from the discard pile into Cao Zhi's hand by the skill.");
            Require(game.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Id == claimed.CardId),
                "The claimed club must sit in Cao Zhi's hand.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Luoying && item.Activated),
                "Luoying must resolve as an activated program binding.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Luoying club claim.");
    }

    public static void JiushiRescuesDyingOwnerByFlippingAndReplays()
    {
        var registry = Registry(JiushiMode, SpadeSlashDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 200 && completed < 1; seed++)
        {
            var game = Start(registry, seed, JiushiMode);
            DriveUntil(game, () => false, stopAtSkills: [Jiushi], budget: 2600);
            if (!IsProgramPrompt(game, Jiushi)) continue;
            var before = game.CreateSnapshot(0, true).Players[0];
            Require(before.Hp <= 0 && !before.IsFaceDown,
                "The Jiushi prompt must appear for a face-up dying Cao Zhi.");

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AnswerJiushi(game, "flip");
            AnswerJiushi(replay, "flip");
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Jiushi rescue must replay identically from the paused choice.");
            var rescue = game.Events.Select(item => item.Payload).OfType<ProgramDyingRescueEvent>()
                .Single(item => item.SkillId == Jiushi);
            Require(rescue.OwnerSeat == 0 && rescue.VictimSeat == 0 &&
                    rescue.CardId == 0 && rescue.RecoveredHp == 1,
                "Jiushi must rescue its dying owner with a cardless virtual alcohol.");
            Require(game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                    .Any(item => item.CardId == 0 && item.CardKind == CardKind.Alcohol &&
                        item.SourceSeat == 0),
                "The virtual alcohol must be declared as a cardless use by the victim.");
            Require(game.Events.Select(item => item.Payload).OfType<RecoveryAppliedEvent>()
                    .Any(item => item.TargetSeat == 0 && item.Amount == 1),
                "The virtual alcohol must recover one HP for the victim.");
            var after = game.CreateSnapshot(0, true).Players[0];
            Require(after.IsFaceDown && after.Hp == before.Hp + 1,
                "The rescue must flip Cao Zhi face down and restore one HP.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Jiushi dying rescue.");
    }

    public static void JiushiFlipsBackAfterDamageWhileFaceDown()
    {
        var registry = Registry(JiushiMode, SpadeSlashDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 200 && completed < 1; seed++)
        {
            var game = Start(registry, seed, JiushiMode);
            DriveUntil(game, () => false, stopAtSkills: [Jiushi], budget: 2600);
            if (!IsProgramPrompt(game, Jiushi)) continue;
            AnswerJiushi(game, "flip");
            DriveUntil(game, () => false, stopAtSkills: [Jiushi], budget: 2600);
            if (game.State.Status == EngineStatus.Completed) continue;
            var prompt = game.PendingDecision!;
            if (prompt.SkillPrompt?.SkillId != Jiushi) continue;
            Require(game.CreateSnapshot(0, true).Players[0].IsFaceDown,
                "The flip-back prompt must appear only while Cao Zhi is face down.");

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            Require(!game.CreateSnapshot(0, true).Players[0].IsFaceDown,
                "The accepted flip-back must turn Cao Zhi face up again.");
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Jiushi flip-back must replay identically from the paused prompt.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == Jiushi &&
                        item.Window == SkillProgramTriggerWindow.AfterDamageApplied),
                "The flip-back must resolve through the after-damage program window.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Jiushi flip-back.");
    }

    private static void AnswerJiushi(GameEngine game, string optionId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Jiushi choice prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("option-id") == optionId) ??
            throw new InvalidOperationException($"The Jiushi prompt lost its {optionId} option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void AcceptTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The program trigger prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") != "skip") ??
            throw new InvalidOperationException("The program trigger lost its accept option.");
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 900)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return;
            }
            if (stopAtDecisions is not null && stopAtDecisions.Contains(prompt.Kind) &&
                prompt.PlayerSeat == 0)
            {
                return;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                        choice.Id, game.Revision)));
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck, int bankHp) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new CaoZhiScenario(mode, deck, bankHp));

    private static ContentDeckRecipe ClubDeck() =>
        DeckCore("standard:slash", Suit.Club);

    private static ContentDeckRecipe SpadeSlashDeck() =>
        DeckCore("standard:slash", Suit.Spade);

    private static ContentDeckRecipe DeckCore(string kind, Suit suit) =>
        new("fixture:cao-zhi-deck", "曹植测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kind, suit, index % 13 + 1)).ToArray()
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
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Cao Zhi fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Cao Zhi selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cao Zhi fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Cao Zhi command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
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
            $"Expected invalid Cao Zhi composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class CaoZhiScenario(string modeId, ContentDeckRecipe deck, int bankHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cao-zhi-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Luoying and Jiushi on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-zhi-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-zhi-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-zhi-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-zhi-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "曹植测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:cao-zhi-bank-a",
                    "fixture:cao-zhi-bank-b",
                    "fixture:cao-zhi-bank-c",
                    "fixture:cao-zhi-bank-d"]));
        }
    }
}
