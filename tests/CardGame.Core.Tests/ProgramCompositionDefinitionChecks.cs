using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCompositionDefinitionChecks
{
    public static void CurrentExecutableContentUsesCompositionKernel()
    {
        var registries = new[]
        {
            StandardContentRegistry.CreateWithClassicGenerals(),
            ComposedSkillContentRegistry.CreateShowcase()
        };
        var programs = registries.SelectMany(registry => registry.Skills.Values)
            .Select(definition => definition.Program).OfType<SkillProgram>()
            .Where(program => program.Activations.Count + program.Triggers.Count > 0 &&
                program.Triggers.All(trigger => trigger.UsesSharedExecutor))
            .DistinctBy(program => program.Id).ToArray();
        if (programs.Length == 0 || programs.Any(program => !program.UsesCompositionKernel ||
                program.MinimumRulesVersion < 128 || program.MinimumRulesVersion > GameCheckpoint.CurrentRulesVersion))
            throw new InvalidOperationException("Current content with shared execution entries must use the composition kernel: " +
                string.Join(", ", programs.Where(program => !program.UsesCompositionKernel).Select(program => program.Id)));
    }

    public static void AiPoliciesFollowResourcePartitionsAndCosts()
    {
        var player = new PlayerSkillContext(0, 4, 4, 0, TurnPhase.Play, IsOwnTurn: true);
        ProgramAiEstimate Estimate(string effects, bool faceDown = false)
        {
            var program = Load("fixture:ai", Rules("fixture:ai", effects, Entries(effects, false)));
            return ProgramCompositionAi.Estimate(program.Activations.Single().Effects, player, faceDown);
        }
        var noGain = Estimate("""
        [{"op":"draw","target":"owner","amount":2,"resultBind":"d"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"d","destination":"discardPile"}]
        """);
        Require(noGain.Score == 0 && noGain.Hint.OwnerDraw == 0,
            "Drawing and discarding the same new cards must not create imaginary hand gain.");
        var split = Estimate("""
        [{"op":"revealTopCards","target":"owner","amount":4,"resultBind":"r","visibility":"public"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"n","suits":["spade","club","diamond"]},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","exceptBind":"h","destination":"ownerHand"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","exceptBind":"n","destination":"discardPile"}]
        """);
        Require(split.Hint.OwnerDraw == 3 && split.Score == 24,
            "Moving one partition must not change the estimated origin of the still-unmoved partition.");
        var lethal = Estimate("""
        [{"op":"loseHp","target":"owner","amount":4},{"op":"recover","target":"owner","amount":4}]
        """);
        Require(lethal.IsSelfLethal, "Recovery after a potentially fatal step must not erase the dying boundary.");
        const string faceDown = """[{"op":"setFaceState","target":"owner","faceDown":true}]""";
        Require(Estimate(faceDown).Score < 0 && Estimate(faceDown, true).Score == 0,
            "Setting a face state must differ from toggling or repeatedly charging for an unchanged state.");
        var actorDraw = Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","target":"actor","amount":2}""");
        var ownerActor = ProgramCompositionAi.Estimate([actorDraw], player,
            publicContext: new ProgramAiPublicContext(0, CardActionActorIsOwner: true));
        var otherActor = ProgramCompositionAi.Estimate([actorDraw], player,
            publicContext: new ProgramAiPublicContext(0, CardActionActorIsOwner: false));
        Require(ownerActor.Hint is { OwnerDraw: 2, TargetDraw: 0 } &&
                otherActor.Hint is { OwnerDraw: 0, TargetDraw: 2 },
            "Actor-target AI must count an owner actor exactly once and retain a distinct external actor target.");
    }

    public static void CatalogDiscoversCompleteOperations()
    {
        var nodes = new Dictionary<SkillProgramEffectOp, string>
        {
            [SkillProgramEffectOp.Draw] = """{"op":"draw","target":"owner","amount":2,"resultBind":"drawn"}""",
            [SkillProgramEffectOp.Recover] = """{"op":"recover","target":"owner","amount":1}""",
            [SkillProgramEffectOp.LoseHp] = """{"op":"loseHp","target":"owner","amount":1}""",
            [SkillProgramEffectOp.RevealTopCards] = """{"op":"revealTopCards","target":"owner","amount":4,"resultBind":"shown","visibility":"public"}""",
            [SkillProgramEffectOp.FilterBoundCards] = """{"op":"filterBoundCards","target":"owner","sourceBind":"shown","resultBind":"hearts","suits":["heart"]}""",
            [SkillProgramEffectOp.SelectCardSubset] = """{"op":"selectCardSubset","target":"owner","sourceBind":"shown","resultBind":"picked","minimumCards":0,"maximumCards":2,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"}""",
            [SkillProgramEffectOp.MoveBoundCards] = """{"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"discardPile"}""",
            [SkillProgramEffectOp.GiveBoundCard] = """{"op":"giveBoundCard","target":"owner","sourceBind":"drawn","targetKind":"otherLiving"}""",
            [SkillProgramEffectOp.SelectTarget] = """{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}""",
            [SkillProgramEffectOp.TurnOver] = """{"op":"turnOver","target":"owner"}""",
            [SkillProgramEffectOp.SetFaceState] = """{"op":"setFaceState","target":"owner","faceDown":true}""",
            [SkillProgramEffectOp.GiveSelected] = """{"op":"giveSelected","target":"selectedTarget","amount":1}""",
            [SkillProgramEffectOp.DiscardSelected] = """{"op":"discardSelected","target":"owner","amount":1}""",
            [SkillProgramEffectOp.InsertPhase] = """{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}""",
            [SkillProgramEffectOp.RecoverTo] = """{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}""",
            [SkillProgramEffectOp.SelectTargets] = """{"op":"selectTargets","target":"owner","targetKind":"otherLivingWithHand","minimumTargets":1,"maximumTargets":2,"targetAiOrder":"hostileThenHandCount"}""",
            [SkillProgramEffectOp.SelectSourceCard] = """{"op":"selectSourceCard","target":"owner","zones":["hand","equipment"],"resultBind":"source"}""",
            [SkillProgramEffectOp.ClaimDamageCards] = """{"op":"claimDamageCards","target":"owner"}""",
            [SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets] = """{"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1}""",
            [SkillProgramEffectOp.AdjustNormalDraw] = """{"op":"adjustNormalDraw","target":"owner","amount":-1}""",
            [SkillProgramEffectOp.GrantTurnCardDamageModifier] = """{"op":"grantTurnCardDamageModifier","target":"owner","amount":1,"cardKinds":["slash","duel"]}""",
            [SkillProgramEffectOp.GrantTurnCardActionProhibition] = """{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash"],"actionTypes":["use","response"]}""",
            [SkillProgramEffectOp.GrantTurnRuleModifier] = """{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":1}""",
            [SkillProgramEffectOp.GrantTurnCardTargetRestriction] = """{"op":"grantTurnCardTargetRestriction","target":"owner","targetRestriction":"selfOnly"}""",
            [SkillProgramEffectOp.StartJudgment] = """{"op":"startJudgment","target":"owner","judgmentReason":"fixture.catalog","resultBind":"judgment","visibility":"public"}""",
            [SkillProgramEffectOp.GrantTurnCardConversion] = """{"op":"grantTurnCardConversion","target":"owner","sourceBind":"judgment","colorRelation":"oppositeBoundCard","outputKind":"duel"}""",
            [SkillProgramEffectOp.DiscardOwnedZoneCards] = """{"op":"discardOwnedZoneCards","target":"owner","zones":["hand","equipment","judgment"]}""",
            [SkillProgramEffectOp.SetChainedState] = """{"op":"setChainedState","target":"owner","chained":false}""",
            [SkillProgramEffectOp.SelectAndMoveOwnedCard] = """{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"actor"},"zones":["hand"],"count":1,"destination":"discardPile","resultBind":"paid"}""",
            [SkillProgramEffectOp.RefundCardUseDebit] = """{"op":"refundCardUseDebit","target":"owner"}""",
            [SkillProgramEffectOp.StartPindian] = """{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}""",
            [SkillProgramEffectOp.SetBooleanState] = """{"op":"setBooleanState","target":"owner","stateId":"ready","value":true}""",
            [SkillProgramEffectOp.ToggleBooleanState] = """{"op":"toggleBooleanState","target":"owner","stateId":"ready"}""",
            [SkillProgramEffectOp.GrantDirectedTurnCardPolicy] = """{"op":"grantDirectedTurnCardPolicy","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"effects":["ignoreDistance"]}""",
            [SkillProgramEffectOp.Damage] = """{"op":"damage","target":"selectedTarget","amount":1}""",
            [SkillProgramEffectOp.Pindian] = """{"op":"pindian","target":"selectedTarget","amount":1}""",
            [SkillProgramEffectOp.ChangeMaximumHp] = """{"op":"changeMaximumHp","target":"owner","amount":-1}""",
            [SkillProgramEffectOp.GrantSkills] = """{"op":"grantSkills","target":"owner","skillIds":["classic:paiyi"]}"""
        };
        Require(nodes.Keys.ToHashSet().SetEquals(Enum.GetValues<SkillProgramEffectOp>()),
            "Catalog parse fixtures must cover every declared program operation exactly once.");
        var descriptors = new List<IProgramOperationDescriptor>();
        foreach (var (op, json) in nodes)
        {
            var effect = Parse(ProgramOperationCatalog.Default, json);
            var descriptor = ProgramOperationCatalog.Default.Resolve(op);
            Require(effect.Op == op && descriptor.Op == op && descriptor.Handler.Op == op,
                $"Descriptor/parse/handler mismatch for {op}.");
            descriptors.Add(descriptor);
        }
        var handlers = new SkillProgramEffectCatalog(descriptors.Select(item => item.Handler));
        foreach (var descriptor in descriptors)
            Require(ReferenceEquals(handlers.Resolve(descriptor.Op), descriptor.Handler),
                $"Execution catalog did not preserve {descriptor.Op}'s registered handler.");

        Reject(() => NewCatalog([.. descriptors, descriptors[0]]), "Duplicate");
        Reject(() => NewCatalog([new WrongHandlerDescriptor()]), "mismatched handler");
        const string drawPlanOnly = """[{"op":"adjustNormalDraw","target":"owner","amount":1}]""";
        Reject(() => Load("fixture:missing-draw-context", Rules("fixture:missing-draw-context",
            drawPlanOnly, Entries(drawPlanOnly, includeTriggers: false))), "requires context DrawPlan");

        var replacement = new ReplacementDrawDescriptor();
        var extended = NewCatalog([replacement, .. descriptors.Where(item => item.Op != SkillProgramEffectOp.Draw)]);
        var extendedEffect = Parse(extended, """{"op":"draw","target":"owner","amount":3}""");
        Require(extendedEffect.Amount == 3 && replacement.ParseCalls == 1 &&
                ReferenceEquals(extended.Resolve(SkillProgramEffectOp.Draw), replacement),
            "Replacing one existing enum operation with a local descriptor did not route through the extension point.");
    }

    public static void EquivalentEntriesCompileSameEffects()
    {
        const string effects = """
        [{"op":"draw","target":"owner","amount":2},
         {"op":"recover","target":"owner","amount":1},
         {"op":"turnOver","target":"owner"}]
        """;
        var rules = Rules("fixture:equivalent", effects, Entries(effects, includeTriggers: true));
        var program = Load("fixture:equivalent", rules);
        var active = program.Activations.Single().Effects.Select(Project).ToArray();
        var play = program.Triggers.Single(item => item.Id == "play").Effects
            .Select(item => Project(item.ToExecutionEffect())).ToArray();
        var turn = program.Triggers.Single(item => item.Id == "turn").Effects
            .Select(item => Project(item.ToExecutionEffect())).ToArray();
        Require(active.SequenceEqual(play) && active.SequenceEqual(turn),
            "Equivalent schema 23 nodes compiled differently across activation/playEnding/turnEnding.");
        Require(!string.IsNullOrWhiteSpace(program.GameplayHash), "The composed program has no gameplay hash.");
    }

    public static void ResourceGraphsRejectAliasingLeaksAndMissingInputs()
    {
        Accept("partition", """
        [{"op":"revealTopCards","target":"owner","amount":4,"resultBind":"r","visibility":"public"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},
         {"op":"selectCardSubset","target":"owner","sourceBind":"h","resultBind":"p","minimumCards":0,"maximumCards":2,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"p","destination":"ownerHand"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"h","exceptBind":"p","destination":"discardPile"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","exceptBind":"h","destination":"discardPile"}]
        """);
        Accept("historical count", """
        [{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"},
         {"op":"recover","target":"owner","numberExpression":"boundCardCount","sourceBind":"r"}]
        """);
        RejectEffects("duplicate move", """
        [{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"ownerHand"}]
        """, "more than once");
        RejectEffects("overlapping alias move", """
        [{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},
         {"op":"moveBoundCards","target":"owner","sourceBind":"h","destination":"ownerHand"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"}]
        """, "more than once");
        RejectEffects("cross root exclusion", """
        [{"op":"revealTopCards","target":"owner","amount":1,"resultBind":"a","visibility":"public"},
         {"op":"revealTopCards","target":"owner","amount":1,"resultBind":"b","visibility":"public"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"a","exceptBind":"b","destination":"discardPile"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"a","destination":"discardPile"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"b","destination":"discardPile"}]
        """, "same source root");
        RejectEffects("reverse non-subset", """
        [{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},
         {"op":"moveBoundCards","target":"owner","sourceBind":"h","exceptBind":"r","destination":"discardPile"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"}]
        """, "subset");
        RejectEffects("unfinished reveal", """
        [{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},
         {"op":"moveBoundCards","target":"owner","sourceBind":"h","destination":"ownerHand"}]
        """, "not fully consumed");
        RejectEffects("undefined input", """
        [{"op":"recover","target":"owner","numberExpression":"boundCardCount","sourceBind":"missing"}]
        """, "unknown card binding");
        RejectEffects("conditional producer", """
        [{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public","condition":{"kind":"wounded"}},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"}]
        """, "must be always");
        RejectEffects("gift then filter", """
        [{"op":"draw","target":"owner","amount":2,"resultBind":"d"},
         {"op":"giveBoundCard","target":"owner","sourceBind":"d","targetKind":"otherLiving"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"d","resultBind":"h","suits":["heart"]}]
        """, "may already have moved");
        RejectEffects("gift then move", """
        [{"op":"draw","target":"owner","amount":2,"resultBind":"d"},
         {"op":"giveBoundCard","target":"owner","sourceBind":"d","targetKind":"otherLiving"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"d","destination":"discardPile"}]
        """, "more than once");
        RejectEffects("subset after partial consumption", """
        [{"op":"revealTopCards","target":"owner","amount":4,"resultBind":"r","visibility":"public"},
         {"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},
         {"op":"moveBoundCards","target":"owner","sourceBind":"h","destination":"ownerHand"},
         {"op":"selectCardSubset","target":"owner","sourceBind":"r","resultBind":"p","minimumCards":0,"maximumCards":2,"maximumRankSum":13,"aiOrder":"mostCardsThenRankSum"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","exceptBind":"h","destination":"discardPile"}]
        """, "already have moved");
        RejectEffects("unselected target", """
        [{"op":"turnOver","target":"selectedTarget"}]
        """, "selectedTarget");
        RejectSelectedCardEffects("duplicate selected-card consumption", """
        [{"op":"discardSelected","target":"owner","amount":1},
         {"op":"discardSelected","target":"owner","amount":1}]
        """, "exactly once");
        RejectTriggerEffects("trigger selected-card consumption", """
        [{"op":"discardSelected","target":"owner","amount":1}]
        """, "this operation consumes activation input and cannot be used by a trigger");
    }

    public static void MalformedNodesFailBeforeExecution()
    {
        var missing = new[]
        {
            """{"op":"draw","target":"owner"}""",
            """{"op":"recover","target":"owner"}""",
            """{"op":"loseHp","target":"owner"}""",
            """{"op":"revealTopCards","target":"owner","amount":1,"visibility":"public"}""",
            """{"op":"filterBoundCards","target":"owner","sourceBind":"r","suits":["heart"]}""",
            """{"op":"selectCardSubset","target":"owner","sourceBind":"r","resultBind":"s","minimumCards":0,"maximumCards":1,"aiOrder":"mostCardsThenRankSum"}""",
            """{"op":"moveBoundCards","target":"owner","destination":"discardPile"}""",
            """{"op":"giveBoundCard","target":"owner","sourceBind":"r"}""",
            """{"op":"selectTarget","target":"owner"}""",
            """{"op":"turnOver"}""",
            """{"op":"setFaceState","target":"owner"}""",
            """{"op":"giveSelected","target":"selectedTarget"}""",
            """{"op":"discardSelected","target":"owner"}"""
        };
        foreach (var node in missing) Reject(() => Parse(ProgramOperationCatalog.Default, node), "missing required property");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","target":"Owner","amount":1}"""), "unsupported");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","target":"owner","amount":"1"}"""), "must be Number");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","op":"draw","target":"owner","amount":1}"""), "duplicate property");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"recover","target":"owner","amount":1,"numberExpression":"boundCardCount","sourceBind":"r"}"""), "amount or numberExpression");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"selectedTargetHand"}"""), "unsupported destination");

        const string oldEffects = """[{"op":"draw","target":"owner","amount":21}]""";
        var old = Rules("fixture:legacy20", oldEffects,
            Entries(oldEffects, includeTriggers: false), schema: 20, minimumRules: 125);
        Require(Load("fixture:legacy20", old).Activations.Single().Effects.Single().Amount == 21,
            "Schema 20 activation draw limit changed while adding schema 23 descriptors.");
        Reject(() => Load("fixture:new23", Rules("fixture:new23", oldEffects,
            Entries(oldEffects, includeTriggers: false))), "between 1 and 20");

        const string cardEffects = """[{"op":"refundCardUseDebit","target":"owner"},{"op":"draw","target":"owner","amount":1,"condition":{"kind":"cardUseIsRed"}}]""";
        var cardTrigger = $$"""
        {"id":"card","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],
         "optional":true,"effects":{{cardEffects}}}
        """;
        var cardProgram = Load("fixture:card24", Rules("fixture:card24", cardEffects,
            $"\"activations\":[],\"triggers\":[{cardTrigger}]", schema: 24, minimumRules: 129));
        Require(cardProgram.RuntimeVersion == "skill-program-v24" && cardProgram.MinimumRulesVersion == 129 &&
                cardProgram.Triggers.Single().UsesSharedExecutor,
            "Schema 24 card-action programs must use the shared executor without changing schema 23.");
        const string actorEffects = """[{"op":"draw","target":"actor","amount":1}]""";
        var actorTrigger = $$"""
        {"id":"actor-card","window":"cardUseCommitted","ownerRelation":"observer","cardKinds":["slash"],
         "optional":true,"effects":{{actorEffects}}}
        """;
        var actorProgram = Load("fixture:actor24", Rules("fixture:actor24", actorEffects,
            $"\"activations\":[],\"triggers\":[{actorTrigger}]", schema: 24, minimumRules: 129));
        var actorTriggerEffect = actorProgram.Triggers.Single().Effects.Single();
        Require(actorTriggerEffect.Target == SkillProgramTriggerEffectTarget.Actor &&
                actorTriggerEffect.ToExecutionEffect().Target == SkillProgramEffectTarget.Actor,
            "Trigger actor targets must map bidirectionally without becoming selectedTarget.");
        Reject(() => Load("fixture:actor-activation", Rules("fixture:actor-activation", actorEffects,
            Entries(actorEffects, includeTriggers: false), schema: 24, minimumRules: 129)),
            "requires context CardAction");
        Reject(() => Load("fixture:actor20", Rules("fixture:actor20", actorEffects,
            Entries(actorEffects, includeTriggers: false), schema: 20, minimumRules: 125)),
            "schema 24 card-action composition");
        Reject(() => Load("fixture:card23", Rules("fixture:card23", cardEffects,
            $"\"activations\":[],\"triggers\":[{cardTrigger}]")), "schema version 24");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"resultSource"},"cardOwnerRef":{"kind":"actor"},"zones":["hand"],"count":1,"destination":"discardPile"}"""),
            "resultBind");
    }

    private static SkillProgramEffect Parse(ProgramOperationCatalog catalog, string json)
    {
        using var document = JsonDocument.Parse(json);
        return catalog.Parse(document.RootElement, "fixture.effects[0]", ParseCondition);
    }

    private static SkillProgramCondition ParseCondition(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Object && node.EnumerateObject().Count() == 1 &&
            node.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String)
        {
            var parsed = kind.GetString() switch
            {
                "always" => SkillProgramConditionKind.Always,
                "wounded" => SkillProgramConditionKind.Wounded,
                _ => throw new InvalidOperationException($"Invalid skill program at {path}: unsupported condition.")
            };
            return new(parsed, 0, []);
        }
        throw new InvalidOperationException($"Invalid skill program at {path}: malformed condition.");
    }

    private static ProgramOperationCatalog NewCatalog(IEnumerable<IProgramOperationDescriptor> descriptors) =>
        (ProgramOperationCatalog)(typeof(ProgramOperationCatalog).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(IEnumerable<IProgramOperationDescriptor>)], null)?.Invoke([descriptors])
            ?? throw new InvalidOperationException("Injectable catalog constructor unavailable."));

    private static void Accept(string id, string effects) =>
        _ = Load("fixture:" + id.Replace(' ', '-'), Rules("fixture:" + id.Replace(' ', '-'), effects,
            Entries(effects, includeTriggers: false)));

    private static void RejectEffects(string id, string effects, string expected) => Reject(() =>
        Accept(id, effects), expected);

    private static void RejectSelectedCardEffects(string id, string effects, string expected)
    {
        var skillId = "fixture:" + id.Replace(' ', '-');
        var activation = $$"""
        {"id":"active","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,
         "targetKind":"anyLiving","usesPerTurn":1,"effects":{{effects}}}
        """;
        Reject(() => Load(skillId, Rules(skillId, effects,
            $"\"activations\":[{activation}],\"triggers\":[]")), expected);
    }

    private static void RejectTriggerEffects(string id, string effects, string expected)
    {
        var skillId = "fixture:" + id.Replace(' ', '-');
        Reject(() => Load(skillId, Rules(skillId, effects,
            $"\"activations\":[],\"triggers\":[{Trigger("trigger", "playEnding", effects)}]")), expected);
    }

    private static SkillProgram Load(string id, string rules) => SkillProgramCatalog.Load(rules,
        JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            skills = new Dictionary<string, object> { [id] = new { name = "Fixture", description = "Fixture" } }
        })).Programs[id];

    private static string Rules(string id, string effects, string entries, int schema = 23, int minimumRules = 128) => $$"""
    {"schemaVersion":{{schema}},"skills":[{"id":"{{id}}","revision":1,"minimumRulesVersion":{{minimumRules}},
    "modifiers":[],"viewAs":[],{{entries}},"contributions":[],"cardIdentities":[]}]}
    """;

    private static string Activation(string id, string effects) => $$"""
    {"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
     "targetKind":"anyLiving","usesPerTurn":1,"effects":{{effects}}}
    """;

    private static string Trigger(string id, string window, string effects) => $$"""
    {"id":"{{id}}","window":"{{window}}","subject":"owner","optional":true,"priority":0,"effects":{{effects}}}
    """;

    private static string Entries(string effects, bool includeTriggers) => includeTriggers
        ? $"\"activations\":[{Activation("active", effects)}],\"triggers\":[{Trigger("play", "playEnding", effects)},{Trigger("turn", "turnEnding", effects)}]"
        : $"\"activations\":[{Activation("active", effects)}],\"triggers\":[]";

    private static string Project(SkillProgramEffect effect) => JsonSerializer.Serialize(new
    {
        effect.Op, effect.Target, effect.Amount, Condition = effect.Condition.Kind, effect.NumberExpression,
        effect.SourceBind, effect.ResultBind, effect.ExceptBind, effect.Visibility, effect.MinimumCards,
        effect.MaximumCards, effect.MaximumRankSum, effect.AiOrder, effect.Destination, effect.FaceDown,
        Zones = effect.Zones.ToArray(), Suits = effect.Suits.ToArray(), effect.TargetKind
    });

    private static void Reject(Action action, string expected)
    {
        try { action(); }
        catch (Exception exception) when (Unwrap(exception).Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ReplacementDrawDescriptor : IProgramOperationDescriptor
    {
        public int ParseCalls { get; private set; }
        public SkillProgramEffectOp Op => SkillProgramEffectOp.Draw;
        public ISkillProgramEffectHandler Handler { get; } = new DrawSkillProgramEffectHandler();
        public ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
        public ProgramContextCapability RequiredCapabilities => ProgramContextCapability.None;
        public ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(effect));
        public SkillProgramEffect Parse(ProgramOperationNodeReader reader)
        {
            ParseCalls++;
            reader.AllowOnly("op", "target", "amount");
            return new(Op, reader.RequiredEnum<SkillProgramEffectTarget>("target"),
                reader.RequiredInt("amount"), new(SkillProgramConditionKind.Always, 0, []));
        }
        public IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    }

    private sealed class WrongHandlerDescriptor : IProgramOperationDescriptor
    {
        public SkillProgramEffectOp Op => SkillProgramEffectOp.Draw;
        public ISkillProgramEffectHandler Handler { get; } = new RecoverSkillProgramEffectHandler();
        public ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
        public ProgramContextCapability RequiredCapabilities => ProgramContextCapability.None;
        public ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(effect));
        public SkillProgramEffect Parse(ProgramOperationNodeReader reader) => throw new NotSupportedException();
        public IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    }
}
