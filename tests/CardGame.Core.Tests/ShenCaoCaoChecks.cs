using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenCaoCaoChecks
{
    private const string General = "classic:shen-cao-cao";
    private const string Guixin = "classic:guixin";
    private const string Feiying = "classic:feiying";
    private const string Mode = "identity:classic-shen-cao-cao-check-5";
    private const string ReasonFragment = "classic:guixin";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "god" } general &&
                general.SkillIds.SequenceEqual([Guixin, Feiying]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "神曹操 must be a three-HP god general in the current identity pools.");

        var guixin = current.Skills[Guixin].Program!;
        var trigger = guixin.Triggers.Single();
        Require(trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                trigger.Subject == SkillProgramTriggerSubject.Owner &&
                trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint &&
                trigger.Optional &&
                trigger.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.TakeRandomCardFromEveryOtherCharacter,
                    SkillProgramEffectOp.TurnOver]),
            "Guixin must chain one random claim per damage point with the owner turn-over.");
        var claim = trigger.Effects[0];
        Require(claim.Target == SkillProgramEffectTarget.Owner &&
                claim.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]),
            "Guixin must draw one random card from every declared area of the other characters.");
        Require(trigger.Effects[1].Target == SkillProgramEffectTarget.Owner,
            "Guixin must turn over its owner rather than the damaged source.");

        var feiying = current.Skills[Feiying].Program!;
        var modifier = feiying.Modifiers.Single();
        Require(modifier.Query == SkillRuleQuery.IncomingDistance &&
                modifier.Operation == SkillRuleOperation.Add &&
                modifier.Value == 1 && modifier.Priority == 0 &&
                modifier.Condition.Kind == SkillProgramConditionKind.Always,
            "Feiying must add one to every other character's distance to its owner.");

        const string template = """
            {"schemaVersion":62,"skills":[{"id":"fixture:guixin","revision":1,
            "minimumRulesVersion": 188,
            "triggers":[{"id":"claim","window":"afterDamageApplied","subject":"owner",
            "damageOccurrence":"perDamagePoint","optional":true,
            "effects":[{"op":"takeRandomCardFromEveryOtherCharacter","target":"owner",
            "zones":["hand","equipment","judgment"]}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:guixin":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(template, presentation)
                .Programs["fixture:guixin"].Triggers.Single().Effects.Count == 1,
            "The every-other-character claim must be independently definable.");
        Reject(template.Replace("\"zones\":[\"hand\",\"equipment\",\"judgment\"]", "\"zones\":[]"),
            presentation, "an empty area list");
        Reject(template.Replace("\"zones\":[\"hand\",\"equipment\",\"judgment\"]",
                "\"zones\":[\"hand\",\"drawPile\"]"),
            presentation, "an unsupported area");
        Reject(template.Replace("\"zones\":[\"hand\",\"equipment\",\"judgment\"]",
                "\"zones\":[\"hand\",\"hand\"]"),
            presentation, "a duplicated area");
        Reject(template.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            presentation, "a non-owner claim target");
        Reject(template.Replace(
                "\"effects\":[{\"op\":\"takeRandomCardFromEveryOtherCharacter\",\"target\":\"owner\",",
                "\"effects\":[{\"op\":\"takeRandomCardFromEveryOtherCharacter\",\"condition\":{\"kind\":\"wounded\"},\"target\":\"owner\","),
            presentation, "a conditional random claim");
    }

    public static void GuixinClaimsFromEveryOtherCharacterAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 200 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (!TryPlayDuelAndConcede(game)) continue;
            DriveUntil(game, () => IsGuixinPrompt(game), stopAtSkills: [Guixin]);
            if (!IsGuixinPrompt(game)) continue;

            var before = game.CreateSnapshot(0, revealAll: true);
            var claimingSeats = before.Players
                .Where(player => player.Seat != 0 && player.IsAlive &&
                    (player.HandCount > 0 || player.Equipment.Count > 0 || player.Judgment.Count > 0))
                .Select(player => player.Seat)
                .OrderBy(seat => seat)
                .ToArray();
            var cardTotal = game.CreateCardZoneDiagnostics().Count;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Guixin window must pause at an identical checkpoint.");

            Activate(game, Guixin);
            Activate(replay, Guixin);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Guixin claim must replay identically from the paused window.");

            var claim = game.Events.Select(item => item.Payload)
                .OfType<ProgramRandomCardsTakenFromCharactersEvent>()
                .Single(item => item.SkillId == Guixin);
            Require(claim.OwnerSeat == 0 &&
                    claim.SourceSeats.OrderBy(seat => seat).SequenceEqual(claimingSeats) &&
                    claim.CardCount == claimingSeats.Length &&
                    claim.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]),
                "Guixin must claim one card from exactly the other characters that still own cards.");

            var movements = game.CardMovements
                .Where(item => item.Reason.Value.Contains(ReasonFragment, StringComparison.Ordinal))
                .ToArray();
            var gains = movements.Where(item => item.To == CardLocation.Hand(0)).ToArray();
            Require(movements.Length == claimingSeats.Length * 2 &&
                    gains.Length == claimingSeats.Length &&
                    movements.All(item => item.From == CardLocation.Processing ||
                        item.From.OwnerSeat is { } owner && claimingSeats.Contains(owner)) &&
                    gains.Select(item => item.CardId).Distinct().Count() == claimingSeats.Length,
                "Every claim must move exactly one card per character through Processing into the owner hand.");

            var after = game.CreateSnapshot(0, revealAll: true);
            Require(after.Players[0].IsFaceDown &&
                    claimingSeats.All(seat => after.Players[seat].HandCount +
                        after.Players[seat].Equipment.Count + after.Players[seat].Judgment.Count >= 0),
                "Guixin must turn its owner face down and leave every source with its remaining cards.");
            Require(game.CreateCardZoneDiagnostics().Count == cardTotal,
                "The Guixin claim must conserve every physical card.");

            var diagnostics = game.CreateCardZoneDiagnostics();
            Require(gains.All(item => diagnostics.Single(card => card.CardId == item.CardId).Location is
            { Zone: CardZoneKind.Hand, OwnerSeat: 0 }),
                "Every claimed card must end up in the owner's hand.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Guixin window.");
    }

    public static void FeiyingRaisesIncomingDistance()
    {
        var registry = Registry();
        var game = Start(registry, 1);
        ReachHumanPlay(game);
        Require(game.State.Status != EngineStatus.Completed, "The Feiying fixture ended during setup.");
        for (var seat = 1; seat < 5; seat++)
        {
            var ringDistance = game.GetSeatDistance(0, seat);
            Require(game.GetCombatDistance(seat, 0) == ringDistance + 1 &&
                    game.GetCombatDistance(0, seat) == ringDistance,
                "Feiying must only raise the distance other characters need to reach its owner.");
        }
        Require(game.GetCombatDistance(0, 0) == 0 && game.GetCombatDistance(1, 1) == 0,
            "A self distance must stay zero under Feiying.");
    }

    private static bool TryPlayDuelAndConcede(GameEngine game)
    {
        for (var step = 0; step < 64 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (TryReachPlay(game) &&
                game.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.Duel) is { } duel)
            {
                Play(game, duel);
                return true;
            }
            if (!AdvanceStep(game)) return false;
        }
        return false;
    }

    private static bool TryReachPlay(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!AdvanceStep(game)) return false;
                continue;
            }
            if (prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.PlayCard) return true;
            if (prompt.PlayerSeat != 0) return false;
            if (!AnswerHumanPassively(game, prompt)) return false;
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

    private static void DriveUntil(GameEngine game, Func<bool> done, string[]? stopAtSkills = null, int budget = 800)
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
                    var choice = PassiveChoice(prompt);
                    if (choice is null) return;
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0);

    private static PromptChoice? PassiveChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
        prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "skip") ??
        (prompt.Kind == DecisionKind.SelectFaction
            ? prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("faction-id") == "qun")
            : prompt.Choices.FirstOrDefault());

    private static bool IsGuixinPrompt(GameEngine game) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            SkillPrompt.SkillId: Guixin
        };

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
        Require(result.Accepted, result.Error?.Message ?? "Shen Cao Cao skill answer failed.");
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
            MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Shen Cao Cao fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Shen Cao Cao selection failed.");
        return game;
    }

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
        throw new InvalidOperationException("The Shen Cao Cao fixture did not reach the human play phase.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Shen Cao Cao card action failed.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Shen Cao Cao fixture did not advance.");
    }

    private static bool AdvanceStep(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        return result.Accepted;
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Shen Cao Cao command failed.");
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
            $"Expected the invalid Shen Cao Cao composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
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
        public PackageManifest Manifest { get; } = new("shen-cao-cao-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are healthy Slash banks that also
            // hold the Duel the owner concedes to open his own damage window.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-cao-cao-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-cao-cao-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-cao-cao-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-cao-cao-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => index % 3 == 0
                    ? "standard:duel"
                    : "standard:slash")
                .Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:shen-cao-cao-deck", "神曹操测试牌堆", 5, 2, [])
            {
                PhysicalCards = cards
            });
            builder.AddMode(new ContentModeDefinition(Mode, "神曹操测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:shen-cao-cao-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:shen-cao-cao-bank-a",
                    "fixture:shen-cao-cao-bank-b",
                    "fixture:shen-cao-cao-bank-c",
                    "fixture:shen-cao-cao-bank-d"]));
        }
    }
}
