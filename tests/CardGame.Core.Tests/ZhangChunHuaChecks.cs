using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangChunHuaChecks
{
    private const string General = "classic:zhang-chun-hua";
    private const string Jueqing = "classic:jueqing";
    private const string Shangshi = "classic:shangshi";
    private const string SlashMode = "identity:classic-zhang-chun-hua-slash-check-5";
    private const string DuelMode = "identity:classic-zhang-chun-hua-duel-check-5";
    private const string DrainMode = "identity:classic-zhang-chun-hua-drain-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "wei" } general &&
                general.SkillIds.SequenceEqual([Jueqing, Shangshi]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Zhang Chun Hua must be in the current Wei roster with three HP.");

        var jueqing = current.Skills[Jueqing].Program!;
        Require(jueqing.Triggers.Count == 0 && jueqing.Activations.Count == 0 &&
                jueqing.CardPolicies.Count == 1,
            "Jueqing must be a locked policy-only skill without triggers or activations.");
        var conversion = jueqing.CardPolicies.Single();
        Require(conversion.Kind == SkillProgramCardPolicyKind.ConvertOutgoingDamageToHpLoss &&
                conversion.CardKinds.Count == 0 &&
                conversion.Condition.Kind == SkillProgramConditionKind.Always,
            "Jueqing must convert every outgoing damage kind unconditionally.");

        var shangshi = current.Skills[Shangshi].Program!;
        Require(shangshi.CardPolicies.Count == 0 && shangshi.Triggers.Count == 3,
            "Shangshi must be trigger-only with three trigger moments.");
        foreach (var trigger in shangshi.Triggers)
        {
            Require(trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.Optional,
                "Every Shangshi branch must be an optional owner-side trigger.");
            Require(trigger.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.LessThan,
                    Left: { Kind: SkillProgramTriggerValueKind.CurrentHandCount },
                    Right: { Kind: SkillProgramTriggerValueKind.CurrentLostHp }
                },
                "Every Shangshi branch must compare hand count against lost HP.");
            var draw = trigger.Effects.Single();
            Require(draw.Op == SkillProgramEffectOp.Draw &&
                    draw.Target == SkillProgramEffectTarget.Owner &&
                    draw.NumberExpression == SkillProgramNumberExpression.OwnerLostHpMinusHandCount &&
                    draw.Condition.Kind == SkillProgramConditionKind.Always,
                "Every Shangshi branch must refill the hand up to the lost HP.");
        }
        Require(shangshi.Triggers.Count(item => item.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                item.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage) == 1,
            "Shangshi must watch damage taken once per damage.");
        Require(shangshi.Triggers.Count(item => item.Window == SkillProgramTriggerWindow.AfterHpLost) == 1,
            "Shangshi must watch non-damage HP loss.");
        var movement = shangshi.Triggers.Single(item => item.Window == SkillProgramTriggerWindow.CardsMoved);
        Require(movement.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                movement.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                movement.ExcludedMovementReasons.Count == 0 &&
                !movement.IgnoreOwnSkillMovements,
            "Shangshi must observe every hand-card loss batch, whatever the cause.");

        const string policyTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:jueqing","revision":1,
            "minimumRulesVersion": 193,
            "cardPolicies":[{"id":"loss","kind":"convertOutgoingDamageToHpLoss"}]}]}
            """;
        const string policyPresentation = """
            {"schemaVersion":3,"skills":{"fixture:jueqing":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(policyTemplate, policyPresentation)
                .Programs["fixture:jueqing"].CardPolicies.Count == 1,
            "The damage-to-loss policy must load through the shared program parser.");
        Reject(policyTemplate.Replace("\"kind\":\"convertOutgoingDamageToHpLoss\"",
                "\"kind\":\"convertOutgoingDamageToHpLoss\",\"factionId\":\"wei\""),
            policyPresentation, "the conversion policy must not be faction gated");
        Reject(policyTemplate.Replace("\"kind\":\"convertOutgoingDamageToHpLoss\"",
                "\"kind\":\"convertOutgoingDamageToHpLoss\",\"inputSuit\":\"spade\",\"outputSuit\":\"heart\""),
            policyPresentation, "the conversion policy must not rewrite suits");
        Reject(policyTemplate.Replace(
                "\"cardPolicies\":[{\"id\":\"loss\",\"kind\":\"convertOutgoingDamageToHpLoss\"}]", ""),
            policyPresentation, "the policy skill must keep its conversion policy");

        const string triggerTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:shangshi","revision":1,
            "minimumRulesVersion": 193,
            "triggers":[
            {"id":"dmg","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage",
            "optional":true,"priority":0,
            "condition":{"kind":"compare","left":{"kind":"currentHandCount"},
            "operator":"lessThan","right":{"kind":"currentLostHp"}},
            "effects":[{"op":"draw","target":"owner","numberExpression":"ownerLostHpMinusHandCount"}]},
            {"id":"lost","window":"afterHpLost","subject":"owner",
            "optional":true,"priority":0,
            "condition":{"kind":"compare","left":{"kind":"currentHandCount"},
            "operator":"lessThan","right":{"kind":"currentLostHp"}},
            "effects":[{"op":"draw","target":"owner","numberExpression":"ownerLostHpMinusHandCount"}]},
            {"id":"move","window":"cardsMoved","subject":"owner","sourceZones":["hand"],
            "movementOccurrence":"perBatch","optional":true,"priority":0,
            "condition":{"kind":"compare","left":{"kind":"currentHandCount"},
            "operator":"lessThan","right":{"kind":"currentLostHp"}},
            "effects":[{"op":"draw","target":"owner","numberExpression":"ownerLostHpMinusHandCount"}]}]}]}
            """;
        const string triggerPresentation = """
            {"schemaVersion":3,"skills":{"fixture:shangshi":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(triggerTemplate, triggerPresentation)
                .Programs["fixture:shangshi"].Triggers.Count == 3,
            "All three Shangshi branches must load through the shared program parser.");
        Reject(triggerTemplate.Replace(
                "\"id\":\"dmg\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"damageOccurrence\":\"perDamage\",",
                "\"id\":\"dmg\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\","),
            triggerPresentation, "the damage branch must declare its damage occurrence");
        Reject(triggerTemplate.Replace(
                "\"id\":\"dmg\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"damageOccurrence\":\"perDamage\",",
                "\"id\":\"dmg\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"damageOccurrence\":\"perDamage\",\"hpChangeOccurrence\":\"perPoint\","),
            triggerPresentation, "an after-damage branch must not declare HP-change occurrences");
        Reject(triggerTemplate.Replace(
                "\"id\":\"move\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],",
                "\"id\":\"move\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"turnOwnerScope\":\"own\","),
            triggerPresentation, "the movement branch must not declare a turn-owner scope");
        Reject(triggerTemplate.Replace(
                "\"id\":\"move\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],",
                "\"id\":\"move\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"destinationZones\":[\"hand\"],"),
            triggerPresentation, "destination filters require the cardsGained window");
        Reject(triggerTemplate.Replace(
                "\"right\":{\"kind\":\"currentLostHp\"}",
                "\"right\":{\"kind\":\"currentLostHp\",\"value\":2}"),
            triggerPresentation, "only integer constants accept a literal value");
        Reject(triggerTemplate.Replace(
                "\"op\":\"draw\",\"target\":\"owner\",\"numberExpression\":\"ownerLostHpMinusHandCount\"",
                "\"op\":\"draw\",\"target\":\"owner\",\"amount\":2,\"numberExpression\":\"ownerLostHpMinusHandCount\"",
                StringComparison.Ordinal),
            triggerPresentation, "an expression draw must not carry a constant amount");
        Reject(triggerTemplate.Replace(
                "\"numberExpression\":\"ownerLostHpMinusHandCount\"",
                "\"numberExpression\":\"integerConstant\"",
                StringComparison.Ordinal),
            triggerPresentation, "the refill draw must use the lost-HP expression");
        Reject(triggerTemplate.Replace(
                "\"op\":\"draw\",\"target\":\"owner\"",
                "\"op\":\"draw\",\"target\":\"selectedTarget\"",
                StringComparison.Ordinal),
            triggerPresentation, "the refill draw must act on its owner");
    }

    public static void JueqingConvertsSlashDamageToHpLossAndReplays()
    {
        var registry = Registry(SlashMode, SlashDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, SlashMode);
            if (!DriveToHumanSlash(game)) continue;
            var target = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.TargetSeat is not null)
                .Select(action => action.TargetSeat!.Value)
                .First();
            var before = game.CreateSnapshot(0, true).Players[target].Hp;

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            var eventsBeforeSlash = game.Events.Count;
            PlayHumanSlash(game, target);
            DriveUntilSettled(game);

            // Assert on the slash-resolution slice only: later AI turns deal
            // ordinary damage that must not leak into this conversion window.
            var slashSlice = game.Events.Skip(eventsBeforeSlash).Select(item => item.Payload).ToArray();
            var loss = slashSlice.OfType<ProgramSkillHpLostEvent>()
                .Single(item => item.SkillId == Jueqing);
            Require(loss.TargetSeat == target && loss.Amount == 1 && loss.RemainingHp == before - 1,
                "Jueqing must turn the slash into exactly one point of target HP loss.");
            Require(!slashSlice.OfType<DamageAppliedEvent>().Any(),
                "The converted slash must not emit any damage-applied event.");
            Require(game.CreateSnapshot(0, true).Players[target].Hp == before - 1,
                "The target must keep the reduced HP after the conversion.");
            PlayHumanSlash(replay, target);
            DriveUntilSettled(replay);

            DriveUntil(game, () => false, budget: 300);
            DriveUntil(replay, () => false, budget: 300);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The jueqing slash must replay identically from the paused play decision.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a jueqing slash conversion.");
    }

    public static void JueqingConvertsDuelDamageAndReplays()
    {
        var registry = Registry(DuelMode, DuelDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, DuelMode);
            if (!DriveToHumanDuel(game)) continue;
            var duel = game.GetHumanLegalActions()
                .First(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null);

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            var eventsBeforeDuel = game.Events.Count;
            Accept(game.Submit(new PlayCardCommand(0, duel.CardId!.Value, duel.TargetSeats,
                game.Revision, game.PendingDecision!.PromptId, duel.PlayedCardKind, duel.TargetCardId)));
            DriveUntilSettled(game);

            // Assert on the duel-resolution slice: the all-duel deck leaves the
            // banks without any Slash response, so the duel damage must convert.
            var duelSlice = game.Events.Skip(eventsBeforeDuel).Select(item => item.Payload).ToArray();
            var losses = duelSlice.OfType<ProgramSkillHpLostEvent>()
                .Where(item => item.SkillId == Jueqing).ToArray();
            Require(losses.Length >= 1 && losses.All(item => item.TargetSeat != 0),
                "Jueqing must turn the duel damage into HP loss for the bank, never for its owner.");
            Require(!duelSlice.OfType<DamageAppliedEvent>().Any(item => item.SourceSeat == 0),
                "The converted duel must not emit any owner-sourced damage-applied event.");
            Accept(replay.Submit(new PlayCardCommand(0, duel.CardId!.Value, duel.TargetSeats,
                replay.Revision, replay.PendingDecision!.PromptId, duel.PlayedCardKind, duel.TargetCardId)));
            DriveUntilSettled(replay);

            DriveUntil(game, () => false, budget: 300);
            DriveUntil(replay, () => false, budget: 300);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The jueqing duel must replay identically from the paused play decision.");
            Require(game.CardMovements.Any(item => item.CardId == duel.CardId &&
                    item.To.Zone == CardZoneKind.DiscardPile),
                "The duel card itself must still finish in the discard pile.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a jueqing duel conversion.");
    }

    public static void ShangshiRefillsHandAfterDuelDamageAndReplays()
    {
        var registry = Registry(DrainMode, DrainDeck(), bankHp: 8);
        var completed = 0;
        for (var seed = 1; seed <= 220 && completed < 1; seed++)
        {
            var game = Start(registry, seed, DrainMode);
            DriveUntil(game, () => false, stopAtSkills: [Shangshi], budget: 2600);
            if (!IsProgramPrompt(game, Shangshi)) continue;
            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);

            var beforeState = game.CreateSnapshot(0, true).Players[0];
            var resolvedDamage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                .Last(item => item.TargetSeat == 0);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Shangshi refill must replay identically from the paused prompt.");
            Require(resolvedDamage.SourceSeat != 0,
                "The Shangshi prompt must follow damage taken from another character.");
            var binding = game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                .Single(item => item.SkillId == Shangshi && item.Activated);
            Require(binding.Window == SkillProgramTriggerWindow.AfterDamageApplied,
                "The first Shangshi refill must resolve through the after-damage window.");
            var after = game.CreateSnapshot(0, true).Players[0];
            var expectedX = beforeState.MaxHp - beforeState.Hp;
            Require(after.Hp == beforeState.Hp &&
                    after.Hand.Count == expectedX && beforeState.Hand.Count < expectedX,
                $"Shangshi must refill the hand exactly to the {expectedX} lost HP.");
            var refill = game.CardMovements.Where(item => item.To.OwnerSeat == 0 &&
                    item.To.Zone == CardZoneKind.Hand &&
                    item.Reason.Value.Contains(Shangshi, StringComparison.Ordinal)).ToList();
            Require(refill.Count == expectedX - beforeState.Hand.Count,
                "The refill must draw exactly the shortfall between hand and lost HP.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Shangshi after-damage prompt.");
    }

    private static void PlayHumanSlash(GameEngine game, int targetSeat)
    {
        var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeat == targetSeat) ??
            throw new InvalidOperationException("The human slash action vanished.");
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind, slash.TargetCardId)));
    }

    private static bool DriveToHumanSlash(GameEngine game)
    {
        for (var step = 0; step < 900 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.PlayCard)
            {
                if (game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Slash && action.TargetSeat is not null))
                {
                    return true;
                }
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                continue;
            }
            if (prompt.Kind == DecisionKind.DiscardCards)
            {
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                continue;
            }
            var fallback = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.FirstOrDefault();
            if (fallback is null)
            {
                Advance(game);
                continue;
            }
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                fallback.Id, game.Revision)));
        }
        return false;
    }

    private static bool DriveToHumanDuel(GameEngine game)
    {
        for (var step = 0; step < 900 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.PlayCard)
            {
                if (game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Duel && action.TargetSeat is not null))
                {
                    return true;
                }
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                continue;
            }
            if (prompt.Kind == DecisionKind.DiscardCards)
            {
                Accept(game.Submit(new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision)));
                continue;
            }
            var fallback = prompt.Choices.FirstOrDefault(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.FirstOrDefault();
            if (fallback is null)
            {
                Advance(game);
                continue;
            }
            Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                fallback.Id, game.Revision)));
        }
        return false;
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
        new ZhangChunHuaScenario(mode, deck, bankHp));

    private static ContentDeckRecipe SlashDeck() =>
        DeckCore("standard:slash", 0);

    private static ContentDeckRecipe DuelDeck() =>
        DeckCore("standard:duel", 0);

    private static ContentDeckRecipe DrainDeck() =>
        DeckCore("standard:duel", 3);

    private static ContentDeckRecipe DeckCore(string kind, int slashModulo) =>
        new("fixture:zhang-chun-hua-deck", "张春华测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                slashModulo > 0 && index % slashModulo != 0
                    ? new ContentDeckPhysicalCard("standard:slash", Suit.Spade, index % 13 + 1)
                    : new ContentDeckPhysicalCard(kind, Suit.Spade, index % 13 + 1)).ToArray()
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
        Require(game.Submit(new StartGameCommand()).Accepted, "Zhang Chun Hua fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Zhang Chun Hua selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang Chun Hua fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Zhang Chun Hua command failed.");
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
            $"Expected invalid Zhang Chun Hua composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class ZhangChunHuaScenario(string modeId, ContentDeckRecipe deck, int bankHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("zhang-chun-hua-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Jueqing and Shangshi on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-chun-hua-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-chun-hua-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-chun-hua-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:zhang-chun-hua-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "张春华测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:zhang-chun-hua-bank-a",
                    "fixture:zhang-chun-hua-bank-b",
                    "fixture:zhang-chun-hua-bank-c",
                    "fixture:zhang-chun-hua-bank-d"]));
        }
    }
}
