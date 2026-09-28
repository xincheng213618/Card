using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaoPiChecks
{
    private const string General = "classic:cao-pi";
    private const string Xingshang = "classic:xingshang";
    private const string Fangzhu = "classic:fangzhu";
    private const string Mode = "identity:classic-cao-pi-check-5";
    private const string ClaimReasonFragment = "claim-death-cleanup";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([Xingshang, Fangzhu]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2010 Cao Pi must be in the current Wei roster with three HP.");

        var xingshang = current.Skills[Xingshang].Program!;
        var deathTrigger = xingshang.Triggers.Single(item => item.Id == "claim-died-cards");
        Require(deathTrigger.Window == SkillProgramTriggerWindow.CharacterDied &&
                deathTrigger.Optional &&
                deathTrigger.Condition.Kind == SkillProgramTriggerConditionKind.DeathVictimHasCards &&
                deathTrigger.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.ClaimDeathCleanupCards]),
            "Xingshang must offer an optional claim on characterDied only when the victim still owns cards.");

        var fangzhu = current.Skills[Fangzhu].Program!;
        var exileTrigger = fangzhu.Triggers.Single(item => item.Id == "injury-exile");
        Require(exileTrigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                exileTrigger.Optional &&
                exileTrigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                exileTrigger.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.TurnOver,
                    SkillProgramEffectOp.Draw]),
            "Fangzhu must chain target selection, flip and a draw after each damage.");
        var draw = exileTrigger.Effects.Single(item => item.Op == SkillProgramEffectOp.Draw);
        Require(draw.Target == SkillProgramEffectTarget.SelectedTarget &&
                draw.NumberExpression == SkillProgramNumberExpression.OwnerLostHp,
            "The exile draw must move ownerLostHp cards to the selected target.");

        const string claimTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:claim","revision":1,
            "minimumRulesVersion": 179,
            "triggers":[{"id":"claim","window":"characterDied","subject":"owner","optional":true,
            "condition":{"kind":"deathVictimHasCards"},
            "effects":[{"op":"claimDeathCleanupCards","target":"owner"}]}]}]}
            """;
        const string claimPresentation = """
            {"schemaVersion":3,"skills":{"fixture:claim":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(claimTemplate, claimPresentation)
                .Programs["fixture:claim"].Triggers.Single().Effects.Count == 1,
            "A characterDied death-claim trigger must be independently definable.");
        Reject(claimTemplate.Replace("\"window\":\"characterDied\"", "\"window\":\"afterDamageApplied\""),
            claimPresentation, "claimDeathCleanupCards outside characterDied");
        Reject(claimTemplate.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            claimPresentation, "claimDeathCleanupCards requires the owner target");
        Reject(claimTemplate.Replace("\"window\":\"characterDied\"", "\"window\":\"ownerDied\""),
            claimPresentation, "deathVictimHasCards requires a characterDied trigger");
    }

    public static void XingShangClaimsDiedPlayerCardsAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var victim = ClaimTarget(game);
            if (victim < 0 ||
                game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Slash && action.TargetSeat == victim) is not { } slash)
                continue;
            Play(game, slash);
            DriveUntil(game, () => IsXingshangPromptFor(game, victim));
            if (game.State.Status == EngineStatus.Completed ||
                !IsXingshangPromptFor(game, victim))
                continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The dying cleanup must pause at an identical Xingshang checkpoint.");
            Activate(game, Xingshang);
            Activate(replay, Xingshang);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Xing Shang claim must replay identically from the paused checkpoint.");
            var deathCleanups = game.CardMovements.Where(item =>
                    item.To == CardLocation.DiscardPile &&
                    item.From.OwnerSeat == victim &&
                    item.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment &&
                    item.Reason.Value.Contains("death", StringComparison.Ordinal))
                .Select(item => item.CardId)
                .Distinct()
                .ToArray();
            var claims = game.CardMovements.Where(item =>
                    item.Reason.Value.Contains(ClaimReasonFragment, StringComparison.Ordinal))
                .ToArray();
            Require(deathCleanups.Length > 0 &&
                    claims.Length == deathCleanups.Length &&
                    claims.Select(item => item.CardId).OrderBy(id => id)
                        .SequenceEqual(deathCleanups.OrderBy(id => id)) &&
                    claims.All(item => item.From == CardLocation.DiscardPile &&
                        item.To == CardLocation.Hand(0)),
                "Xing Shang must claim exactly the cards the victim lost at death, from the discard pile.");
            var diagnostics = game.CreateCardZoneDiagnostics();
            Require(deathCleanups.All(item =>
                    diagnostics.Single(card => card.CardId == item).Location is
                        { Zone: CardZoneKind.Hand, OwnerSeat: 0 }),
                "Every claimed card must end up in Cao Pi's hand.");
            Require(game.Events.Select(item => item.Payload).Any(item => item is
                    ProgramBindingStartedEvent { SkillId: Xingshang }),
                "The claim must publish the Xing Shang binding event.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Xing Shang claim.");
    }

    public static void XingShangSkipKeepsVictimCardsInDiscard()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var victim = ClaimTarget(game);
            if (victim < 0 ||
                game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Slash && action.TargetSeat == victim) is not { } slash)
                continue;
            Play(game, slash);
            DriveUntil(game, () => IsXingshangPromptFor(game, victim));
            if (game.State.Status == EngineStatus.Completed ||
                !IsXingshangPromptFor(game, victim))
                continue;
            var prompt = game.PendingDecision!;
            var skip = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip");
            Answer(game, skip);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            var deathCleanups = game.CardMovements.Where(item =>
                    item.To == CardLocation.DiscardPile &&
                    item.From.OwnerSeat == victim &&
                    item.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment &&
                    item.Reason.Value.Contains("death", StringComparison.Ordinal))
                .Select(item => item.CardId)
                .Distinct()
                .ToArray();
            Require(deathCleanups.Length > 0,
                "The fixture victim must lose cards at death for the decline check.");
            var diagnostics = game.CreateCardZoneDiagnostics();
            Require(deathCleanups.All(item =>
                    diagnostics.Single(card => card.CardId == item).Location.Zone == CardZoneKind.DiscardPile),
                "Declining Xing Shang must leave every victim card in the discard pile.");
            Require(game.CardMovements.All(item =>
                    !item.Reason.Value.Contains(ClaimReasonFragment, StringComparison.Ordinal)),
                "Declining Xing Shang must not produce a claim movement.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a declined Xing Shang prompt.");
    }

    private static bool IsXingshangPromptFor(GameEngine game, int victimSeat) =>
        IsProgramPrompt(game, Xingshang) && game.PendingDecision!.TargetSeat == victimSeat;

    public static void FangZhuFlipsTargetAndDrawsOwnerLostHp()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            DriveUntil(game, () => IsProgramPrompt(game, Fangzhu),
                stopAtSkills: [Fangzhu]);
            if (game.State.Status == EngineStatus.Completed ||
                !IsProgramPrompt(game, Fangzhu))
                continue;
            var self = game.CreateSnapshot(0, true).Players[0];
            var lostHp = self.MaxHp - self.Hp;
            Require(lostHp > 0, "The Fangzhu prompt must follow a real damage.");
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Activate(game, Fangzhu);
            Activate(replay, Fangzhu);
            var targetSeat = SelectLowestTarget(game);
            SelectLowestTarget(replay);
            Require(targetSeat >= 0, "The Fangzhu target prompt lost its candidates.");
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Fangzhu exile must replay identically from the paused checkpoint.");
            var target = game.CreateSnapshot(0, true).Players[targetSeat];
            Require(target.IsFaceDown,
                "The exiled target's general card must be turned face-down.");
            var drawn = game.CardMovements.Where(item =>
                    item.To == CardLocation.Hand(targetSeat) &&
                    item.Reason.Value.Contains("classic:fangzhu", StringComparison.Ordinal))
                .ToArray();
            Require(drawn.Length == lostHp,
                $"The exiled target must draw exactly the owner's lost HP ({lostHp}).");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Fangzhu exile.");
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static int ClaimTarget(GameEngine game)
    {
        var snapshot = game.CreateSnapshot(0, true);
        var victim = snapshot.Players
            .Where(item => item.Seat != 0 && item.IsAlive && item.MaxHp == 1)
            .Select(item => (int?)item.Seat).FirstOrDefault() ?? -1;
        return victim;
    }

    private static int SelectLowestTarget(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "No pending decision for the Fangzhu target selection.");
        var choice = prompt.Choices
            .Where(item => item.Parameters.GetValueOrDefault("program-action") == "select-target")
            .OrderBy(item => item.Targets[0])
            .FirstOrDefault() ?? throw new InvalidOperationException(
            "The Fangzhu target prompt lost its select-target choices.");
        Answer(game, choice);
        return choice.Targets[0];
    }

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        int budget = 500)
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
                        item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
                        prompt.Choices.FirstOrDefault(item =>
                            item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static void Activate(GameEngine game, string skillId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            $"No pending decision to activate {skillId}.");
        Require(prompt.Kind == DecisionKind.ProgramTrigger,
            $"The {skillId} prompt must be a program trigger.");
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            throw new InvalidOperationException($"The {skillId} prompt lost its activate choice.");
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            activate.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"{skillId} activation failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cao Pi skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Cao Pi fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Cao Pi selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Cao Pi fixture did not reach Play.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cao Pi fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Cao Pi card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Cao Pi command failed.");
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
            $"Expected invalid Cao Pi composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cao-pi-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 149, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1 and 4 are one-hit victims whose
            // deaths open the Xingshang window, seats 2 and 3 are healthy slash banks.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-pi-victim-a", "测试受击一",
                "supporter", "standard:none", "qun", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-pi-bystander-b", "测试目标二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-pi-bystander-c", "测试目标三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cao-pi-victim-d", "测试受击四",
                "supporter", "standard:none", "qun", BaseHp: 1));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 4) switch
            {
                1 => "standard:duel",
                2 => "standard:dodge",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:cao-pi-deck", "曹丕测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "曹丕测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:cao-pi-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General, "fixture:cao-pi-victim-a", "fixture:cao-pi-bystander-b",
                    "fixture:cao-pi-bystander-c", "fixture:cao-pi-victim-d"]));
        }
    }
}
