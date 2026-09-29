using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangZhaoZhangHongChecks
{
    private const string General = "classic:zhang-zhao-zhang-hong";
    private const string Zhijian = "classic:zhijian";
    private const string Guzheng = "classic:guzheng";
    private const string Mode = "identity:classic-zhao-zhang-hong-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "wu" } general &&
                general.SkillIds.SequenceEqual([Zhijian, Guzheng]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "Zhang Zhao & Zhang Hong must join the Wu roster with three HP.");

        var zhijian = current.Skills[Zhijian].Program!;
        var activation = zhijian.Activations.Single();
        Require(activation.UsesPerTurn is null && activation.MinCards == 0 && activation.MaxCards == 0 &&
                activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                activation.TargetKind == SkillProgramTargetKind.OtherLiving &&
                activation.MinTargets == 1 && activation.MaxTargets == 1 &&
                activation.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.Draw]),
            "Zhijian must be an unlimited active skill offering one hand card to a selected other player.");
        var move = activation.Effects[0];
        Require(move.ChooserRef!.Kind == ProgramParticipantRef.Owner &&
                move.CardOwnerRef!.Kind == ProgramParticipantRef.Owner &&
                move.Zones.SequenceEqual([CardZoneKind.Hand]) &&
                move.CardCategories!.SequenceEqual([SkillProgramCardCategory.Equipment]) &&
                move.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone &&
                move.TargetReference!.Kind == ProgramParticipantRef.SelectedTarget &&
                move.ProhibitReplacingEquipment,
            "Zhijian must hand one own hand equipment card to the selected target without replacing equipment.");
        Require(activation.Effects[1] is { Target: SkillProgramEffectTarget.Owner, Amount: 1 } draw &&
                draw.Condition.Kind == SkillProgramConditionKind.Always,
            "Zhijian must draw exactly one card unconditionally after the hand-off.");

        var guzheng = current.Skills[Guzheng].Program!;
        var trigger = guzheng.Triggers.Single();
        Require(trigger.Window == SkillProgramTriggerWindow.DiscardPhaseEnded &&
                trigger.Subject == SkillProgramTriggerSubject.Owner &&
                trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
                trigger.Optional &&
                trigger.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.RestorePhaseHandDiscards]),
            "Guzheng must observe other living players' discard-phase endings.");
        var restore = trigger.Effects[0];
        Require(restore.ChooserRef!.Kind == ProgramParticipantRef.Owner &&
                restore.SourceRef!.Kind == ProgramParticipantRef.EventSource,
            "Guzheng must restore cards to the discard-phase owner supplied by the window.");

        const string activationTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:zhijian-template","revision":1,
            "minimumRulesVersion": 187,
            "activations":[{"id":"equip-template","minCards":0,"maxCards":0,"sourceZones":["hand"],
            "minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
            "condition":{"kind":"always"},
            "effects":[
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},
            "cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,
            "cardCategories":["equipment"],
            "destination":"selectedTargetCorrespondingZone",
            "targetRef":{"kind":"selectedTarget"},"prohibitReplacingEquipment":true},
            {"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string skillPresentation = """
            {"schemaVersion":3,"skills":{"fixture:zhijian-template":{"name":"测试","description":"测试"}}}
            """;
        Load(activationTemplate, skillPresentation);
        Reject(activationTemplate.Replace(",\"prohibitReplacingEquipment\":true", ""),
            skillPresentation, "hand-source corresponding-zone moves must set prohibitReplacingEquipment");
        Reject(activationTemplate.Replace("\"zones\":[\"hand\"]", "\"zones\":[\"equipment\"]"),
            skillPresentation, "prohibitReplacingEquipment requires a single hand source");
        Reject(activationTemplate.Replace("\"cardCategories\":[\"equipment\"]", "\"cardCategories\":[\"basic\"]"),
            skillPresentation, "prohibitReplacingEquipment requires an equipment category");
        Reject(activationTemplate.Replace("\"targetRef\":{\"kind\":\"selectedTarget\"",
                "\"targetRef\":{\"kind\":\"owner\""),
            skillPresentation, "prohibitReplacingEquipment requires a selected target");

        const string guzhengTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:guzheng-template","revision":1,
            "minimumRulesVersion": 187,
            "triggers":[{"id":"restore-template","window":"WINDOW","subject":"owner",
            "turnOwnerScope":"otherLiving","optional":true,"priority":0,
            "condition":{"kind":"always"},
            "effects":[{"op":"restorePhaseHandDiscards","target":"owner",
            "chooserRef":{"kind":"owner"},"phaseOwnerRef":{"kind":"REF"}}]}]}]}
            """;
        Load(guzhengTemplate.Replace("WINDOW", "discardPhaseEnded").Replace("REF", "eventSource"),
            skillPresentation.Replace("fixture:zhijian-template", "fixture:guzheng-template"));
        Reject(guzhengTemplate.Replace("WINDOW", "discardPhaseEnded").Replace("REF", "owner"),
            skillPresentation.Replace("fixture:zhijian-template", "fixture:guzheng-template"),
            "the phase owner must be the lifecycle eventSource");
        Reject(guzhengTemplate.Replace("WINDOW", "drawPhaseStarting").Replace("REF", "eventSource"),
            skillPresentation.Replace("fixture:zhijian-template", "fixture:guzheng-template"),
            "turnOwnerScope requires a turnEnding, playEnding, playPhaseStarting or discardPhaseEnded trigger");
    }

    public static void ZhijianEquipsFreeSlotAndDrawsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 300 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!DriveToFirstPlay(game)) continue;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var equipmentCard = hand.FirstOrDefault(card => EquipmentCatalog.IsEquipment(card.Kind));
            var targetSeat = NextLivingSeat(game, 0);
            var target = game.CreateSnapshot(0, true).Players[targetSeat];
            if (equipmentCard is null || target.Equipment!.Count >= 5) continue;
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            var activation = game.Submit(new UseProgramSkillCommand(0, Zhijian, "equip-target",
                [], [targetSeat], game.Revision, game.PendingDecision!.PromptId));
            if (!activation.Accepted) continue;
            var replayActivation = replay.Submit(new UseProgramSkillCommand(0, Zhijian, "equip-target",
                [], [targetSeat], replay.Revision, replay.PendingDecision!.PromptId));
            Require(replayActivation.Accepted, replayActivation.Error?.Message ?? "Replay activation failed.");
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt ||
                prompt.SkillPrompt?.SkillId != Zhijian)
            {
                continue;
            }
            var choice = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
                item.Cards.SequenceEqual([equipmentCard.Id]));
            if (choice is null) continue;
            Answer(game, choice);
            var replayPrompt = replay.PendingDecision;
            var replayChoice = replayPrompt!.Choices.Single(item => item.Id == choice.Id);
            Answer(replay, replayChoice);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Zhijian hand-off must replay identically from the paused checkpoint.");

            var handedOff = game.CardMovements.Where(item =>
                item.Reason.Value.Contains(Zhijian, StringComparison.Ordinal) &&
                item.From == CardLocation.Hand(0) && item.CardId == equipmentCard.Id).ToArray();
            Require(handedOff.Length == 1 && handedOff[0].To == CardLocation.Equipment(targetSeat),
                "Zhijian must move exactly the chosen hand equipment card into the target's equipment zone.");
            var drawn = game.CardMovements.Where(item =>
                item.To == CardLocation.Hand(0) && item.From == CardLocation.DrawPile &&
                item.Reason.Value.Contains(Zhijian, StringComparison.Ordinal)).ToArray();
            Require(drawn.Length == 1,
                "Zhijian must draw exactly one card after the hand-off.");
            Require(game.CreateSnapshot(0, true).Players[targetSeat]
                    .Equipment!.Any(card => card.Id == equipmentCard.Id),
                "The handed-off equipment card must stay in the target's equipment zone.");
            completed++;
        }
        Require(completed == 1, "No seeded setup exercised the Zhijian hand-off.");
    }

    public static void GuzhengReturnsOneAndTakesRestAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 300 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            DriveUntil(game, () => false, stopAtSkills: [Guzheng]);
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt ||
                prompt.SkillPrompt?.SkillId != Guzheng)
            {
                continue;
            }
            var activate = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "activate");
            if (activate is null) continue;
            Answer(game, activate);
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } givePrompt)
            {
                continue;
            }
            var giveChoices = givePrompt.Choices.Where(item =>
                item.Parameters.GetValueOrDefault("program-action") == "restore-phase-hand-discard").ToArray();
            if (giveChoices.Length < 2) continue;
            var phaseOwnerSeat = int.Parse(giveChoices[0].Parameters["phase-owner-seat"]);
            var givenCardId = giveChoices[0].Cards[0];
            var restCardIds = giveChoices.Skip(1).Select(item => item.Cards[0]).ToArray();
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            Answer(game, giveChoices[0]);
            var replayChoice = replay.PendingDecision!.Choices.Single(item => item.Id == giveChoices[0].Id);
            Answer(replay, replayChoice);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Guzheng trade must replay identically from the paused checkpoint.");

            var restoreReason = "skill-program.classic:guzheng.RestorePhaseHandDiscards";
            var returned = game.CardMovements.Where(item =>
                item.CardId == givenCardId && item.From == CardLocation.DiscardPile &&
                item.To == CardLocation.Hand(phaseOwnerSeat) &&
                item.Reason.Value == restoreReason).ToArray();
            Require(returned.Length == 1,
                "Guzheng must return exactly the chosen discard to the phase owner.");
            var taken = restCardIds.Select(id => game.CardMovements.Count(item =>
                    item.CardId == id && item.From == CardLocation.DiscardPile &&
                    item.To == CardLocation.Hand(0) &&
                    item.Reason.Value == restoreReason)).ToArray();
            Require(taken.All(count => count == 1),
                "Guzheng must take every other discarded card from that phase into the owner's hand.");
            Require(game.CreateSnapshot(0, true).Players[0].Hand
                    .Any(card => restCardIds.Contains(card.Id)),
                "The taken cards must remain in the owner's hand.");
            completed++;
        }
        Require(completed == 1, "No seeded setup exercised the Guzheng trade.");
    }

    private static bool DriveToFirstPlay(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
        return game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    }

    private static int NextLivingSeat(GameEngine game, int seat)
    {
        var players = game.CreateSnapshot(0, true).Players;
        for (var offset = 1; offset <= players.Count; offset++)
        {
            var candidate = (seat + offset) % players.Count;
            if (players[candidate].IsAlive) return candidate;
        }
        throw new InvalidOperationException("The fixture has no living neighbor.");
    }

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
                        item.Parameters.GetValueOrDefault("program-action") is "skip" or
                            "restore-phase-hand-decline") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static void Load(string rules, string presentation) =>
        _ = SkillProgramCatalog.Load(rules, presentation);

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
            $"Expected invalid Zhang Zhao Zhang Hong composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong command failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong skill answer failed.");
    }

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
            MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Zhang Zhao Zhang Hong fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Zhang Zhao Zhang Hong selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong fixture did not advance.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("zhang-zhao-zhang-hong-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 158, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhao-hong-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhao-hong-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhao-hong-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhao-hong-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 2));
            var cards = Enumerable.Range(0, 240).Select(index => (index % 6) switch
            {
                0 => "standard:slash",
                1 => "standard:dodge",
                2 => "standard:crossbow",
                3 => "standard:dodge",
                4 => "standard:dodge",
                _ => "standard:dodge"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:zhao-hong-deck", "张昭张纮测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "张昭张纮测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:zhao-hong-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:zhao-hong-bank-a",
                    "fixture:zhao-hong-bank-b",
                    "fixture:zhao-hong-bank-c",
                    "fixture:zhao-hong-bank-d"]));
        }
    }
}
