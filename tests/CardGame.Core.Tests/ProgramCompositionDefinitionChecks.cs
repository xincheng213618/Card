using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCompositionDefinitionChecks
{
    public static void CurrentExecutableContentHasOneExecutionPlan()
    {
        var registries = new[]
        {
            StandardContentRegistry.CreateWithClassicGenerals(),
            ComposedSkillContentRegistry.CreateShowcase()
        };
        var programs = registries.SelectMany(registry => registry.Skills.Values)
            .Select(definition => definition.Program).OfType<SkillProgram>()
            .Where(program => program.Activations.Count > 0 || program.Triggers.Count > 0)
            .DistinctBy(program => program.Id).ToArray();
        Require(programs.Length > 0, "Current content must contain executable programs.");
        foreach (var program in programs)
        {
            Require(program.RuntimeVersion == SkillProgramCatalog.RuntimeVersion &&
                    program.MinimumRulesVersion is >= 171 and <= GameCheckpoint.CurrentRulesVersion,
                $"Program {program.Id} must use the current contract.");
            foreach (var activation in program.Activations)
                Require(ProgramInstructionResolver.Default.Resolve(program, ProgramInstructionSourceKind.Activation,
                    activation.Id).Instructions.SequenceEqual(activation.Effects),
                    $"Activation {program.Id}/{activation.Id} must resolve directly to its instructions.");
            foreach (var trigger in program.Triggers)
                Require(ProgramInstructionResolver.Default.Resolve(program, ProgramInstructionSourceKind.Trigger,
                    trigger.Id).Instructions.SequenceEqual(trigger.Effects),
                    $"Trigger {program.Id}/{trigger.Id} must resolve directly to its instructions.");
        }
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
        var nullify = Parse(ProgramOperationCatalog.Default,
            """{"op":"nullifyCurrentCardEffect","target":"owner"}""");
        var loseOneHp = Parse(ProgramOperationCatalog.Default,
            """{"op":"loseHp","target":"owner","amount":1}""");
        var harmfulNullify = ProgramCompositionAi.Estimate([nullify, loseOneHp], player,
            publicContext: new ProgramAiPublicContext(0, CardUseEffectiveKind: CardKind.Slash));
        var harmlessNullify = ProgramCompositionAi.Estimate([nullify, loseOneHp], player,
            publicContext: new ProgramAiPublicContext(0, CardUseEffectiveKind: CardKind.DrawTwo));
        var lethalNullify = ProgramCompositionAi.Estimate([nullify, loseOneHp],
            player with { Hp = 1 },
            publicContext: new ProgramAiPublicContext(0, CardUseEffectiveKind: CardKind.Slash));
        Require(harmfulNullify.Score > 0 && harmlessNullify.Score < 0 && lethalNullify.IsSelfLethal,
            "Card-effect nullification AI must pay HP only for harmful effects and retain the shared self-lethal guard.");
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
            [SkillProgramEffectOp.DistributeOwnedCards] = """{"op":"distributeOwnedCards","target":"owner","zones":["hand"],"numberExpression":"boundCardCount","sourceBind":"drawn","targetKind":"otherLiving","allowDeclineBeforeFirst":true}""",
            [SkillProgramEffectOp.RequestAttackRangeAid] = """{"op":"requestAttackRangeAid","target":"selectedTarget"}""",
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
            [SkillProgramEffectOp.ReplaceJudgment] = """{"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade","heart","club","diamond"],"oldCardDestination":"discardPile"}""",
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
            [SkillProgramEffectOp.GrantSkills] = """{"op":"grantSkills","target":"owner","skillIds":["classic:paiyi"]}""",
            [SkillProgramEffectOp.ChooseOption] = """{"op":"chooseOption","target":"owner","resultBind":"answer","options":[{"id":"yes","condition":{"kind":"always"}}]}""",
            [SkillProgramEffectOp.SelectOwnedCards] = """{"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand","equipment"],"resultBind":"selected"}""",
            [SkillProgramEffectOp.GrantTurnHandColorRestriction] = """{"op":"grantTurnHandColorRestriction","target":"selectedTarget","sourceBind":"selected"}""",
            [SkillProgramEffectOp.PreventCurrentDamage] = """{"op":"preventCurrentDamage","target":"owner"}""",
            [SkillProgramEffectOp.CaptureSelectedCards] = """{"op":"captureSelectedCards","target":"owner","resultBind":"captured"}""",
            [SkillProgramEffectOp.RevealBoundCards] = """{"op":"revealBoundCards","target":"owner","sourceBind":"captured"}""",
            [SkillProgramEffectOp.ChooseDifferentCategoryDiscard] = """{"op":"chooseDifferentCategoryDiscard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"sourceBind":"captured","resultBind":"response"}""",
            [SkillProgramEffectOp.UseSelectedCardsAs] = """{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-as-slash","outputKind":"slash"}""",
            [SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick] = """{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all-hand-trick"}""",
            [SkillProgramEffectOp.GrantTurnSkills] = """{"op":"grantTurnSkills","target":"owner","skillIds":["classic:wusheng","classic:paoxiao"]}""",
            [SkillProgramEffectOp.ChangeAttributedMarker] = """{"op":"changeAttributedMarker","target":"owner","targetRef":{"kind":"eventSource"},"marker":"nightmare","amount":1}""",
            [SkillProgramEffectOp.CauseDeathUnlessBoundCardKind] = """{"op":"causeDeathUnlessBoundCardKind","target":"selectedTarget","sourceBind":"judgment","excludedCardKinds":["peach","peachGarden"]}""",
            [SkillProgramEffectOp.UseBoundCardAsDyingAlcohol] = """{"op":"useBoundCardAsDyingAlcohol","target":"owner","sourceBind":"rescue-card"}""",
            [SkillProgramEffectOp.ChooseOtherOwnedCardDiscard] = """{"op":"chooseOtherOwnedCardDiscard","target":"owner","chooserRef":{"kind":"owner"},"zones":["hand","equipment","judgment"],"condition":{"kind":"always"}}""",
            [SkillProgramEffectOp.NullifyCurrentCardEffect] = """{"op":"nullifyCurrentCardEffect","target":"owner"}""",
            [SkillProgramEffectOp.NullifySelectedCardEffects] = """{"op":"nullifySelectedCardEffects","target":"owner"}""",
            [SkillProgramEffectOp.StartVirtualDuel] = """{"op":"startVirtualDuel","target":"owner"}""",
            [SkillProgramEffectOp.RequestFactionCard] = """{"op":"requestFactionCard","target":"selectedTarget","providerFactionId":"shu","requiredKind":"slash"}""",
            [SkillProgramEffectOp.TransferRandomOwnedCard] = """{"op":"transferRandomOwnedCard","target":"selectedTarget","resultBind":"publicGift"}""",
            [SkillProgramEffectOp.AccumulateSelectedCardCount] = """{"op":"accumulateSelectedCardCount","target":"owner","usageId":"phase-count","threshold":2,"resultBind":"crossed"}""",
            [SkillProgramEffectOp.ClaimJudgmentCard] = """{"op":"claimJudgmentCard","target":"owner"}""",
            [SkillProgramEffectOp.ReorderTopCards] = """{"op":"reorderTopCards","target":"owner","amount":5,"numberExpression":"livingPlayerCount"}""",
            [SkillProgramEffectOp.RepeatJudgment] = """{"op":"repeatJudgment","target":"owner","judgmentReason":"skill.luoshen","resultBind":"repeated-judgment","suits":["spade","club"]}""",
            [SkillProgramEffectOp.SkipTurnPhases] = """{"op":"skipTurnPhases","target":"owner","phases":["judgment","draw"]}""",
            [SkillProgramEffectOp.UseVirtualCard] = """{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}""",
            [SkillProgramEffectOp.RevealUniqueRankForDying] = """{"op":"revealUniqueRankForDying","target":"owner","zone":"buquWound","rescueHp":1}""",
            [SkillProgramEffectOp.ProhibitCurrentResponse] = """{"op":"prohibitCurrentResponse","target":"owner"}""",
            [SkillProgramEffectOp.RedirectCurrentAttack] = """{"op":"redirectCurrentAttack","target":"selectedTarget"}""",
            [SkillProgramEffectOp.RedirectCurrentDamage] = """{"op":"redirectCurrentDamage","target":"selectedTarget","sourceBind":"cost","drawLostHpAfterDamage":true}""",
            [SkillProgramEffectOp.HoldTargetCards] = """{"op":"holdTargetCards","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"eventTarget"},"zones":["hand","equipment"],"resultBind":"pojun-hold","minimumCards":1}""",
            [SkillProgramEffectOp.RevealTargetHandCard] = """{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"qiangzhi-shown","mode":"chooser"}""",
            [SkillProgramEffectOp.UseBoundCardByTarget] = """{"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"gift"}""",
            [SkillProgramEffectOp.PendExtraTurn] = """{"op":"pendExtraTurn","target":"owner"}""",
            [SkillProgramEffectOp.ClaimDeathCleanupCards] = """{"op":"claimDeathCleanupCards","target":"owner"}""",
            [SkillProgramEffectOp.GrantTurnHandCardProhibition] = """{"op":"grantTurnHandCardProhibition","target":"selectedTarget"}""",
            [SkillProgramEffectOp.AbolishOwnerAreas] = """{"op":"abolishOwnerAreas","target":"owner","zones":["equipment","judgment"]}""",
            [SkillProgramEffectOp.LoseDeathSourceSkills] = """{"op":"loseDeathSourceSkills","target":"owner"}""",
            [SkillProgramEffectOp.ChooseOwnCardDiscard] = """{"op":"chooseOwnCardDiscard","target":"owner","chooserRef":{"kind":"eventSource"},"zones":["hand","equipment"],"condition":{"kind":"always"}}""",
            [SkillProgramEffectOp.ExchangeSelectedTargetHands] = """{"op":"exchangeSelectedTargetHands","target":"owner","condition":{"kind":"always"}}""",
            [SkillProgramEffectOp.RequestSlashByTarget] = """{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}""",
            [SkillProgramEffectOp.BindDiscardPhaseDiscards] = """{"op":"bindDiscardPhaseDiscards","target":"owner","resultBind":"guzheng-pool"}""",
            [SkillProgramEffectOp.RestorePhaseHandDiscards] = """{"op":"restorePhaseHandDiscards","target":"owner","chooserRef":{"kind":"owner"},"phaseOwnerRef":{"kind":"eventSource"},"condition":{"kind":"always"}}""",
            [SkillProgramEffectOp.TakeRandomCardFromEveryOtherCharacter] = """{"op":"takeRandomCardFromEveryOtherCharacter","target":"owner","zones":["hand","equipment","judgment"]}""",
            [SkillProgramEffectOp.RequestNearestSlash] = """{"op":"requestNearestSlash","target":"owner"}"""

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
            .Select(item => Project(item)).ToArray();
        var turn = program.Triggers.Single(item => item.Id == "turn").Effects
            .Select(item => Project(item)).ToArray();
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
        Accept("one-per-suit partition", """
        [{"op":"revealTopCards","target":"owner","amount":5,"resultBind":"r","visibility":"public"},
         {"op":"selectCardSubset","target":"owner","sourceBind":"r","resultBind":"p","minimumCards":1,"maximumCards":4,"maximumRankSum":208,"aiOrder":"mostCardsThenRankSum","onePerSuit":true},
         {"op":"moveBoundCards","target":"owner","sourceBind":"p","destination":"ownerHand"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"r","exceptBind":"p","destination":"discardPile"}]
        """);
        {
            const string choiceBranches = """
            [{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"shown","mode":"chooser","suits":["heart"],"allowDecline":true},
             {"op":"chooseOption","target":"owner","resultBind":"disposition","condition":{"kind":"boundCardsMatchSuits","sourceBind":"shown","suits":["heart"]},"options":[{"id":"discard","condition":{"kind":"always"}},{"id":"top","condition":{"kind":"always"}}]},
             {"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"discardPile","condition":{"kind":"choiceIs","sourceBind":"disposition","optionId":"discard"}},
             {"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"drawPileTop","condition":{"kind":"choiceIs","sourceBind":"disposition","optionId":"top"}}]
            """;
            const string repeatOptionBranches = """
            [{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"shown","mode":"chooser","suits":["heart"],"allowDecline":true},
             {"op":"chooseOption","target":"owner","resultBind":"disposition","condition":{"kind":"boundCardsMatchSuits","sourceBind":"shown","suits":["heart"]},"options":[{"id":"discard","condition":{"kind":"always"}},{"id":"top","condition":{"kind":"always"}}]},
             {"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"discardPile","condition":{"kind":"choiceIs","sourceBind":"disposition","optionId":"discard"}},
             {"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"ownerHand","condition":{"kind":"choiceIs","sourceBind":"disposition","optionId":"discard"}}]
            """;
            const string branchThenUnconditional = """
            [{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"shown","mode":"chooser","suits":["heart"],"allowDecline":true},
             {"op":"chooseOption","target":"owner","resultBind":"disposition","condition":{"kind":"boundCardsMatchSuits","sourceBind":"shown","suits":["heart"]},"options":[{"id":"discard","condition":{"kind":"always"}},{"id":"top","condition":{"kind":"always"}}]},
             {"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"discardPile","condition":{"kind":"choiceIs","sourceBind":"disposition","optionId":"discard"}},
             {"op":"moveBoundCards","target":"owner","sourceBind":"shown","destination":"ownerHand"}]
            """;
            AcceptChoiceBranches("choice-branch-moves", choiceBranches);
            RejectChoiceBranches("repeat option branch", repeatOptionBranches, "more than once");
            RejectChoiceBranches("branch then unconditional", branchThenUnconditional, "more than once");
        }
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
        """, "activation input cards must be consumed exactly once");
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
            """{"op":"discardSelected","target":"owner"}"""
        };
        foreach (var node in missing) Reject(() => Parse(ProgramOperationCatalog.Default, node), "missing required property");
        Require(Parse(ProgramOperationCatalog.Default,
                """{"op":"giveSelected","target":"selectedTarget"}""").Amount == 0,
            "Omitting giveSelected amount must mean the complete frozen selected-card set.");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"giveSelected","target":"selectedTarget","amount":0}"""), "between 1 and 20");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","target":"Owner","amount":1}"""), "unsupported");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","target":"owner","amount":"1"}"""), "must be Number");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"draw","op":"draw","target":"owner","amount":1}"""), "duplicate property");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"recover","target":"owner","amount":1,"numberExpression":"boundCardCount","sourceBind":"r"}"""), "amount or numberExpression");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"processing"}"""), "unsupported SkillProgramCardDestination value");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"shown","mode":"chooser","suits":[]}"""),
            "must be nonempty");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"shown","mode":"chooser","allowDecline":true}"""),
            "decline requires");

        const string oldEffects = """[{"op":"draw","target":"owner","amount":21}]""";
        Reject(() => Load("fixture:new23", Rules("fixture:new23", oldEffects,
            Entries(oldEffects, includeTriggers: false))), "between 1 and 20");

        const string cardEffects = """[{"op":"refundCardUseDebit","target":"owner"},{"op":"draw","target":"owner","amount":1,"condition":{"kind":"cardUseIsRed"}}]""";
        var cardTrigger = $$"""
        {"id":"card","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],
         "optional":true,"effects":{{cardEffects}}}
        """;
        var cardProgram = Load("fixture:card24", Rules("fixture:card24", cardEffects,
            $"\"activations\":[],\"triggers\":[{cardTrigger}]"));
        Require(cardProgram.RuntimeVersion == "skill-program-v62" && cardProgram.MinimumRulesVersion == 171,
            "Schema 24 card-action programs must use the shared executor without changing schema 23.");
        const string actorEffects = """[{"op":"draw","target":"actor","amount":1}]""";
        var actorTrigger = $$"""
        {"id":"actor-card","window":"cardUseCommitted","ownerRelation":"observer","cardKinds":["slash"],
         "optional":true,"effects":{{actorEffects}}}
        """;
        var actorProgram = Load("fixture:actor24", Rules("fixture:actor24", actorEffects,
            $"\"activations\":[],\"triggers\":[{actorTrigger}]"));
        var actorTriggerEffect = actorProgram.Triggers.Single().Effects.Single();
        Require(actorTriggerEffect.Target == SkillProgramEffectTarget.Actor,
            "Trigger actor targets must map bidirectionally without becoming selectedTarget.");
        Reject(() => Load("fixture:actor-activation", Rules("fixture:actor-activation", actorEffects,
            Entries(actorEffects, includeTriggers: false))),
            "requires context CardAction");
        Reject(() => Parse(ProgramOperationCatalog.Default,
            """{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"resultSource"},"cardOwnerRef":{"kind":"actor"},"zones":["hand"],"count":1,"destination":"discardPile"}"""),
            "resultBind");

        const string restrictionEffects = """
        [{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"discardPile","resultBind":"discarded"},
         {"op":"selectTarget","target":"owner","targetKind":"otherLivingAtDistanceOne"},
         {"op":"grantTurnHandColorRestriction","target":"selectedTarget","sourceBind":"discarded"}]
        """;
        var restrictionTrigger = $$"""
        {"id":"restriction","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,
         "effects":{{restrictionEffects}}}
        """;
        var restrictionProgram = Load("fixture:restriction32", Rules("fixture:restriction32", restrictionEffects,
            $"\"activations\":[],\"triggers\":[{restrictionTrigger}]"));
        Require(restrictionProgram.RuntimeVersion == "skill-program-v62" &&
                restrictionProgram.MinimumRulesVersion == 171,
            "Schema 32 hand-color restriction programs must retain their exact runtime boundary.");

        const string preventionEffects = """[{"op":"preventCurrentDamage","target":"owner"}]""";
        var preventionTrigger = $$$"""
        {"id":"prevention","window":"beforeDamageApplied","subject":"owner","optional":true,
         "condition":{"kind":"compare","left":{"kind":"eventTargetHp"},"operator":"equal","right":{"kind":"integerConstant","value":1}},
         "effects":{{{preventionEffects}}}}
        """;
        var preventionProgram = Load("fixture:prevention33", Rules("fixture:prevention33", preventionEffects,
            $"\"activations\":[],\"triggers\":[{preventionTrigger}]"));
        Require(preventionProgram.RuntimeVersion == "skill-program-v62" &&
                preventionProgram.MinimumRulesVersion == 171 &&
                preventionProgram.Triggers.Single().Window == SkillProgramTriggerWindow.BeforeDamageApplied,
            "Schema 33 before-damage prevention programs must retain their exact runtime boundary.");

        const string categoryEffects = """
        [{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},
         {"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile"},
         {"op":"chooseDifferentCategoryDiscard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"sourceBind":"cost","resultBind":"response"},
         {"op":"turnOver","target":"selectedTarget","condition":{"kind":"choiceIs","sourceBind":"response","optionId":"declined"}},
         {"op":"draw","target":"selectedTarget","numberExpression":"boundCardCount","sourceBind":"cost","condition":{"kind":"choiceIs","sourceBind":"response","optionId":"declined"}}]
        """;
        var categoryActivation = $$"""
        {"id":"category","minCards":1,"maxCards":3,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,
         "targetKind":"otherLiving","usesPerTurn":1,"condition":{"kind":"always"},"effects":{{categoryEffects}}}
        """;
        var categoryProgram = Load("fixture:category34", Rules("fixture:category34", categoryEffects,
            $"\"activations\":[{categoryActivation}],\"triggers\":[]"));
        Require(categoryProgram.RuntimeVersion == "skill-program-v62" &&
                categoryProgram.MinimumRulesVersion == 171 &&
                categoryProgram.Activations.Single() is { MinCards: 1, MaxCards: 3 } activation &&
                activation.Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.CaptureSelectedCards,
                    SkillProgramEffectOp.MoveBoundCards,
                    SkillProgramEffectOp.ChooseDifferentCategoryDiscard,
                    SkillProgramEffectOp.TurnOver,
                    SkillProgramEffectOp.Draw]),
            "Schema 34 category challenges must retain variable activation cards, exact choice binding and bound-card draws.");

        static string MultiCardRules() => $$$"""
        {"schemaVersion":62,"skills":[{"id":"fixture:multi-card","revision":1,
         "minimumRulesVersion": 171,"modifiers":[],
         "viewAs":[{"id":"two-as-slash","inputKinds":[],"inputSuits":[],"inputCount":2,
          "outputKind":"slash","forPlay":true,"forResponse":true,"condition":{"kind":"always"}}],
         "activations":[{"id":"two-as-slash","minCards":2,"maxCards":2,"sourceZones":["hand"],
          "minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
          "condition":{"kind":"always"},"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget",
          "sourceBind":"two-as-slash","outputKind":"slash"}]}],
         "triggers":[{"id":"grant","window":"afterDamageApplied","subject":"source",
          "sourceSkillId":"fixture:multi-card","sourceViewAsId":"two-as-slash","damageOccurrence":"perDamage",
          "optional":false,"priority":0,"usageScope":"turn","usageLimit":1,"condition":{"kind":"always"},
          "effects":[{"op":"grantTurnSkills","target":"owner","skillIds":["classic:wusheng","classic:paoxiao"]}]}],
         "contributions":[],"cardIdentities":[]}]}
        """;
        var multiCardProgram = Load("fixture:multi-card", MultiCardRules());
        Require(multiCardProgram.RuntimeVersion == "skill-program-v62" &&
                multiCardProgram.MinimumRulesVersion == 171 &&
                multiCardProgram.ViewAs.Single() is { Id: "two-as-slash", InputCount: 2 } &&
                multiCardProgram.Activations.Single().Effects.Single().Op ==
                    SkillProgramEffectOp.UseSelectedCardsAs &&
                multiCardProgram.Triggers.Single() is
                {
                    Subject: SkillProgramTriggerSubject.Source,
                    SourceSkillId: "fixture:multi-card",
                    SourceViewAsId: "two-as-slash"
                } sourceTrigger &&
                sourceTrigger.Effects.Single().Op == SkillProgramEffectOp.GrantTurnSkills,
            "Schema 35 must own multi-card view-as, selected-card use, damage-source filtering and turn grants.");

        const string allHandEffects =
            """[{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all-hand-trick","condition":{"kind":"always"}}]""";
        var allHandActivation = $$"""
        {"id":"all-hand-trick","minCards":1,"maxCards":64,"sourceZones":["hand"],
         "minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
         "condition":{"kind":"always"},"effects":{{allHandEffects}}}
        """;
        var allHandProgram = Load("fixture:all-hand-trick", Rules("fixture:all-hand-trick", allHandEffects,
            $"\"activations\":[{allHandActivation}],\"triggers\":[]"));
        Require(allHandProgram.RuntimeVersion == "skill-program-v62" &&
                allHandProgram.MinimumRulesVersion == 171 &&
                allHandProgram.Activations.Single().Effects.Single() is
                {
                    Op: SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick,
                    SourceBind: "all-hand-trick"
                },
            "Schema 36 must own the all-hand ordinary-trick activation contract.");

        const string completeZoneEffects = """
        [{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"revealed"},
         {"op":"revealBoundCards","target":"owner","sourceBind":"revealed"},
         {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"eventSource"},"cardOwnerRef":{"kind":"eventSource"},
          "zones":["hand"],"count":1,"destination":"discardPile","skipIfNoCards":true,
          "condition":{"kind":"boundCardsSameColor","sourceBind":"revealed"}}]
        """;
        var completeZoneTrigger = $$"""
        {"id":"reveal","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage",
         "optional":true,"priority":0,"condition":{"kind":"always"},"effects":{{completeZoneEffects}}}
        """;
        var completeZoneProgram = Load("fixture:complete-zone", Rules("fixture:complete-zone", completeZoneEffects,
            $"\"activations\":[],\"triggers\":[{completeZoneTrigger}]"));
        var completeZoneProgramEffects = completeZoneProgram.Triggers.Single().Effects;
        Require(completeZoneProgram.RuntimeVersion == "skill-program-v62" &&
                completeZoneProgramEffects[0].NumberExpression == SkillProgramNumberExpression.AllOwnedZoneCards &&
                completeZoneProgramEffects[2].Condition.Kind == SkillProgramConditionKind.BoundCardsSameColor &&
                completeZoneProgramEffects[2].SkipIfNoCards,
            "Schema 36 must own complete-zone capture, bound-card color tests and empty-card skipping.");

        const string attackRangeEffects =
            """[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"unlimited"}]""";
        var attackRangeProgram = Load("fixture:attack-range48", Rules("fixture:attack-range48", attackRangeEffects,
            Entries(attackRangeEffects, includeTriggers: false)));
        Require(attackRangeProgram.RuntimeVersion == "skill-program-v62" &&
                attackRangeProgram.MinimumRulesVersion == 171 &&
                attackRangeProgram.Activations.Single().Effects.Single() is
                {
                    RuleQuery: SkillRuleQuery.AttackRange,
                    RuleOperation: SkillRuleOperation.Unlimited
                },
            "Schema 48 must own unlimited attack-range turn modifiers without widening older schemas.");

        const string distributionEffects =
            """[{"op":"draw","target":"owner","amount":2,"resultBind":"drawn"},{"op":"distributeOwnedCards","target":"owner","zones":["hand"],"numberExpression":"boundCardCount","sourceBind":"drawn","targetKind":"otherLiving","allowDeclineBeforeFirst":true}]""";
        var distributionProgram = Load("fixture:distribution49", Rules("fixture:distribution49", distributionEffects,
            Entries(distributionEffects, includeTriggers: false)));
        Require(distributionProgram.RuntimeVersion == "skill-program-v62" &&
                distributionProgram.MinimumRulesVersion == 171 &&
                distributionProgram.Activations.Single().Effects[1] is
                {
                    Op: SkillProgramEffectOp.DistributeOwnedCards,
                    NumberExpression: SkillProgramNumberExpression.BoundCardCount,
                    SourceBind: "drawn",
                    TargetKind: SkillProgramTargetKind.OtherLiving,
                    AllowDeclineBeforeFirst: true
                } distribution && distribution.Zones.SequenceEqual([CardZoneKind.Hand]),
            "Schema 49 must own all-or-nothing owned-card distribution without widening older schemas.");

        const string attackRangeAidEffects =
            """[{"op":"requestAttackRangeAid","target":"selectedTarget"}]""";
        const string attackRangeAidActivation =
            """{"id":"aid","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"requestAttackRangeAid","target":"selectedTarget"}]}""";
        var attackRangeAidProgram = Load("fixture:attack-range-aid50", Rules("fixture:attack-range-aid50", attackRangeAidEffects,
            $"\"activations\":[{attackRangeAidActivation}],\"triggers\":[]"));
        Require(attackRangeAidProgram.RuntimeVersion == "skill-program-v62" &&
                attackRangeAidProgram.MinimumRulesVersion == 171 &&
                attackRangeAidProgram.Activations.Single() is
                {
                    UsesPerGame: 1,
                    Effects: [{ Op: SkillProgramEffectOp.RequestAttackRangeAid }]
                },
            "Schema 50 must own game-limited activations and attack-range aid without widening older schemas.");

        const string nullifyCardEffect =
            """[{"op":"nullifyCurrentCardEffect","target":"owner"}]""";
        const string nullifyCondition =
            """{"kind":"not","children":[{"kind":"cardActionActorIsOwner"}]}""";
        string NullifyTrigger(string condition, string cardKinds = "\"slash\"") => $$"""
        {"id":"nullify","window":"cardUseBeforeTargetEffects","ownerRelation":"target",
         "cardKinds":[{{cardKinds}}],"optional":true,"condition":{{condition}},
         "effects":{{nullifyCardEffect}}}
        """;
        const string oneDraw = """[{"op":"draw","target":"owner","amount":1}]""";
        var wrongWindowTrigger = $$"""
        {"id":"nullify","window":"turnEnding","subject":"owner","optional":true,
         "condition":{{nullifyCondition}},"effects":{{nullifyCardEffect}}}
        """;
        Reject(() => Load("fixture:nullify-wrong-window", Rules("fixture:nullify-wrong-window", nullifyCardEffect,
            $"\"activations\":[],\"triggers\":[{wrongWindowTrigger}]")),
            "card-action trigger");
        Reject(() => Load("fixture:nullify-actor", Rules("fixture:nullify-actor", nullifyCardEffect,
            $"\"activations\":[],\"triggers\":[{NullifyTrigger(nullifyCondition).Replace("\"target\":\"owner\"", "\"target\":\"actor\"", StringComparison.Ordinal)}]")),
            "must be owner");
        var nullifyProgram = Load("fixture:nullify51", Rules("fixture:nullify51", nullifyCardEffect,
            $"\"activations\":[],\"triggers\":[{NullifyTrigger(nullifyCondition, "\"slash\",\"duel\"")}]"));
        Require(nullifyProgram.RuntimeVersion == "skill-program-v62" &&
                nullifyProgram.MinimumRulesVersion == 171 &&
                nullifyProgram.Triggers.Single() is
                {
                    Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                    OwnerRelation: SkillProgramCardActionOwnerRelation.Target,
                    Condition.Kind: SkillProgramTriggerConditionKind.Not,
                    Effects: [{ Op: SkillProgramEffectOp.NullifyCurrentCardEffect }]
                } nullifyTrigger &&
                nullifyTrigger.CardKinds.Contains(CardKind.Duel) &&
                nullifyTrigger.Condition.Children.Single().Kind ==
                    SkillProgramTriggerConditionKind.CardActionActorIsOwner,
            "Schema 51 must own per-target card-effect nullification and actor/owner identity facts without widening older schemas.");

        const string targetCountModifier =
            """{"id":"fire-extra","query":"cardTargetCount","operation":"add","value":1,"priority":0,"cardKinds":["fireSlash"]}""";
        const string chainedViewAs =
            """{"id":"slash-to-fire","inputKinds":["slash"],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"allowChainedInput":true}""";
        var schema52Rules = Rules("fixture:target-count52", oneDraw,
                Entries(oneDraw, includeTriggers: false))
            .Replace("\"modifiers\":[]", $"\"modifiers\":[{targetCountModifier}]", StringComparison.Ordinal)
            .Replace("\"viewAs\":[]", $"\"viewAs\":[{chainedViewAs}]", StringComparison.Ordinal);
        var schema52Program = Load("fixture:target-count52", schema52Rules);
        Require(schema52Program.RuntimeVersion == "skill-program-v62" &&
                schema52Program.MinimumRulesVersion == 171 &&
                schema52Program.Modifiers.Single() is
                { Query: SkillRuleQuery.CardTargetCount, Operation: SkillRuleOperation.Add, Value: 1 } targetCount &&
                targetCount.CardKinds.SequenceEqual([CardKind.FireSlash]) &&
                schema52Program.ViewAs.Single() is
                { OutputKind: CardKind.FireSlash, AllowChainedInput: true },
            "Schema 52 must own card-kind-filtered target counts and explicit chained viewAs input.");
        Reject(() => Load("fixture:target-count-empty52",
                schema52Rules.Replace("\"cardKinds\":[\"fireSlash\"]", "\"cardKinds\":[]", StringComparison.Ordinal)
                    .Replace("fixture:target-count52", "fixture:target-count-empty52", StringComparison.Ordinal)),
            "at least one effective card kind");
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

    private static void AcceptChoiceBranches(string id, string effects)
    {
        var skillId = "fixture:" + id.Replace(' ', '-');
        var activation = $$"""
        {"id":"active","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
         "targetKind":"otherLivingWithHand","usesPerTurn":1,"effects":{{effects}}}
        """;
        var rules = Rules(skillId, effects, $"\"activations\":[{activation}],\"triggers\":[]");
        var presentation = JsonSerializer.Serialize(new
        {
            schemaVersion = 3,
            skills = new Dictionary<string, object>
            {
                [skillId] = new
                {
                    name = "Fixture", description = "Fixture",
                    optionLabels = new { discard = "Discard", top = "Top" }
                }
            }
        });
        _ = SkillProgramCatalog.Load(rules, presentation).Programs[skillId];
    }

    private static void RejectChoiceBranches(string id, string effects, string expected) =>
        Reject(() => AcceptChoiceBranches(id, effects), expected);

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
            schemaVersion = 3,
            skills = new Dictionary<string, object> { [id] = new { name = "Fixture", description = "Fixture" } }
        })).Programs[id];

    private static string Rules(string id, string effects, string entries) => $$"""
    {"schemaVersion":62,"skills":[{"id":"{{id}}","revision":1,"minimumRulesVersion": 171,
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
        effect.Op,
        effect.Target,
        effect.Amount,
        Condition = effect.Condition.Kind,
        effect.NumberExpression,
        effect.SourceBind,
        effect.ResultBind,
        effect.ExceptBind,
        effect.Visibility,
        effect.MinimumCards,
        effect.MaximumCards,
        effect.MaximumRankSum,
        effect.AiOrder,
        effect.Destination,
        effect.FaceDown,
        Zones = effect.Zones.ToArray(),
        Suits = effect.Suits.ToArray(),
        effect.TargetKind
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
