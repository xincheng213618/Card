using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class QinglongCrescentBladeChecks
{
    public static void SameTargetFollowupAndReplay()
    {
        var boundary = QinglongCrescentBladeScenario.FindHumanTrigger();
        var registry = boundary.Registry;
        var game = boundary.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Qinglong fixture lost its private trigger prompt.");
        var full = game.CreateSnapshot(0, revealAll: true);
        var source = full.Players[0];
        var expectedCandidates = source.Hand
            .Where(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .Select(card => card.Id)
            .ToArray();
        var slashChoices = prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-slash").ToArray();

        Require(prompt.IsPrivate &&
                game.CreateSnapshot(boundary.TargetSeat).PendingDecision is null &&
                prompt.ValidTargetSeats.SequenceEqual([boundary.TargetSeat]) &&
                prompt.ValidCardIds.SequenceEqual(expectedCandidates) &&
                slashChoices.SelectMany(choice => choice.Cards).SequenceEqual(expectedCandidates) &&
                slashChoices.All(choice =>
                    choice.Cards.Count == 1 &&
                    choice.Targets.SequenceEqual([boundary.TargetSeat])),
            "Qinglong must publish exact available Slashes for only the original target and source.");

        var pausedCheckpoint = RoundTrip(game.CreateCheckpoint());
        var paused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
            "An in-flight Qinglong follow-up prompt must restore exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skip = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-skip");
        var targetHp = skipped.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == boundary.TargetSeat).Hp;
        var skipResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skip.Id,
            skipped.Revision));
        Require(skipResult.Accepted &&
                skipped.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == boundary.TargetSeat).Hp == targetHp &&
                skipped.Events.Select(item => item.Payload)
                    .OfType<QinglongCrescentBladeResolvedEvent>()
                    .Any(resolved => !resolved.Used && resolved.SlashCardIds.Count == 0),
            skipResult.Error?.Message ??
            "Skipping Qinglong must preserve the successful Dodge without damage.");

        var used = GameReplay.Restore(pausedCheckpoint, registry);
        var usePrompt = used.PendingDecision!;
        var use = usePrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        var previousSlashUses = used.Events.Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Count(item => item.SourceSeat == 0 && item.TargetSeat == boundary.TargetSeat);
        var useResult = used.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            use.Id,
            used.Revision));
        Require(useResult.Accepted, useResult.Error?.Message ??
            "The exact Qinglong follow-up Slash was rejected.");
        var resolved = used.Events.Select(item => item.Payload)
            .OfType<QinglongCrescentBladeResolvedEvent>()
            .LastOrDefault();
        var followupUses = used.Events.Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Where(item => item.SourceSeat == 0 && item.TargetSeat == boundary.TargetSeat)
            .ToArray();
        Require(resolved is { Used: true } &&
                resolved.SlashCardIds.SequenceEqual(use.Cards) &&
                resolved.TargetSeat == boundary.TargetSeat &&
                followupUses.Length == previousSlashUses + 1 &&
                followupUses[^1].CardId == use.Cards.Single() &&
                used.CardMovements.Any(movement =>
                    movement.CardId == use.Cards.Single() &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
            "Qinglong must finish the canceled Slash and open a real new Slash against the same target.");

        var replayed = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Require(State(replayed) == State(used) && Events(replayed).SequenceEqual(Events(used)),
            "A paused Qinglong follow-up Slash must replay exactly.");

        UnselectedCompletedGiftSuspendsForQinglongAndReturnsCold();

    }



    private const string GiftMode = "identity:classic-qinglong-completed-gift";
    private const string GiftDriver = "fixture:qinglong-gift-driver";

    private static void UnselectedCompletedGiftSuspendsForQinglongAndReturnsCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new CompletedGiftQinglongFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = GiftMode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        GiftAccept(game, new StartGameCommand()); GiftReach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        GiftAccept(game, new SelectGeneralCommand(0, "fixture:qinglong-gift-owner", game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        GiftAccept(game, new UseProgramSkillCommand(0, GiftDriver, "draw", [], [], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        GiftAccept(game, new UseProgramSkillCommand(0, GiftDriver, "draw-target", [], [1], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var weapon = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.QinglongCrescentBlade).Id;
        GiftAccept(game, new PlayCardCommand(0, weapon, [], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]) &&
            game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == a.CardId && c.Kind == CardKind.Slash && c.Suit == Suit.Heart));
        GiftAccept(game, new PlayCardCommand(0, slash.CardId!.Value, [1], game.Revision, GiftPrompt(game)!.PromptId));
        GiftReach(game, p => p.Kind == DecisionKind.QinglongCrescentBlade && p.PlayerSeat == 0);
        var firstUse = game.ResolutionStack.OfType<CardUseFrame>().Last(f => f.SourceSeat == 0 && f.CardId == slash.CardId);
        GiftCold(game, registry);
        var followupChoice = GiftPrompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash");
        var followupCard = followupChoice.Cards.Single();
        GiftAccept(game, new AnswerPromptCommand(0, GiftPrompt(game)!.PromptId, followupChoice.Id, game.Revision));
        var original = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == firstUse.Id);
        var gift = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f =>
            f.SkillId == "classic:zhongyong" && f.WindowContext?.CardUse?.ParentCardUseFrameId == original.Id);
        var saved = original.Continuations.QinglongFollowup?.Decision;
        var child = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == followupCard && f.SourceSeat == 0);
        Require(gift.CompletedCardGiftDraft is { RecipientSeat: null, GiftWasRed: false } && gift.SelectedTargetSeats.Count == 0 &&
            original.Continuations.QinglongFollowup is { Active: true, NextAttackOwnerId: var oldId } && oldId == original.Id &&
            saved is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true } && saved.SkillPrompt?.SkillId == gift.SkillId &&
            saved.Choices.All(c => c.Parameters.GetValueOrDefault("frame-id") == gift.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
            child.Action is { Type: CardActionType.Use, ActorSeat: 0 } action && action.ParentActionId == original.Action!.ActionId &&
            child.TargetSeats.SequenceEqual([1]) && child.Action.PhysicalCards.Single().CardId == followupCard &&
            game.CardMovements.Count(m => m.CardId == followupCard && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
            "The real original owner's follow-up Slash suspends precisely the unselected Zhongyong gift, with one exact physical payment.");
        for (var seat = 1; seat < 4; seat++) Require(game.CreateSnapshot(seat).Players[0].Hand.Count == 0,
            "The saved private giver choices and physical hand remain hidden in every foreign view.");
        GiftCold(game, registry);
        // The nested Slash may itself be dodged and complete Zhongyong. Skip its
        // additional Qinglong/gift offers using real commands, retaining the old
        // saved give prompt until this exact original Program returns.
        GiftReach(game, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == "classic:zhongyong" &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("frame-id") == gift.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var restored = GiftPrompt(game)!;
        Require(restored.PromptId == saved!.PromptId && restored.PlayerSeat == saved.PlayerSeat && restored.IsPrivate &&
            restored.Choices.Select(c => c.Id).SequenceEqual(saved.Choices.Select(c => c.Id)) &&
            game.ResolutionStack.Last() is ProgramSkillFrame returned && returned.Id == gift.Id &&
            returned.CompletedCardGiftDraft?.RecipientSeat is null &&
            game.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == child.Id) == 1 &&
            game.CardMovements.Count(m => m.CardId == followupCard && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
            "Only after the real child finishes does the identical private unselected giver prompt resume, without repaying its Slash.");
        GiftCold(game, registry);
        var give = restored.Choices.First(c => c.Parameters.GetValueOrDefault("gift-option") == "give" &&
            c.Targets.SequenceEqual([3]) && c.Cards.Contains(firstUse.CardId));
        var gifted = give.Cards.ToArray();
        GiftAccept(game, new AnswerPromptCommand(0, restored.PromptId, give.Id, game.Revision));
        Require(game.Events.Select(e => e.Payload).OfType<CompletedCardGiftedEvent>().Single(e => e.FrameId == gift.Id) is { RecipientSeat: 3 } fact &&
            fact.CardActionId == original.Action!.ActionId && fact.CardIds.SequenceEqual(gifted) &&
            gifted.All(id => game.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Hand(3) &&
                m.Reason.Value == "skill-program.classic:zhongyong.completed-card-gift") == 1),
            "The resumed real human giver selects a recipient and transfers only the original frozen Slash once.");
        GiftCold(game, registry);
        GiftReach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var recipientSlash = game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Single(e =>
            e.SourceSeat == 3 && e.CardId == firstUse.CardId && e.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
        Require(game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Count(e =>
                e.Action.Type == CardActionType.Use && e.Action.ActorSeat == 3 && e.Action.ParentActionId == original.Action!.ActionId &&
                e.Action.PhysicalCards.Any(cost => cost.CardId == firstUse.CardId && cost.From == CardLocation.Hand(3))) == 1 &&
            game.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == recipientSlash.ResolutionId) == 1 &&
            game.CardMovements.Count(m => m.CardId == firstUse.CardId && m.From == CardLocation.Hand(3) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
            "The fixed native recipient must actually pay and complete the gifted original red Slash once through its exact original parent.");
        Require(game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Count(e => e.FrameId == gift.Id && e.Completed) == 1 &&
            game.CardMovements.Count(m => m.CardId == followupCard && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            !game.CreateCardZoneDiagnostics().Any(c => c.Location == CardLocation.Processing),
            "Native recipient choices return through the original completed use and leave no duplicated cost or Processing entity.");
        GiftCold(game, registry);
    }

    private static PendingDecision? GiftPrompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat))
        .Select(snapshot => snapshot.PendingDecision).FirstOrDefault(p => p is not null);
    private static void GiftReach(GameEngine game, Func<PendingDecision, bool> reached)
    {
        for (var step = 0; step < 100; step++)
        {
            var prompt = GiftPrompt(game); if (prompt is not null && reached(prompt)) return;
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade })
                GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip").Id, game.Revision));
            else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("gift-option") == "decline"))
                GiftAccept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("gift-option") == "decline").Id, game.Revision));
            else GiftAccept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed Zhongyong/Qinglong command fixture did not reach its exact boundary: " + JsonSerializer.Serialize(GiftPrompt(game)));
    }
    private static void GiftAccept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real Qinglong/gift command."); }
    private static string GiftState(GameEngine game) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Facts = Events(game), game.CardMovements,
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void GiftCold(GameEngine game, ContentRegistry registry) => Require(GiftState(game) == GiftState(GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry)),
        "Cold accepted-command replay preserves all four private views, the exact suspended prompt, typed owning frames, facts and one-time costs.");

    private sealed class CompletedGiftQinglongFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-qinglong-completed-gift", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardClassicGeneralPackage).Assembly;
            string Resource(string name) { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal)))!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Resource("classic-zhou-cang.rules.json"), Resource("classic-zhou-cang.presentation.json"));
            builder.AddSkill(new("classic:zhongyong", "忠勇", "已有经典忠勇真实程序") { Program = actual.Programs["classic:zhongyong"], ProgramPresentation = actual.Presentations["classic:zhongyong"] });
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{GiftDriver}}","revision":1,"activations":[
                 {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                 {"id":"draw-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":20}]}]},
                {"id":"fixture:qinglong-gift-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object> {
                    [GiftDriver] = new { name = "实际摸牌", description = "小牌组中准备真实杀与闪" },
                    ["fixture:qinglong-gift-quiet"] = new { name = "安静回合", description = "正常摸牌后跳过出牌" } } }));
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new(id, id, "机制夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            builder.AddSkill(new("fixture:qinglong-gift-selection", "固定目标", "无运行程序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:qinglong-gift-owner", "忠勇青龙拥有者", "supporter", "classic:zhongyong", "shu", 4, [GiftDriver]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:qinglong-gift-target-{seat}", "固定目标", "supporter", "fixture:qinglong-gift-selection", "wei", 8, ["fixture:qinglong-gift-quiet"]));
            builder.AddCard(new("fixture:qinglong-gift-weapon", "青龙偃月刀", "装备牌", "已有经典武器实际能力", CardKind.QinglongCrescentBlade));
            builder.AddDeck(new("fixture:qinglong-gift-deck", "小实体牌组", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i =>
                new ContentDeckPhysicalCard(i % 3 == 0 ? "fixture:qinglong-gift-weapon" : i % 3 == 1 ? "standard:slash" : "standard:dodge",
                    i % 3 == 1 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(GiftMode, "已有忠勇青龙交叉", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:qinglong-gift-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:qinglong-gift-owner", "fixture:qinglong-gift-target-1", "fixture:qinglong-gift-target-2", "fixture:qinglong-gift-target-3"]));
        }
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
