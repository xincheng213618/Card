using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChoiceTargetPrerequisiteChecks
{
    private const string Skill = "fixture:choice-target-prerequisite";
    private const string Driver = "fixture:choice-target-driver";
    private const string Child = "fixture:choice-target-child";
    private const string Far = "fixture:choice-target-far";
    private const string Owner = "fixture:choice-target-owner";
    private const string Mode = "identity:classic-choice-target-prerequisite";
    private const string PaidReason = "skill-program.fixture:choice-target-prerequisite.SelectAndMoveOwnedCard";
    private const string DrawReason = "skill-program.fixture:choice-target-prerequisite.Draw";

    public static void MissingTargetsRetainMandatoryDrawRecoveryAndConsumeGroupOnce()
    {
        foreach (var option in new[] { "draw", "recover" })
        {
            var (g, registry) = Start(emptyAndFar: true);
            Accept(g, new UseProgramSkillCommand(0, Driver, "hurt", [], [], g.Revision, P(g)!.PromptId));
            Reach(g, p => p.Kind == DecisionKind.PlayCard);
            var hp = g.CreateSnapshot(0).Players[0].Hp;
            EndPlay(g); Reach(g, p => IsChoice(p, "branch"));
            var parent = Parent(g); var parentId = parent.Id;
            Require(P(g)!.Choices.Select(Option).ToHashSet().SetEquals(["draw", "recover"]) &&
                    Enumerable.Range(1, 3).All(s => g.CreateSnapshot(0).Players[s] is { HandCount: 0, Equipment.Count: 0 }) &&
                    Facts<ProgramBindingStartedEvent>(g).Single(e => e.FrameId == parentId).Window == SkillProgramTriggerWindow.TurnEnding,
                "The actual mandatory ending group starts despite absent range/HE targets and publishes exactly its legal draw/recovery alternatives.");
            Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
            Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, new("program-option.unavailable-damage"), g.Revision));
            var privateView = g.CreateSnapshot(0); var privateJson = SnapshotJson.Serialize(privateView);
            AssertPrivate(g); g = Cold(g, registry); AnswerOption(g, option);
            if (option == "draw")
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Child);
                AssertBranch(g, parentId, option);
                Require(g.CardMovements.Count(m => m.Reason.Value == DrawReason && m.To == CardLocation.Hand(0)) == 2 &&
                        g.CreateSnapshot(0).Players[0].HandCount == 2,
                    "The native Draw instruction pays its two real physical entities before its actual gained-card child.");
                g = Cold(g, registry); Continue(g);
            }
            Reach(g, p => IsChoice(p, "tail"));
            Require(g.CreateSnapshot(0).Players[0].Hp == hp + (option == "recover" ? 1 : 0) &&
                    Facts<DamageAppliedEvent>(g).Length == 0 && !g.CardMovements.Any(m => m.Reason.Value == PaidReason) &&
                    SnapshotJson.Serialize(privateView) == privateJson,
                "The chosen non-target branch alone executes; absent damage/discard branches never cancel or leak through, and retained private views stay immutable.");
            AssertBranch(g, parentId, option); g = Cold(g, registry); Continue(g); FinishParent(g, parentId);
            Require(Facts<ProgramOptionChosenEvent>(g).Count(e => e.FrameId == parentId && e.ResultBind == "branch" && e.OptionId == option) == 1 &&
                    Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.Activated && e.Completed) == 1 &&
                    (option == "draw" ? g.CardMovements.Count(m => m.Reason.Value == DrawReason) == 2 :
                        Facts<RecoveryAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1),
                "The mandatory group's selected alternative and successful completion are committed once, including after native child cold return.");
            _ = Cold(g, registry);
        }
        RejectInvalidBranchContracts();
    }

    public static void AvailableTargetsKeepExactChoiceThroughDamageAndPrivateDiscardChildren()
    {
        foreach (var option in new[] { "damage", "discard" })
        {
            var (g, registry) = Start(emptyAndFar: false);
            EndPlay(g); Reach(g, p => IsChoice(p, "branch"));
            var parentId = Parent(g).Id;
            Require(P(g)!.Choices.Select(Option).ToHashSet().SetEquals(["draw", "damage", "discard"]),
                "A healthy owner with real in-range and foreign HE targets receives all three legal alternatives, excluding wounded-only recovery.");
            g = Cold(g, registry); AnswerOption(g, option);
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
            AssertBranch(g, parentId, option); AssertPrivate(g);
            Require(P(g)!.ValidTargetSeats.Count > 0 && !P(g)!.ValidTargetSeats.Contains(0),
                "Only the chosen conditional selector publishes actual other living targets.");
            Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
            var target = P(g)!.ValidTargetSeats[0];
            var hp = g.CreateSnapshot(0).Players[target].Hp;
            var hand = g.CreateSnapshot(target).Players[target].Hand.Select(c => c.Id).ToArray();
            g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([target]));
            if (option == "discard")
            {
                Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
                Require(P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == "Hand") &&
                        g.CreateSnapshot(0).Players[target].Hand.Count == 0,
                    "The foreign hand payment publishes opaque native slot choices without exposing card IDs or faces to the chooser.");
                AssertBranch(g, parentId, option); AssertPrivate(g); g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
            }
            Reach(g, p => p.SkillPrompt?.SkillId == Child);
            AssertBranch(g, parentId, option);
            var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Child);
            Require(child.WindowContext is { } context &&
                    (option == "damage" ? context.Window == SkillProgramTriggerWindow.AfterDamageApplied :
                        context.Window == SkillProgramTriggerWindow.DiscardPileReceived && context.MovementBatch?.ParentFrameId == parentId) &&
                    !Facts<ProgramBindingResolvedEvent>(g).Any(e => e.FrameId == parentId),
                "The real native effect child suspends its still-live original choice parent; payment is not treated as whole-program completion.");
            if (option == "damage") Require(Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == target && e.Amount == 1) == 1 &&
                    g.CreateSnapshot(0).Players[target].Hp == hp - 1 && !g.CardMovements.Any(m => m.Reason.Value == PaidReason),
                "The chosen range target takes one real damage and the unselected discard branch pays nothing.");
            else
            {
                var paid = g.CardMovements.Single(m => m.Reason.Value == PaidReason);
                Require(hand.Contains(paid.CardId) && paid.From == CardLocation.Hand(target) && paid.To == CardLocation.DiscardPile &&
                        g.CreateSnapshot(target).Players[target].HandCount == hand.Length - 1 && !Facts<DamageAppliedEvent>(g).Any(),
                    "Exactly one actually published opaque hand slot becomes the selected target's physical discard; the unchosen damage branch never runs.");
            }
            g = Cold(g, registry); Continue(g); Reach(g, p => IsChoice(p, "tail"));
            AssertBranch(g, parentId, option); g = Cold(g, registry); Continue(g); FinishParent(g, parentId);
            Require(Facts<ProgramOptionChosenEvent>(g).Count(e => e.FrameId == parentId && e.ResultBind == "branch" && e.OptionId == option) == 1 &&
                    Facts<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.Activated && e.Completed) == 1 &&
                    Facts<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Child && e.Activated && e.Completed) == 1 &&
                    !g.CardMovements.Any(m => m.Reason.Value == DrawReason) &&
                    (option == "damage" ? Facts<DamageAppliedEvent>(g).Length == 1 : g.CardMovements.Count(m => m.Reason.Value == PaidReason) == 1),
                "Cold native effect return keeps the exact selected branch, finishes child and original parent once, and never repeats damage, payment or an unchosen tail.");
            _ = Cold(g, registry);
        }
    }

    private static void RejectInvalidBranchContracts()
    {
        foreach (var mutation in new[] { "unguarded-read", "other-branch-read", "wrong-target-kind", "undeclared-option", "invalid-required-kind", "foreign-chooser" })
        {
            var rules = JsonNode.Parse(Rules)!; var effects = rules["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray();
            switch (mutation)
            {
                case "unguarded-read": effects[2]!.AsObject().Remove("condition"); break;
                case "other-branch-read": effects[2]!["condition"]!["optionId"] = "draw"; break;
                case "wrong-target-kind": effects[1]!["targetKind"] = "otherLiving"; break;
                case "undeclared-option": effects[1]!["condition"]!["optionId"] = "missing"; break;
                case "invalid-required-kind": effects[0]!["options"]![2]!["requiredTargetKind"] = "anyLiving"; break;
                case "foreign-chooser": effects[0]!["chooserRef"] = new JsonObject { ["kind"] = "eventTarget" }; break;
            }
            try { _ = SkillProgramCatalog.Load(rules.ToJsonString(), Presentation); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("The new target-prerequisite contract accepted invalid shape: " + mutation);
        }
    }
    private static (GameEngine, ContentRegistry) Start(bool emptyAndFar)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(emptyAndFar));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = 0, HumanRole = Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, p => p.Kind == DecisionKind.PlayCard); return (g, registry);
    }
    private static ProgramSkillFrame Parent(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill);
    private static void AssertBranch(GameEngine g, long id, string option) => Require(Parent(g) is { Id: var actual } p && actual == id &&
        p.ChoiceBindings.Single(c => c.Name == "branch").OptionId == option, "The original parent retains the same once-answered named branch across selector, payment and native child boundaries.");
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option-id");
    private static bool IsChoice(PendingDecision p, string bind) => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == bind);
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) => AnswerOption(g, "continue");
    private static void AnswerOption(GameEngine g, string option) => Answer(g, c => Option(c) == option);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First(predicate).Id, g.Revision));
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 128; step++) { if (P(g) is { } p && predicate(p)) return; Require(P(g) is null, "Unexpected choice-target fixture boundary: " + JsonSerializer.Serialize(P(g))); Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        throw new InvalidOperationException("The fixed target-prerequisite fixture exceeded its bounded native stage.");
    }
    private static void FinishParent(GameEngine g, long id)
    {
        for (var step = 0; step < 64; step++) { if (Facts<ProgramBindingResolvedEvent>(g).Any(e => e.FrameId == id)) return; Require(P(g) is null, "Unexpected final native choice boundary."); Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        throw new InvalidOperationException("The real ending group did not return once.");
    }
    private static void AssertPrivate(GameEngine g) => Require(P(g)!.IsPrivate && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null), "Only the actual human chooser receives the named branch and native target/card prompt.");
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(cold) == State(g), "Cold command replay preserves exact branch/target/private cost and typed native children."); return cold; }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real target-prerequisite command rejected."); }
    private static void Reject(GameEngine g, GameCommand command) { var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(!result.Accepted && result.Error is not null && State(g) == before, "Invalid branch/actor rejects without changing the mandatory group, private state, entity history or command prefix."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(bool emptyAndFar) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-choice-target-prerequisite", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var p in catalog.Programs.Values) b.AddSkill(new ContentSkillDefinition(p.Id, catalog.Presentations[p.Id].Name, catalog.Presentations[p.Id].Description) { Program = p });
            b.AddGeneral(new ContentGeneralDefinition(Owner, "分支先决条件", "cao_cao", Skill, "jin", BaseHp: 4, AdditionalSkillIds: [Driver, Child]));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:choice-target-peer-{s}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new ContentGeneralDefinition(peer, "真实候选", "cao_cao", emptyAndFar ? Far : "standard:none", "wei", BaseHp: 4));
            b.AddDeck(new ContentDeckRecipe("fixture:choice-target-deck", "小固定实体牌堆", emptyAndFar ? 0 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 5)).ToArray() });
            b.AddMode(new ContentModeDefinition(Mode, "分支目标条件", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                DeckId: "fixture:choice-target-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
    private static string Rules => $$$"""
      {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
        {"id":"{{{Skill}}}","revision":1,"triggers":[{"id":"mandatory-group","window":"turnEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
          {"op":"chooseOption","target":"owner","resultBind":"branch","options":[{"id":"draw"},{"id":"recover","condition":{"kind":"wounded"}},{"id":"damage","requiredTargetKind":"otherLivingInAttackRange"},{"id":"discard","requiredTargetKind":"otherLivingWithDiscardableHandOrEquipment"}]},
          {"op":"selectTarget","target":"owner","targetKind":"otherLivingInAttackRange","condition":{"kind":"choiceIs","sourceBind":"branch","optionId":"damage"}},
          {"op":"damage","target":"selectedTarget","amount":1,"condition":{"kind":"choiceIs","sourceBind":"branch","optionId":"damage"}},
          {"op":"selectTarget","target":"owner","targetKind":"otherLivingWithDiscardableHandOrEquipment","condition":{"kind":"choiceIs","sourceBind":"branch","optionId":"discard"}},
          {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand","equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true,"condition":{"kind":"choiceIs","sourceBind":"branch","optionId":"discard"}},
          {"op":"draw","target":"owner","amount":2,"condition":{"kind":"choiceIs","sourceBind":"branch","optionId":"draw"}},
          {"op":"recover","target":"owner","amount":1,"condition":{"kind":"choiceIs","sourceBind":"branch","optionId":"recover"}},
          {"op":"chooseOption","target":"owner","resultBind":"tail","options":[{"id":"continue"}]}]}]},
        {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
        {"id":"{{{Child}}}","revision":1,"triggers":[
          {"id":"damage-child","window":"afterDamageApplied","subject":"damageSource","optional":false,"damageOccurrence":"perDamage","effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-damage","options":[{"id":"continue"}]}]},
          {"id":"discard-child","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","sourceZones":["hand","equipment"],"movementOccurrence":"perBatch","movementReasons":["{{{PaidReason}}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-discard","options":[{"id":"continue"}]}]},
          {"id":"draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-draw","options":[{"id":"continue"}]}]}]},
        {"id":"{{{Far}}}","revision":1,"modifiers":[{"id":"out-of-owner-range","query":"incomingDistance","operation":"add","value":4,"priority":0}]}
      ]}
      """;
    private static string Presentation => $$$"""
      {"schemaVersion":3,"skills":{
        "{{{Skill}}}":{"name":"合法分支","description":"选择具有实际目标的效果。","optionLabels":{"draw":"摸牌","recover":"回复","damage":"伤害","discard":"弃牌","continue":"继续"}},
        "{{{Driver}}}":{"name":"真实失血","description":"实际失去一点体力。"},
        "{{{Child}}}":{"name":"原生子窗","description":"暂停真实伤害或牌移动。","optionLabels":{"continue":"继续"}},
        "{{{Far}}}":{"name":"远距陪测","description":"到此角色的距离增加四。"}
      }}
      """;
}
