using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CompiledSkillMetadataChecks
{
    public static void CatalogDependenciesPreserveWholeRegistryAnswersAndIsolation()
    {
        var plain = StandardContentRegistry.Create();
        var classic = StandardContentRegistry.CreateWithClassicGenerals();
        var customProgram = LoadTwinBindings();
        var custom = ContentRegistry.Build(new StandardContentPackage(), new ProgramOnlyPackage(customProgram));
        foreach (var registry in new[] { plain, classic, custom })
        {
            var programs = registry.Skills.Values.Select(skill => skill.Program).OfType<SkillProgram>().ToArray();
            var triggers = programs.SelectMany(program => program.Triggers).ToArray();
            var dependencies = registry.ProgramDependencies;
            foreach (var kind in Enum.GetValues<SkillProgramTriggerConditionKind>())
                Require(dependencies.UsesTriggerCondition(kind) == triggers.Any(trigger => ContainsCondition(trigger.Condition, kind)),
                    $"The complete catalog condition opt-in changed for {kind}.");
            foreach (var kind in Enum.GetValues<SkillProgramTriggerValueKind>())
                Require(dependencies.UsesTriggerValue(kind) == triggers.Any(trigger => ContainsValue(trigger.Condition, kind)),
                    $"The complete catalog value opt-in changed for {kind}.");
            foreach (var op in Enum.GetValues<SkillProgramEffectOp>())
                Require(dependencies.HasTriggerOperation(op) == triggers.Any(trigger => trigger.Effects.Any(effect => effect.Op == op)),
                    $"Activation effects must not alter the trigger-only operation opt-in {op}.");
            foreach (var op in Enum.GetValues<SkillProgramEffectOp>())
            {
                Require(dependencies.HasActivationOperation(op) == programs.Any(program =>
                    program.Activations.Any(activation => activation.Effects.Any(effect => effect.Op == op))),
                    $"Unowned activation definitions must preserve the catalog appearance opt-in {op}.");
                var expectedSkills = registry.Skills.Values.Where(skill => skill.Program?.Triggers
                    .Any(trigger => trigger.Effects.Any(effect => effect.Op == op)) == true)
                    .Select(skill => skill.Id).ToArray();
                var capturedSkills = dependencies.GetTriggerOperationSkillIds(op);
                Require(capturedSkills.SequenceEqual(expectedSkills),
                    $"The unconditional trigger ledger must retain the exact catalog skill identities for {op}.");
                Require(capturedSkills.Count == 0 || capturedSkills is ICollection<string> frozen && frozen.IsReadOnly,
                    "Definition dependency lists cannot become mutable rule state.");
            }
            foreach (var window in Enum.GetValues<SkillProgramTriggerWindow>())
                Require(dependencies.HasTriggerWindow(window) == triggers.Any(trigger => trigger.Window == window),
                    $"The catalog lifecycle opt-in changed for {window}.");
            foreach (var kind in Enum.GetValues<SkillProgramCardPolicyKind>())
                Require(dependencies.HasCardPolicy(kind) == programs.Any(program => program.CardPolicies.Any(policy => policy.Kind == kind)),
                    $"The compiled card-policy opt-in changed for {kind}.");
            foreach (var kind in Enum.GetValues<CardKind>())
            foreach (var category in Enum.GetValues<SkillProgramCardCategory>())
                Require(dependencies.HasFinalizedCardTrigger(kind, category) == triggers.Any(trigger =>
                    trigger.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                    (trigger.CardKinds.Count == 0 || trigger.CardKinds.Contains(kind)) &&
                    (trigger.CardCategories.Count == 0 || trigger.CardCategories.Contains(category))),
                    "Card-kind/category correlations must remain on the same finalized-target trigger.");
            Require(dependencies.CapturesCompletedResponseSuit == triggers.Any(trigger =>
                trigger.Window == SkillProgramTriggerWindow.CardUseCompleted && trigger.IncludeResponseUses &&
                ContainsCondition(trigger.Condition, SkillProgramTriggerConditionKind.CardActionSuitIs)),
                "An unrelated suit condition must not opt into completed-response suit capture.");
            Require(dependencies.TracksPlayCardHistory == programs.Any(program =>
                program.Triggers.Any(trigger => trigger.Effects.Any(effect => effect.Op is
                    SkillProgramEffectOp.ReplaceAllSlashTargets or SkillProgramEffectOp.GrantRandomSkillAndSuitShield)) ||
                program.ViewAs.Any(rule => rule.InheritPreviousPlaySuit) || program.Triggers.Any(trigger =>
                    ContainsCondition(trigger.Condition, SkillProgramTriggerConditionKind.CardActionMatchesPreviousPlayCard) ||
                    ContainsCondition(trigger.Condition, SkillProgramTriggerConditionKind.CardActionSuitIs))),
                "Historical catalogs must retain their exact play-card history/event opt-in.");
            Require(dependencies.HasCardPolicyKindAtOrAbove(450) == programs.Any(program =>
                program.CardPolicies.Any(policy => (int)policy.Kind >= 450)),
                "Unowned catalog policies must retain the public-counter opt-in.");
        }
        Require(!ReferenceEquals(plain.ProgramDependencies, classic.ProgramDependencies) &&
            !plain.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.TurnEnding) &&
            custom.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.TurnEnding),
            "Separate frozen registries must not reuse another catalog's static answers.");

        // This package registers a program without giving it to any general.
        // Catalog opt-ins must still capture it, exactly as the historical scan did.
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, HumanSeat = 0 }, custom);
        Require(game.ContentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.TurnEnding),
            "Dependency compilation must not narrow catalog scope to a match's owners.");
    }

    public static void SourcePlansReuseBindingsWithoutIdentityOrOrderCollisions()
    {
        var program = LoadTwinBindings();
        var resolver = new ProgramInstructionResolver();
        var active = resolver.Resolve(program, ProgramInstructionSourceKind.Activation, "same");
        var triggered = resolver.Resolve(program, ProgramInstructionSourceKind.Trigger, "same");
        Require(ReferenceEquals(active.Activation, program.Activations.Single()) && active.Trigger is null &&
            ReferenceEquals(triggered.Trigger, program.Triggers.Single()) && triggered.Activation is null &&
            active.Instructions.Select(effect => effect.Op).SequenceEqual(new[] { SkillProgramEffectOp.Draw, SkillProgramEffectOp.Recover }) &&
            triggered.Instructions.Select(effect => effect.Op).SequenceEqual(new[] { SkillProgramEffectOp.Recover, SkillProgramEffectOp.Draw }),
            "Same-name activation/trigger sources must retain their exact binding and instruction order.");
        var parallel = new ProgramExecutionPlan[16];
        Parallel.For(0, parallel.Length, index => parallel[index] =
            resolver.Resolve(program, ProgramInstructionSourceKind.Activation, "same"));
        Require(parallel.All(plan => ReferenceEquals(plan, active)) &&
            ReferenceEquals(active.Features, resolver.Features(program.Activations.Single())),
            "Repeated and concurrent resolution must reuse the same published definition plan/features.");

        var other = LoadTwinBindings(drawAmount: 2);
        var otherPlan = resolver.Resolve(other, ProgramInstructionSourceKind.Activation, "same");
        var sameFingerprint = new SkillProgram(program.Id, program.Revision, program.GameplayHash,
            program.RuntimeVersion, program.MinimumRulesVersion, program.Modifiers, program.ViewAs,
            other.Activations, program.Triggers, program.Contributions, program.CardIdentities);
        var separateReference = resolver.Resolve(sameFingerprint, ProgramInstructionSourceKind.Activation, "same");
        Require(!ReferenceEquals(active, otherPlan) && active.GetInstruction(0).Effect.Amount == 1 &&
            otherPlan.GetInstruction(0).Effect.Amount == 2 && !ReferenceEquals(active, separateReference) &&
            separateReference.GetInstruction(0).Effect.Amount == 2 && resolver.FindActivation(program, "missing") is null &&
            resolver.FindTrigger(program, null) is null,
            "A same-id program or absent binding must not leak another immutable reference's plan.");
        var readOnly = (IList<SkillProgramEffect>)active.Features.ForOperation(SkillProgramEffectOp.Draw);
        Require(readOnly.IsReadOnly, "Compiled operation buckets must not expose mutable arrays.");
        try { readOnly[0] = triggered.GetInstruction(0).Effect; throw new InvalidOperationException("Bucket mutation was accepted."); }
        catch (NotSupportedException) { }
    }

    public static void SharedLegalityProtectsActionsSubmittedInputAndPublicAi()
    {
        const string id = "fixture:compiled-legality";
        var rules = $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,
          "activations":[
            {"id":"gift","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,
             "effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
            {"id":"contest","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
             "effects":[{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"result","visibility":"public"}]},
            {"id":"clear","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
             "effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}]}
        """;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = 3,
            skills = new Dictionary<string, object> { [id] = new { name = "Legality", description = "Public prerequisites" } } });
        var registry = ProgramCompositionEntryChecks.Registry(id, rules, presentation);
        var game = ProgramCompositionEntryChecks.Start(registry, id);
        ProgramCompositionEntryChecks.ReachPlay(game);
        var gift = game.GetHumanLegalActions().Single(action => action.ProgramActivationId == "gift");
        var contest = game.GetHumanLegalActions().Single(action => action.ProgramActivationId == "contest");
        Require(!gift.SelectableTargetSeats.Contains(0) && contest.SelectableTargetSeats.Count == 4,
            "Gift selection must exclude its owner; a hand contest must retain all initially eligible opponents.");
        var card = gift.SelectableCardIds[0];
        var before = ProgramCompositionEntryChecks.State(game);
        var revision = game.Revision;
        var rejected = game.Submit(new UseProgramSkillCommand(0, id, "gift", [card], [0], revision, game.PendingDecision!.PromptId));
        Require(!rejected.Accepted && game.Revision == revision && ProgramCompositionEntryChecks.State(game) == before,
            "A forged self-gift must not mutate state or pay the selected card.");
        var receiverBefore = game.CreateSnapshot(0).Players[1].HandCount;
        var accepted = game.Submit(new UseProgramSkillCommand(0, id, "gift", [card], [1], game.Revision, game.PendingDecision!.PromptId));
        Require(accepted.Accepted && game.CreateSnapshot(0).Players[1].HandCount == receiverBefore + 1 &&
            !game.CreateSnapshot(0).Players[0].Hand.Any(item => item.Id == card),
            "A legal gift must still move the exact paid card to the actual recipient once.");
        ProgramCompositionEntryChecks.ReachPlay(game);
        accepted = game.Submit(new UseProgramSkillCommand(0, id, "clear", [], [], game.Revision, game.PendingDecision!.PromptId));
        Require(accepted.Accepted, accepted.Error?.Message ?? "The real hand-clearing command was rejected.");
        ProgramCompositionEntryChecks.ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].HandCount == 0 &&
            game.GetHumanLegalActions().All(action => action.ProgramActivationId != "contest"),
            "Emptying the actual owner hand must remove its zero-input hand contest.");
        var validate = typeof(GameEngine).GetMethod("ValidateProgramSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(validate.Invoke(game, [contest, Array.Empty<int>(), new[] { 1 }]) is CommandError { Code: CommandErrorCode.IllegalAction },
            "The final validator must reject stale contest input using the same current public prerequisites.");

        var program = registry.GetSkill(id).Program!;
        var owner = new PlayerSkillContext(0, 4, 4, 4, TurnPhase.Play, IsOwnTurn: true);
        var target = new PlayerSkillContext(1, 4, 4, 0, TurnPhase.Play);
        var contestEffects = ProgramInstructionResolver.Default.FindActivation(program, "contest")!.Effects;
        Require(ProgramCompositionAi.Estimate(contestEffects, owner, publicContext: new(2, SelectedTarget: target)).Score == 0d &&
            ProgramCompositionAi.Estimate(contestEffects, owner with { HandCount = 0 }).Score == 0d &&
            ProgramCompositionAi.Estimate(contestEffects, owner, publicContext: new(2, SelectedTarget: target with { HandCount = 1 })).Score > 0d,
            "AI must use the same public hand requirements without inspecting hidden physical cards.");
        var giftEffects = ProgramInstructionResolver.Default.FindActivation(program, "gift")!.Effects;
        Require(!ProgramCompositionAi.Estimate(giftEffects, owner, publicContext: new(2, SelectedTarget: owner)).Hint.GivesSelected &&
            ProgramCompositionAi.Estimate(giftEffects, owner, publicContext: new(2, SelectedTarget: target)).Hint.GivesSelected,
            "AI must distinguish an impossible self-gift from an eligible recipient using the same descriptor policy.");
        var restored = GameReplay.Restore(ProgramCompositionEntryChecks.RoundTrip(game.CreateCheckpoint()), registry);
        Require(ProgramCompositionEntryChecks.Events(game).SequenceEqual(ProgramCompositionEntryChecks.Events(restored)) &&
            Enumerable.Range(0, 5).All(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer)) == SnapshotJson.Serialize(restored.CreateSnapshot(viewer))),
            "Accepted input must replay to the same events, card ownership and every player view.");
    }

    public static void ExecutionConditionsReadHandContextOnlyWhenRequired()
    {
        const string id = "fixture:execution-hand-context";
        var rules = $$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{id}}}","revision":1,
          "activations":[{"id":"active","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[
              {"op":"draw","target":"owner","amount":1,"condition":{"kind":"positiveHandLimit"}},
              {"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},
              {"op":"chooseOption","target":"owner","resultBind":"emptyHand","options":[
                {"id":"empty","condition":{"kind":"not","children":[{"kind":"handCountAtLeast","value":1}]}},
                {"id":"cards","condition":{"kind":"handCountAtLeast","value":1}}]},
              {"op":"draw","target":"owner","amount":1,"condition":{"kind":"all","children":[
                {"kind":"positiveHandLimit"},{"kind":"not","children":[{"kind":"hasUsableHandCard"}]}]}},
              {"op":"draw","target":"owner","amount":2,"condition":{"kind":"any","children":[
                {"kind":"hpAtLeast","value":100},{"kind":"all","children":[{"kind":"positiveHandLimit"},
                  {"kind":"not","children":[{"kind":"not","children":[{"kind":"hasUsableHandCard"}]}]}]}]}},
              {"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"handLimit","ruleOperation":"add","amount":-20},
              {"op":"draw","target":"owner","amount":4,"condition":{"kind":"positiveHandLimit"}},
              {"op":"draw","target":"owner","amount":1,"condition":{"kind":"not","children":[{"kind":"positiveHandLimit"}]}},
              {"op":"chooseOption","target":"owner","resultBind":"drawnHand","options":[
                {"id":"empty","condition":{"kind":"not","children":[{"kind":"handCountAtLeast","value":1}]}},
                {"id":"cards","condition":{"kind":"handCountAtLeast","value":1}}]}]}]}]}
        """;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = 3,
            skills = new Dictionary<string, object> { [id] = new
            {
                name = "Hand context", description = "Read each instruction's current hand state",
                optionLabels = new { empty = "Empty", cards = "Cards" }
            } } });
        // Choice gates accept public chooser state, rather than hidden-card usability
        // or rule-query results. Keep that boundary while effect gates read both.
        foreach (var kind in new[] { "positiveHandLimit", "hasUsableHandCard" })
        {
            var invalidOptions = rules.Replace("{\"kind\":\"handCountAtLeast\",\"value\":1}",
                $"{{\"kind\":\"{kind}\"}}", StringComparison.Ordinal);
            try
            {
                SkillProgramCatalog.Load(invalidOptions, presentation);
                throw new InvalidOperationException("A private or rule-query chooser condition was accepted.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("public chooser state", StringComparison.Ordinal)) { }
        }
        var registry = ProgramCompositionEntryChecks.Registry(id, rules, presentation);
        var game = ProgramCompositionEntryChecks.Start(registry, id);
        ProgramCompositionEntryChecks.ReachPlay(game);
        var hostType = typeof(GameEngine).GetNestedType("ProgramSkillHost", BindingFlags.NonPublic)!;
        var host = (ISkillProgramExecutionHost)hostType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single().Invoke([game]);
        var ordinary = host.GetActor(0);
        Require(ordinary.IsAlive && ordinary.Context.HandCount > 0 &&
            ordinary.Context.HandLimit is null && ordinary.Context.HasUsableHandCard is null,
            "Ordinary executor actor reads must not eagerly enumerate native hand actions or query the hand limit.");

        ProgramCompositionEntryChecks.UseActivation(game, id);
        RequireOptions(game, "empty", 0);
        var restored = GameReplay.Restore(ProgramCompositionEntryChecks.RoundTrip(game.CreateCheckpoint()), registry);
        RequireSame(game, restored);
        foreach (var current in new[] { game, restored })
        {
            AnswerOption(current, "empty");
            RequireOptions(current, "cards", 4);
        }
        RequireSame(game, restored);
        restored = GameReplay.Restore(ProgramCompositionEntryChecks.RoundTrip(game.CreateCheckpoint()), registry);
        RequireSame(game, restored);
        foreach (var current in new[] { game, restored })
        {
            AnswerOption(current, "cards");
            ProgramCompositionEntryChecks.ReachPlay(current);
            Require(current.CreateSnapshot(0).Players[0].HandCount == 4,
                "Dependent effect gates must see clear, draw and hand-limit changes without replaying an instruction.");
        }
        RequireSame(game, restored);

        static void RequireOptions(GameEngine current, string expected, int handCount)
        {
            var prompt = current.PendingDecision;
            Require(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("option-id")).SequenceEqual([expected]) &&
                current.CreateSnapshot(0).Players[0].HandCount == handCount,
                "A paused option must expose only its current public hand-count branch.");
        }
        static void AnswerOption(GameEngine current, string option)
        {
            var prompt = current.PendingDecision!;
            var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("option-id") == option);
            var answer = current.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, current.Revision));
            Require(answer.Accepted, answer.Error?.Message ?? "The current public hand-state option was rejected.");
        }
        static void RequireSame(GameEngine original, GameEngine copy) => Require(
            ProgramCompositionEntryChecks.Events(original).SequenceEqual(ProgramCompositionEntryChecks.Events(copy)) &&
            Enumerable.Range(0, 5).All(viewer => SnapshotJson.Serialize(original.CreateSnapshot(viewer)) ==
                SnapshotJson.Serialize(copy.CreateSnapshot(viewer))),
            "Cold replay must preserve both suspended choice gates, events and every private player view.");
    }

    public static void OrdinaryTrickQueriesPreserveExactOptionsWhenNarrowed()
    {
        const string id = "fixture:ordinary-trick-query";
        var rules = $$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{id}}}","revision":1,
          "activations":[{"id":"active","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
        """;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = 3,
            skills = new Dictionary<string, object> { [id] = new { name = "Trick query", description = "Exact ordinary trick options" } } });
        var registry = ProgramCompositionEntryChecks.Registry(id, rules, presentation);
        var game = ProgramCompositionEntryChecks.Start(registry, id);
        ProgramCompositionEntryChecks.ReachPlay(game);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var method = typeof(GameEngine).GetMethod("BuildProgramOrdinaryTrickUseOptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var optionType = typeof(GameEngine).GetNestedType("ProgramOrdinaryTrickUseOption", BindingFlags.NonPublic)!;
        var effectiveKind = optionType.GetProperty("EffectiveCardKind")!;
        var kinds = new[]
        {
            CardKind.DrawTwo, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.PeachGarden,
            CardKind.FiveGrains, CardKind.Duel, CardKind.Dismantlement, CardKind.Snatch,
            CardKind.FireAttack, CardKind.IronChain, CardKind.BorrowedSword
        };
        var firstCard = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        var scenarios = new (Suit? PhysicalSuit, bool ExcludeOwner, bool EnforcePermission,
            Suit? ShieldSuit, bool? Color, bool HasActualColor, IReadOnlyList<int>? PhysicalCards, bool IncludeAdjustment)[]
        {
            (null, false, false, null, null, false, null, false),
            (Suit.Spade, true, true, Suit.Heart, false, true, new[] { firstCard }, true),
            (Suit.None, false, true, null, null, true, Array.Empty<int>(), true)
        };
        var before = ProgramCompositionEntryChecks.State(game);
        var eventsBefore = ProgramCompositionEntryChecks.Events(game);
        foreach (var scenario in scenarios)
        {
            var broad = Query(null);
            Require(broad.Any(option => (CardKind)effectiveKind.GetValue(option)! == CardKind.Duel) &&
                broad.Any(option => (CardKind)effectiveKind.GetValue(option)! == CardKind.IronChain),
                "The shared query fixture must offer real targeted and multiple-target trick choices.");
            foreach (var kind in kinds)
            {
                var expected = broad.Where(option => (CardKind)effectiveKind.GetValue(option)! == kind)
                    .Select(Describe).ToArray();
                var actual = Query(kind).Select(Describe).ToArray();
                Require(expected.SequenceEqual(actual),
                    $"Narrowing {kind} changed its exact option order, identities, targets, card choice or adjustment: {scenario}.");
            }

            object[] Query(CardKind? kind) => ((System.Collections.IEnumerable)method.Invoke(game,
                [players[0], kind, scenario.PhysicalSuit, scenario.ExcludeOwner, scenario.EnforcePermission,
                    scenario.ShieldSuit, scenario.Color, scenario.HasActualColor, scenario.PhysicalCards,
                    scenario.IncludeAdjustment])!).Cast<object>().ToArray();
        }
        Require(before == ProgramCompositionEntryChecks.State(game) &&
            eventsBefore.SequenceEqual(ProgramCompositionEntryChecks.Events(game)),
            "Broad and narrowed option queries must leave the match and committed event history unchanged.");

        // Serialize the complete option, including Id, ActionKind, TargetSeats,
        // TargetCardId, RequiredCardKind, Description and NextActualUseAdjusted.
        static string Describe(object option) => JsonSerializer.Serialize(option, option.GetType());
    }

    public static void PhysicalCardQueriesPreserveWholePoolFactsAndExactActions()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new PhysicalQueryPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = PhysicalQueryPackage.Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Physical-query fixture start failed.");
        Require(game.Submit(new SelectGeneralCommand(0, PhysicalQueryPackage.Owner, game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Physical-query fixture owner was not offered.");
        ProgramCompositionEntryChecks.ReachPlay(game);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", flags)!.GetValue(game)!;
        var zones = (CardZoneStore)typeof(GameEngine).GetField("_cardZones", flags)!.GetValue(game)!;
        var query = typeof(GameEngine).GetMethod("BuildLegalActions", flags)!;
        var adjustments = (Dictionary<int, CardConversionSource>)typeof(GameEngine).GetField("_nextCardTargetAdjustmentOwners", flags)!.GetValue(game)!;
        var effects = (TurnCardUseEffectStore)typeof(GameEngine).GetField("_turnCardUseEffects", flags)!.GetValue(game)!;

        // Host-only placement isolates read-side legality. These are existing
        // finite deck entities; no replay or accepted-input claim is made here.
        foreach (var card in zones.CardsAt(CardLocation.Hand(0)).ToArray())
            zones.Move(card.Id, CardLocation.Hand(0), CardLocation.DiscardPile);
        var slash = Place(CardKind.Slash, CardLocation.Hand(0));
        var dodge = Place(CardKind.Dodge, CardLocation.Hand(0));
        var chain = Place(CardKind.IronChain, CardLocation.Hand(0));
        var borrowed = Place(CardKind.BorrowedSword, CardLocation.Hand(0));
        var weapon = Place(CardKind.FangtianHalberd, CardLocation.Equipment(0));
        Place(CardKind.WoodenOx, CardLocation.Equipment(0));
        var grain = Place(CardKind.Slash, CardLocation.WoodenOxGrain(0));
        var authority = Place(CardKind.Dodge, CardLocation.Authority(0));
        var judgment = Place(CardKind.Indulgence, CardLocation.Judgment(0));
        Place(CardKind.Crossbow, CardLocation.Equipment(1));
        Place(CardKind.Crossbow, CardLocation.Equipment(3));
        var grant = players[0].SkillGrants.Grants.Single(item => item.SkillId == PhysicalQueryPackage.Skill);
        adjustments[0] = new(PhysicalQueryPackage.Skill, "next-card-target-adjustment", 0, grant.SkillInstanceId);
        var ids = new[] { slash, dodge, chain, borrowed, weapon, grain, authority, judgment, 0, int.MaxValue };
        var broad = Verify("native, chained, every owned material region and adjusted Borrowed Sword", ids);
        Require(broad.Any(action => action.CardId == slash && action.Kind == LegalActionKind.Slash && action.ConversionSource is null) &&
            broad.Any(action => action.CardId == dodge && action.AdditionalConversionSources is { Count: > 0 }) &&
            new[] { weapon, grain, authority, judgment }.All(id => broad.Any(action => action.CardId == id)) &&
            broad.Any(action => action.CardId == borrowed && action.Kind == LegalActionKind.BorrowedSword && action.TargetSeats.Count == 4),
            "The shared fixture must actually offer native/chained actions, all selected source zones and complete adjusted holder/victim pairs.");

        adjustments.Clear();
        broad = Verify("Fangtian with several actual Hand cards", [slash, dodge, grain]);
        Require(!broad.Any(action => action.CardId == slash && action.Kind == LegalActionKind.Slash && action.TargetSeats.Count > 1),
            "Selecting one entity cannot turn the real multi-card Hand into Fangtian's last Hand card.");
        foreach (var card in zones.CardsAt(CardLocation.Hand(0)).Where(card => card.Id != slash).ToArray())
            zones.Move(card.Id, CardLocation.Hand(0), CardLocation.DiscardPile);
        broad = Verify("Fangtian with one actual Hand card plus usable Wooden Ox grain", [slash, grain]);
        Require(zones.CardsAt(CardLocation.Hand(0)).Count == 1 && zones.CardsAt(CardLocation.WoodenOxGrain(0)).Count == 1 &&
            broad.Any(action => action.CardId == slash && action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 3) &&
            !broad.Any(action => action.CardId == grain && action.Kind == LegalActionKind.Slash && action.TargetSeats.Count > 1),
            "Fangtian reads the complete actual Hand, while grain remains usable without becoming its last Hand entity.");

        typeof(GameEngine).GetField("_slashCountThisTurn", flags)!.SetValue(game, 1);
        broad = Verify("exhausted ordinary Slash quota", [slash, grain, judgment, weapon]);
        Require(!broad.Any(action => action.CardId == slash && action.Kind == LegalActionKind.Slash),
            "An exhausted Slash allowance must stay exhausted in a selected-entity query.");
        typeof(GameEngine).GetField("_slashCountThisTurn", flags)!.SetValue(game, 0);
        var secondJudgment = Place(CardKind.Lightning, CardLocation.Judgment(0));
        broad = Verify("two actual Judgment entities", [judgment, secondJudgment]);
        Require(!broad.Any(action => action.CardId == judgment || action.CardId == secondJudgment),
            "A selected Judgment entity must not conceal the other actual entity from last-source-zone qualification.");

        zones.Move(chain, zones.GetLocation(chain), CardLocation.Hand(0));
        effects.GrantHandColorRestriction(game.State.TurnNumber, 0, 900, 0,
            new(PhysicalQueryPackage.Skill, "host-restriction", 0, grant.SkillInstanceId), 0, true);
        broad = Verify("restricted real Iron Chain retains native recast", [chain, slash, authority]);
        Require(broad.Any(action => action.CardId == chain && action.Kind == LegalActionKind.Recast) &&
            !broad.Any(action => action.CardId == chain && action.Kind != LegalActionKind.Recast),
            "A restricted Hand entity keeps its legal native recast but cannot gain a use or conversion.");

        int Place(CardKind kind, CardLocation destination)
        {
            var id = game.CreateCardZoneDiagnostics().First(card => card.CardKind == kind && card.Location == CardLocation.DrawPile).CardId;
            zones.Move(id, CardLocation.DrawPile, destination);
            return id;
        }
        IReadOnlyList<LegalAction> Query(bool includePrograms, int? selected) =>
            (IReadOnlyList<LegalAction>)query.Invoke(game, [players[0], includePrograms, selected])!;
        IReadOnlyList<LegalAction> Verify(string boundary, IReadOnlyList<int> selectedIds)
        {
            var before = Observe();
            IReadOnlyList<LegalAction>? complete = null;
            foreach (var includePrograms in new[] { true, false })
            {
                var wide = Query(includePrograms, null);
                complete ??= wide;
                foreach (var id in selectedIds)
                {
                    var expected = JsonSerializer.Serialize(wide.Where(action => action.CardId == id).ToArray());
                    var actual = JsonSerializer.Serialize(Query(includePrograms, id));
                    Require(expected == actual, $"Selected physical entity {id} changed ordered complete action JSON at {boundary}, programs={includePrograms}.");
                    Require(typeof(GameEngine).GetField("_actionQuery", flags)!.GetValue(game) is null,
                        "A narrow query must release its local action-query scope.");
                }
                Require(JsonSerializer.Serialize(wide) == JsonSerializer.Serialize(Query(includePrograms, null)),
                    "A selected entity must not contaminate the next complete-pool action query.");
            }
            Require(before == Observe(), $"Physical queries changed revision, four player views, frames, events, accepted commands, movements or zones at {boundary}.");
            return complete!;
        }
        string Observe() => JsonSerializer.Serialize(new
        {
            game.Revision,
            Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray(),
            Frames = JsonSerializer.Serialize(game.ResolutionStack),
            Events = ProgramCompositionEntryChecks.Events(game),
            Commands = CommandJson.Serialize(game.AcceptedCommands), game.CardMovements,
            Zones = game.CreateCardZoneDiagnostics()
        });
    }

    private sealed class PhysicalQueryPackage : IGameContentPackage
    {
        internal const string Skill = "fixture:physical-query", Owner = "fixture:physical-query-owner", Mode = "identity:classic-physical-query";
        public PackageManifest Manifest { get; } = new("physical-query", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Skill}}","revision":1,"viewAs":[
              {"id":"to-slash","inputKinds":[],"inputSuits":[],"sourceZones":["hand","equipment"],"outputKind":"slash","forPlay":true,"forResponse":false},
              {"id":"fire-chain","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","allowChainedInput":true,"forPlay":true,"forResponse":false},
              {"id":"authority-snatch","inputKinds":[],"inputSuits":[],"sourceZones":["authority"],"outputKind":"snatch","forPlay":true,"forResponse":false},
              {"id":"judgment-slash","inputKinds":[],"inputSuits":[],"sourceZones":["judgment"],"lastInSourceZone":true,"outputKind":"slash","forPlay":true,"forResponse":true},
              {"id":"borrowed","inputKinds":["dodge"],"inputSuits":[],"outputKind":"borrowedSword","singleCardTrickUse":true,"forPlay":true,"forResponse":false}]}]}
            """, JsonSerializer.Serialize(new
            {
                schemaVersion = 3,
                skills = new Dictionary<string, object>
                {
                    [Skill] = new { name = "Physical query", description = "Complete owned pools" }
                }
            }));
            builder.AddSkill(new(Skill, "Physical query", "Real source pools") { Program = catalog.Programs[Skill] });
            builder.AddGeneral(new(Owner, "Physical query owner", "supporter", Skill, "wei", 20));
            foreach (var seat in new[] { 1, 2, 3 })
                builder.AddGeneral(new($"fixture:physical-query-peer-{seat}", "Query peer", "supporter", "standard:none", "shu", 20));
            var cards = new[] { "standard:slash", "standard:dodge", "standard:iron_chain", "classic:borrowed-sword",
                "classic:fangtian-halberd", "classic:wooden-ox", "standard:crossbow", "standard:indulgence", "standard:lightning" };
            builder.AddDeck(new("fixture:physical-query-deck", "Finite query materials", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(index => new ContentDeckPhysicalCard(cards[index % cards.Length], Suit.Heart, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "Physical query", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:physical-query-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, "fixture:physical-query-peer-1", "fixture:physical-query-peer-2", "fixture:physical-query-peer-3"]));
        }
    }

    private static SkillProgram LoadTwinBindings(int drawAmount = 1) => SkillProgramCatalog.Load($$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:compiled-bindings","revision":1,
          "activations":[{"id":"same","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
            "effects":[{"op":"draw","target":"owner","amount":{{drawAmount}}},{"op":"recover","target":"owner","amount":1}]}],
          "triggers":[{"id":"same","window":"turnEnding","subject":"owner","optional":true,
            "effects":[{"op":"recover","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]}]}]}
        """, """{"schemaVersion":3,"skills":{"fixture:compiled-bindings":{"name":"Bindings","description":"Source identity"}}}""")
        .Programs["fixture:compiled-bindings"];

    private static bool ContainsCondition(SkillProgramTriggerCondition condition, SkillProgramTriggerConditionKind kind) =>
        condition.Kind == kind || condition.Children.Any(child => ContainsCondition(child, kind));
    private static bool ContainsValue(SkillProgramTriggerCondition condition, SkillProgramTriggerValueKind kind) =>
        condition.Left?.Kind == kind || condition.Right?.Kind == kind || condition.Children.Any(child => ContainsValue(child, kind));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class ProgramOnlyPackage(SkillProgram program) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("compiled-program-only", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => builder.AddSkill(new(program.Id, "Bindings", "Unowned opt-in") { Program = program });
    }
}
