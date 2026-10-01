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
