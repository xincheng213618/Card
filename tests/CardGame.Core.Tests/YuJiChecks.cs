using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YuJiChecks
{
    private const string General = "classic:yu-ji";
    private const string Guhuo = "classic:guhuo";
    private const string Chanyuan = "classic:chanyuan";
    private const string BankSkill = "classic:tiandu";
    private const string BankGeneral = "fixture:yu-ji-bank-a";
    private const string Mode = "identity:classic-yu-ji-check-5";

    public static void DefinitionAndSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "qun" } general &&
                general.SkillIds.SequenceEqual([Guhuo]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "于吉 must be a three-HP qun general in the current identity pools.");

        var chanyuan = current.Skills[Chanyuan];
        Require(chanyuan.Tags.HasFlag(SkillTag.Locked) &&
                chanyuan.SuppressionRule is { OwnerHpEquals: 1 },
            "缠怨 must stay a locked skill that suppresses the owner's other skills at one HP.");

        var guhuo = current.Skills[Guhuo].Program!;
        var activation = guhuo.Activations.Single();
        Require(activation.MinCards == 1 && activation.MaxCards == 1 &&
                activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                activation.UsesPerTurn is null &&
                activation.UsesPerAnyTurn == 1 &&
                activation.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.UsePlacedCardAsDeclared]),
            "蛊惑 must place exactly one hand card once per character turn.");

        const string template = """
            {"schemaVersion":62,"skills":[{"id":"fixture:guhuo","revision":1,
            "minimumRulesVersion": 192,
            "activations":[{"id":"declared","minCards":1,"maxCards":1,"sourceZones":["hand"],
            "minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerAnyTurn":1,
            "effects":[{"op":"usePlacedCardAsDeclared","target":"owner"}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:guhuo":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(template, presentation)
                .Programs["fixture:guhuo"].Activations.Single().Effects.Count == 1,
            "The placed-card declared use must be independently definable.");
        Reject(template.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            presentation, "a non-owner declared use");
        Reject(template.Replace("\"maxCards\":1", "\"maxCards\":2"),
            presentation, "a multi-card placement");
        Reject(template.Replace("\"sourceZones\":[\"hand\"]", "\"sourceZones\":[\"equipment\"]"),
            presentation, "an equipment placement");
        Reject(template.Replace("\"usesPerAnyTurn\":1", "\"usesPerAnyTurn\":0"),
            presentation, "a non-positive per-any-turn limit");
    }

    public static void GuhuoTrueFlipGrantsChanyuanAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!TryReachHumanPlay(game)) continue;
            var placed = FindHandCard(game, CardKind.DrawTwo);
            if (placed is null) continue;
            if (!TryActivateAndDeclare(game, placed.Value, CardKind.DrawTwo)) continue;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Guhuo declaration must pause at an identical checkpoint.");
            var handBefore = game.State.Players[0].HandCount;
            if (!TryDeclareDrawTwo(game)) continue;
            TryDeclareDrawTwo(replay);
            if (!TryDriveToSettled(game) || !TryDriveToSettled(replay)) continue;
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The true flip must replay identically from the paused declaration.");

            var declared = game.Events.Select(item => item.Payload)
                .OfType<ProgramCardDeclaredEvent>().Single(item => item.SkillId == Guhuo);
            Require(declared.OwnerSeat == 0 &&
                    declared.DeclaredKind == CardKind.DrawTwo &&
                    declared.DoubtCandidateSeats.SequenceEqual([1, 2, 3, 4]),
                "Every other living character must be asked to question the declaration.");

            var flipped = game.Events.Select(item => item.Payload)
                .OfType<ProgramGuhuoCardFlippedEvent>().Single(item => item.SkillId == Guhuo);
            Require(flipped.DoubterSeat == 1 && flipped.IsTrue &&
                    flipped.DeclaredKind == CardKind.DrawTwo &&
                    flipped.ActualKind == CardKind.DrawTwo &&
                    flipped.PlacedCardId == placed.Value,
                "The first doubter must flip a true DrawTwo declaration.");

            var acquired = game.Events.Select(item => item.Payload)
                .OfType<SkillsAcquiredEvent>()
                .Where(item => item.SkillIds.Contains(Chanyuan)).ToArray();
            Require(acquired.Length == 1 && acquired[0].PlayerSeat == 1 &&
                    acquired[0].SourceSkillId == Guhuo,
                "A true flip must grant Chanyuan to the doubter once.");
            Require(game.State.Players[1].Skills!.Any(skill => skill.ContentId == Chanyuan),
                "Chanyuan must be an enabled skill of the doubter.");
            Require(game.State.Players[0].HandCount == handBefore - 1 + 2,
                "A true DrawTwo declaration still resolves: the placed card is spent and the owner draws two.");

            var diagnostics = game.CreateCardZoneDiagnostics();
            Require(diagnostics.Single(card => card.CardId == placed.Value).Location is
            { Zone: CardZoneKind.DiscardPile },
                "The placed card must end in the discard pile after the declared use.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a true Guhuo flip.");
    }

    public static void GuhuoFalseFlipVoidsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!TryReachHumanPlay(game)) continue;
            var placed = FindHandCard(game, CardKind.Slash);
            if (placed is null) continue;
            if (!TryActivateAndDeclare(game, placed.Value, CardKind.DrawTwo)) continue;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The false Guhuo declaration must pause at an identical checkpoint.");
            var handBefore = game.State.Players[0].HandCount;
            if (!TryDeclareDrawTwo(game)) continue;
            TryDeclareDrawTwo(replay);
            if (!TryDriveToSettled(game) || !TryDriveToSettled(replay)) continue;
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The false flip must replay identically from the paused declaration.");

            var flipped = game.Events.Select(item => item.Payload)
                .OfType<ProgramGuhuoCardFlippedEvent>().Single(item => item.SkillId == Guhuo);
            Require(flipped.DoubterSeat == 1 && !flipped.IsTrue &&
                    flipped.DeclaredKind == CardKind.DrawTwo &&
                    flipped.ActualKind == CardKind.Slash,
                "Questioning a false declaration must flip a non-DrawTwo card.");

            Require(!game.Events.Select(item => item.Payload)
                    .OfType<SkillsAcquiredEvent>()
                    .Any(item => item.SkillIds.Contains(Chanyuan)),
                "A false flip must not grant Chanyuan.");
            Require(game.State.Players[0].HandCount == handBefore - 1,
                "A voided declaration must not resolve: the owner neither draws nor keeps the placed card.");
            var movements = game.CardMovements
                .Where(item => item.CardId == placed.Value && item.From == CardLocation.Hand(0)).ToArray();
            Require(movements.Length == 1 &&
                    movements[0].To == CardLocation.DiscardPile &&
                    movements[0].Reason.Value.Contains("GuhuoVoid", StringComparison.Ordinal),
                "A voided placed card must go straight from the hidden hand to the discard pile.");
            Require(!game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.UseProgramSkill &&
                        action.ProgramSkillId == Guhuo),
                "Guhuo must be exhausted for this turn after one declaration.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a false Guhuo flip.");
    }

    public static void GuhuoIsOncePerAnyTurnAndReopensNextTurn()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!TryReachHumanPlay(game)) continue;
            var placed = FindHandCard(game, CardKind.DrawTwo);
            if (placed is null) continue;
            if (!TryActivateAndDeclare(game, placed.Value, CardKind.DrawTwo)) continue;
            if (!TryDeclareDrawTwo(game)) continue;
            if (!TryDriveToSettled(game)) continue;

            Require(!game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.UseProgramSkill &&
                        action.ProgramSkillId == Guhuo),
                "Guhuo must stay exhausted for the rest of this character's turn.");

            if (!TryReachHumanPlayNextTurn(game)) continue;
            var nextPlaced = FindHandCard(game, CardKind.DrawTwo);
            if (nextPlaced is null) continue;
            if (!TryActivateAndDeclare(game, nextPlaced.Value, CardKind.DrawTwo)) continue;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            TryDeclareDrawTwo(game);
            TryDeclareDrawTwo(replay);
            if (!TryDriveToSettled(game) || !TryDriveToSettled(replay)) continue;
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The next-turn declaration must replay identically from its paused checkpoint.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a per-turn Guhuo limit flow.");
    }

    public static void ChanyuanHolderSkipsDoubtAndSuppressesOtherSkillsAtOneHp()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 160 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!TryReachHumanPlay(game)) continue;
            var placed = FindHandCard(game, CardKind.DrawTwo);
            var slash = FindHandCard(game, CardKind.Slash);
            if (placed is null || slash is null) continue;
            var bankA = game.CreateSnapshot(0, true).Players.Single(player =>
                player.IsAlive && player.GeneralId == BankGeneral);
            if (bankA.Seat != 1) continue; // 第一位质疑者固定为 1 号位，必须正是天妒持有者。
            if (!TryActivateAndDeclare(game, placed.Value, CardKind.DrawTwo)) continue;
            if (!TryDeclareDrawTwo(game)) continue;
            if (!TryDriveToSettled(game)) continue;
            var firstFlip = game.Events.Select(item => item.Payload)
                .OfType<ProgramGuhuoCardFlippedEvent>().Single(item => item.SkillId == Guhuo);
            Require(firstFlip.DoubterSeat == 1 && firstFlip.IsTrue &&
                    game.State.Players[1].Skills!.Any(skill => skill.ContentId == Chanyuan),
                "The first true flip must grant Chanyuan to the Tiandu holder on seat one.");

            // 下一个于吉回合：缠怨持有者不再进入质疑序列。
            if (!TryReachHumanPlayNextTurn(game)) continue;
            if (game.GetLivePlayer(1) is not { IsAlive: true, Hp: 2 }) continue; // 持有者满体力存活，排除才唯一归于缠怨。
            var nextPlaced = FindHandCard(game, CardKind.DrawTwo);
            if (nextPlaced is null) continue;
            if (!TryActivateAndDeclare(game, nextPlaced.Value, CardKind.DrawTwo)) continue;

            var expectedCandidates = game.State.Players.Where(player =>
                    player.Seat != 0 && player.Seat != 1 && player.IsAlive)
                .Select(player => player.Seat).ToArray();
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            if (!TryDeclareDrawTwo(game)) continue;
            var declaration = game.Events.Select(item => item.Payload)
                .OfType<ProgramCardDeclaredEvent>().OrderBy(item => item.FrameId).Last();
            Require(declaration.DoubtCandidateSeats.SequenceEqual(expectedCandidates),
                "The Chanyuan holder must be excluded from the doubt window.");
            TryDeclareDrawTwo(replay);
            if (!TryDriveToSettled(game) || !TryDriveToSettled(replay)) continue;
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The skip flow must replay identically from the paused declaration.");

            var flipped = game.Events.Select(item => item.Payload)
                .OfType<ProgramGuhuoCardFlippedEvent>().OrderBy(item => item.FrameId).Last();
            Require(flipped.DoubterSeat == expectedCandidates[0] && flipped.IsTrue,
                "The doubt window must pass over the Chanyuan holder to the next character.");
            Require(game.State.Players[expectedCandidates[0]].Skills!.Any(skill => skill.ContentId == Chanyuan),
                "The second true flip must grant Chanyuan to the second doubter.");

            // 体力 1 的缠怨持有者保留缠怨，其余技能（天妒）失效。
            if (!TryPlaySlashAt(game, slash.Value, 1)) continue;
            var wounded = game.State.Players[1];
            Require(wounded.Hp == 1 &&
                    wounded.Skills!.Any(skill => skill.ContentId == Chanyuan),
                "A wounded doubter must keep the doubt-acquired Chanyuan enabled.");
            var index = new MatchSkillBindingIndex(registry.GetSkill, false,
                owner => owner.SkillGrants.EffectiveSkillIds.Any(id =>
                    registry.Skills[id].SuppressionRule is not null));
            var shard = index.GetShard(game.GetLivePlayer(1));
            Require(shard.HasSkill(Chanyuan) && !shard.HasSkill(BankSkill),
                "At one HP the doubt-acquired Chanyuan must suppress the holder's other skills.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Chanyuan skip flow.");
    }

    private static int? FindHandCard(GameEngine game, CardKind kind) =>
        game.State.Players[0].Hand.FirstOrDefault(card => card.Kind == kind)?.Id;

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Yu Ji fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Yu Ji selection failed.");
        return game;
    }

    private static bool TryActivateAndDeclare(GameEngine game, int cardId, CardKind declaredKind)
    {
        try
        {
            var prompt = game.PendingDecision;
            if (prompt is not { Kind: DecisionKind.PlayCard }) return false;
            var result = game.Submit(new UseProgramSkillCommand(
                0, Guhuo, "guhuo-declared-use", [cardId], [], game.Revision, prompt.PromptId));
            if (!result.Accepted) return false;
            var declaration = game.PendingDecision;
            return declaration is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                   declaration.SkillPrompt?.SkillId == Guhuo &&
                   declaration.Choices.Any(choice =>
                       choice.Parameters.GetValueOrDefault("program-action") == "guhuo-declare" &&
                       choice.Parameters.GetValueOrDefault("card-kind") == declaredKind.ToString());
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryDeclareDrawTwo(GameEngine game)
    {
        var declaration = game.PendingDecision;
        if (declaration is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 }) return false;
        var choice = declaration.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "guhuo-declare" &&
            choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.DrawTwo));
        var result = game.Submit(new AnswerPromptCommand(0, declaration.PromptId, choice.Id, game.Revision));
        return result.Accepted;
    }

    private static bool TryPlaySlashAt(GameEngine game, int slashId, int seat)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.State.Status == EngineStatus.Completed) return false;
            if (game.PendingDecision is null)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            break;
        }
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.PlayCard }) return false;
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.CardId == slashId &&
            candidate.TargetSeats.SequenceEqual([seat]));
        if (action is null) return false;
        var result = game.Submit(new PlayCardCommand(
            0, slashId, [seat], game.Revision, prompt.PromptId, action.PlayedCardKind));
        return result.Accepted && TryDriveToSettled(game);
    }

    private static bool TryReachHumanPlay(GameEngine game)
    {
        try
        {
            ReachHumanPlay(game);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReachHumanPlayNextTurn(GameEngine game)
    {
        var endedOnce = false;
        for (var step = 0; step < 900; step++)
        {
            if (game.State.Status == EngineStatus.Completed) return false;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            if (prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.PlayCard)
            {
                if (!endedOnce)
                {
                    var result = game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
                    if (!result.Accepted) return false;
                    endedOnce = true;
                    continue;
                }
                return true;
            }
            if (prompt.PlayerSeat == 0)
            {
                if (!AnswerHumanPassively(game, prompt)) return false;
                continue;
            }
            if (!AdvanceStep(game)) return false;
        }
        return false;
    }

    private static bool TryDriveToSettled(GameEngine game)
    {
        for (var step = 0; step < 600; step++)
        {
            if (game.ResolutionStack.Count == 0 && game.PendingDecision is null) return true;
            if (game.State.Status == EngineStatus.Completed)
                return game.ResolutionStack.Count == 0;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            if (prompt.PlayerSeat == 0)
            {
                if (!AnswerHumanPassively(game, prompt)) return false;
                continue;
            }
            if (!AdvanceStep(game)) return false;
        }
        return false;
    }

    private static bool AnswerHumanPassively(GameEngine game, PendingDecision prompt)
    {
        switch (prompt.Kind)
        {
            case DecisionKind.DiscardCards:
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                return true;
            case DecisionKind.SelectFaction when prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("faction-id") == "qun"):
                Answer(game, prompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("faction-id") == "qun"));
                return true;
            default:
                var choice = PassiveChoice(prompt);
                if (choice is null) return false;
                Answer(game, choice);
                return true;
        }
    }

    private static PromptChoice? PassiveChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
        prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "skip" ||
            item.Parameters.GetValueOrDefault("decision") == "pass") ??
        prompt.Choices.FirstOrDefault();

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.PlayCard) return;
            if (prompt.PlayerSeat == 0 && AnswerHumanPassively(game, prompt)) continue;
            Advance(game);
        }
        throw new InvalidOperationException("The Yu Ji fixture did not reach the human play phase.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Yu Ji skill answer failed.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Yu Ji fixture did not advance.");
    }

    private static bool AdvanceStep(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        return result.Accepted;
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Yu Ji command failed.");
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
            $"Expected the invalid Yu Ji composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("yu-ji-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human Yu Ji. One AI is a two-HP Tiandu holder whose
            // skills must vanish at one HP after it gains Chanyuan; the other
            // banks are inert Slash sources. The deck holds no Dodge, so one
            // declared or played Slash always lands.
            builder.AddGeneral(new ContentGeneralDefinition(BankGeneral, "测试对手一",
                "supporter", BankSkill, "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:yu-ji-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:yu-ji-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:yu-ji-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => index % 3 == 0
                    ? "standard:draw_two"
                    : "standard:slash")
                .Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:yu-ji-deck", "于吉测试牌堆", 4, 2, [])
            {
                PhysicalCards = cards
            });
            builder.AddMode(new ContentModeDefinition(Mode, "于吉测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:yu-ji-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    BankGeneral,
                    "fixture:yu-ji-bank-b",
                    "fixture:yu-ji-bank-c",
                    "fixture:yu-ji-bank-d"]));
        }
    }
}
