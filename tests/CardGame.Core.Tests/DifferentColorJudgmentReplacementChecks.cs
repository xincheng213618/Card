using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DifferentColorJudgmentReplacementChecks
{
    private const string Driver = "fixture:different-color-driver";
    private const string Replace = "fixture:different-color-replace";
    private const string Final = "fixture:different-color-final";
    private const string Red = "fixture:different-color-red";
    private const string Owner = "fixture:different-color-owner";
    private const string Mode = "identity:classic-different-color-judgment";
    private const string Reason = "skill.fixture.different-color-judgment";

    public static void EffectiveOwnerAndSubjectColorsPayHandEquipmentAndReplayOnce()
    {
        Exercise(ownerRed: true, subjectRed: false, equipment: true);
        Exercise(ownerRed: false, subjectRed: true, equipment: false);
        var blackInitiallyUsable = ExerciseChangedCurrentColor(Suit.Spade);
        var redInitiallyUsable = ExerciseChangedCurrentColor(Suit.Heart);
        Require(blackInitiallyUsable != redInitiallyUsable,
            "The fixed two-binding cases execute both unavailable-to-available and available-to-unavailable color transitions.");
    }

    public static void SameColorNoneAndLegacyDefaultsKeepPrivateNativeChoices()
    {
        foreach (var (red, suit) in new[] { (false, Suit.Spade), (true, Suit.Spade), (false, Suit.None) })
        {
            var (g, r) = Start(red, red, suit);
            Use(g, 0);
            Reach(g, p => p.SkillPrompt?.SkillId == Final);
            Require(!Facts<JudgmentReplacementRequestedEvent>(g).Any() &&
                    !Facts<ProgramJudgmentReplacementResolvedEvent>(g).Any(),
                $"Same effective colors and colorless cards cannot offer a replacement: red={red}, printed={suit}.");
            g = Cold(g, r); Continue(g); ReachPlay(g);
            Require(Facts<JudgmentResolvedEvent>(g).Count(e => e.Reason == Reason) == 1,
                "An unavailable color change preserves the original real judgment and returns once.");
        }

        foreach (var flag in new bool?[] { null, false })
        {
            var (g, r) = Start(false, false, Suit.Spade, flag);
            Use(g, 0); Reach(g, p => p.Kind == DecisionKind.ProgramJudgmentReplacement);
            var p = P(g)!;
            Require(p.ValidCardIds.Count == g.CreateSnapshot(0).Players[0].HandCount,
                "Omitted and explicit false preserve the existing same-color replacement candidates.");
            AssertPrivateAndFrozen(g);
            g = Cold(g, r); Answer(g, c => c.Cards.Count == 1);
            Reach(g, q => q.SkillPrompt?.SkillId == Final); Continue(g); ReachPlay(g);
            Require(Facts<ProgramJudgmentReplacementResolvedEvent>(g).Count(e => e.Activated) == 1,
                "Legacy replacement still pays and completes through the native judgment boundary.");
        }
        ValidateJsonContract();
    }

    private static void Exercise(bool ownerRed, bool subjectRed, bool equipment)
    {
        var (g, registry) = Start(ownerRed, subjectRed, Suit.Spade);
        int paidCard;
        if (equipment)
        {
            var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
            paidCard = equip.CardId!.Value;
            Accept(g, new PlayCardCommand(0, paidCard, equip.TargetSeats, g.Revision, P(g)!.PromptId, equip.PlayedCardKind));
            ReachPlay(g);
        }
        else paidCard = g.CreateSnapshot(0).Players[0].Hand[0].Id;
        var paidFrom = equipment ? CardLocation.Equipment(0) : CardLocation.Hand(0);
        Use(g, 1); Reach(g, p => p.Kind == DecisionKind.ProgramJudgmentReplacement);
        var pending = P(g)!;
        var judgment = g.ResolutionStack.OfType<JudgmentFrame>().Single();
        var oldId = judgment.CardId!.Value;
        var judgmentId = judgment.Id;
        var oldEffective = subjectRed ? Suit.Heart : Suit.Spade;
        var newEffectiveForSubject = subjectRed ? Suit.Heart : Suit.Spade;
        var own = g.CreateSnapshot(0).Players[0];
        Require(pending.ValidCardIds.ToHashSet().SetEquals(own.Hand.Concat(own.Equipment).Select(c => c.Id)) &&
                pending.ValidCardIds.Contains(paidCard) &&
                own.Hand.Concat(own.Equipment).All(c => c.Suit == Suit.Spade) &&
                Facts<JudgmentReplacementRequestedEvent>(g).Single().JudgmentSuit == oldEffective,
            $"Eligibility compares owner-effective {(ownerRed ? "red" : "black")} to subject-effective {(subjectRed ? "red" : "black")}, despite identical printed spades.");
        AssertPrivateAndFrozen(g);
        var ai = new SimpleAiBrain(0, 31).ChooseJudgmentReplacement(g.CreateSnapshot(0), 1, Reason,
            pending.ValidCardIds, CardKind.OffensiveHorse, oldEffective, 5, null, 0);
        Require(ai.CardId is null || pending.ValidCardIds.Contains(ai.CardId.Value),
            "Native judgment AI chooses only its published private eligible HE cards or the legal skip.");
        Reject(g, new AnswerPromptCommand(1, pending.PromptId, pending.Choices.First(c => c.Cards.Contains(paidCard)).Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, pending.PromptId, new("unpublished-color-replacement"), g.Revision));
        var retained = g.CreateSnapshot(0); var retainedJson = SnapshotJson.Serialize(retained);
        g = Cold(g, registry);
        Answer(g, c => c.Cards.SequenceEqual([paidCard]));
        Reach(g, p => p.SkillPrompt?.SkillId == Final);
        var resultWindow = g.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single();
        Require(resultWindow.ParentFrameId == judgmentId && resultWindow.Judgment is
                { CardId: var finalCard, SubjectSeat: 1, Suit: var finalSuit } && finalCard == paidCard && finalSuit == newEffectiveForSubject &&
                Facts<JudgmentResolvedEvent>(g).Single(e => e.ResolutionId == judgmentId).CardId == paidCard &&
                Facts<ProgramJudgmentReplacementResolvedEvent>(g).Single(e => e.JudgmentFrameId == judgmentId) is
                { Activated: true, OwnerSeat: 0, SubjectSeat: 1, OldCardId: var replacedOld, ReplacementCardId: var replacement } &&
                replacedOld == oldId && replacement == paidCard,
            "The actual finalized child belongs to the original judgment and freezes the replacement under the subject's effective suit.");
        var cost = g.CardMovements.Where(m => m.Reason == CardMoveReasons.ProgramJudgmentReplace && m.CardId == paidCard).ToArray();
        Require(cost.Length == 2 && cost[0].From == paidFrom && cost[0].To == CardLocation.Processing &&
                cost[1].From == CardLocation.Processing && cost[1].To == CardLocation.Judgment(1) &&
                g.CardMovements.Count(m => m.CardId == oldId && m.Reason == CardMoveReasons.ProgramJudgmentOldCard && m.To == CardLocation.DiscardPile) == 1 &&
                SnapshotJson.Serialize(retained) == retainedJson,
            $"The real chosen HE entity pays once through Processing, old judgment is discarded once, and retained private snapshots stay immutable. paidFrom={paidFrom}; cost={JsonSerializer.Serialize(cost)}; oldMoves={JsonSerializer.Serialize(g.CardMovements.Where(m => m.CardId == oldId))}; retainedUnchanged={SnapshotJson.Serialize(retained) == retainedJson}.");
        Reject(g, new AnswerPromptCommand(0, pending.PromptId, pending.Choices.First(c => c.Cards.Contains(paidCard)).Id, g.Revision));
        g = Cold(g, registry); Continue(g); ReachPlay(g);
        Require(Facts<JudgmentResolvedEvent>(g).Count(e => e.ResolutionId == judgmentId) == 1 &&
                Facts<ProgramJudgmentReplacementResolvedEvent>(g).Count(e => e.JudgmentFrameId == judgmentId && e.Activated) == 1 &&
                Facts<ProgramJudgmentTriggerResolvedEvent>(g).Count(e => e.JudgmentFrameId == judgmentId && e.SkillId == Final && e.Activated) == 1 &&
                g.CardMovements.Count(m => m.CardId == paidCard && m.Reason == CardMoveReasons.ProgramJudgmentReplace) == 2 &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == paidCard).Location == CardLocation.DiscardPile &&
                !g.ResolutionStack.Any(f => f.Id == judgmentId),
            "Cold finalized-child return completes the exact parent and physical discard once without repaying replacement.");
        _ = Cold(g, registry);
    }

    private static void ValidateJsonContract()
    {
        SkillProgramEffect Load(bool? value) => SkillProgramCatalog.Load(Rules(value), Presentation).Programs[Replace].Triggers[0].Effects[0];
        Require(Load(true).RequireDifferentColor == true && Load(false).RequireDifferentColor == false && Load(null).RequireDifferentColor is null &&
                JsonNode.Parse(JsonSerializer.Serialize(Load(true)))!["RequireDifferentColor"]!.GetValue<bool>() &&
                JsonNode.Parse(JsonSerializer.Serialize(Load(null)))!["RequireDifferentColor"] is null,
            "The additive nullable flag survives parsing and exposed serialization while old effects omit it.");
        foreach (var invalid in new JsonNode?[] { JsonValue.Create("true"), JsonValue.Create(1), null })
        {
            var rules = JsonNode.Parse(Rules(true))!;
            rules["skills"]![1]!["triggers"]![0]!["effects"]![0]!["requireDifferentColor"] = invalid;
            try { _ = SkillProgramCatalog.Load(rules.ToJsonString(), Presentation); }
            catch (InvalidOperationException e) when (e.Message.Contains("requireDifferentColor", StringComparison.Ordinal)) { continue; }
            throw new InvalidOperationException("A present requireDifferentColor must be a boolean, never a string, number or null.");
        }
        var old = SkillProgramCatalog.Load(Rules(null), Presentation).Programs[Replace];
        Require(old.GameplayHash == SkillProgramCatalog.Load(Rules(null), Presentation).Programs[Replace].GameplayHash &&
                old.GameplayHash != SkillProgramCatalog.Load(Rules(true), Presentation).Programs[Replace].GameplayHash,
            "Old unchanged rule JSON retains its fingerprint; opting into the new behavior changes its content fingerprint.");
    }

    private static bool ExerciseChangedCurrentColor(Suit allowedSuit)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(false, false, Suit.Spade, true, allowedSuit));
        var g = Start(registry);
        Use(g, 1); Reach(g, p => p.Kind == DecisionKind.ProgramJudgmentReplacement);
        var initial = g.ResolutionStack.OfType<JudgmentFrame>().Single();
        var initialId = initial.CardId!.Value;
        var initialSuit = Facts<JudgmentReplacementRequestedEvent>(g).Single().JudgmentSuit;
        var oppositeSuit = initialSuit == Suit.Spade ? Suit.Heart : Suit.Spade;
        var own = g.CreateSnapshot(0).Players[0];
        var replacementId = own.Hand.FirstOrDefault(c => c.Suit == oppositeSuit)?.Id ??
            throw new InvalidOperationException($"Seed 31 fixed red/black hand has no opposite-color predecessor material: old={initialSuit}, hand={JsonSerializer.Serialize(own.Hand)}.");
        Require(P(g)!.Choices.Where(c => c.Cards.Count == 1).All(c => c.Id.Value.Contains("a-predecessor", StringComparison.Ordinal)) &&
                own.Hand.Any(c => c.Suit == allowedSuit),
            "The first real replacement is the preceding legacy binding; the later binding already has physical eligible-suit material.");
        var laterInitiallyUsable = initialSuit != allowedSuit;
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([replacementId]));
        Reach(g, p => p.Kind == DecisionKind.ProgramJudgmentReplacement || p.SkillPrompt?.SkillId == Final);
        if (!laterInitiallyUsable)
        {
            Require(P(g)!.Kind == DecisionKind.ProgramJudgmentReplacement &&
                    P(g)!.Choices.Where(c => c.Cards.Count == 1).All(c => c.Id.Value.Contains("replace", StringComparison.Ordinal) &&
                        g.CreateSnapshot(0).Players[0].Hand.Single(h => h.Id == c.Cards[0]).Suit == allowedSuit),
                "An initially same-color binding remains in the judgment candidate order and becomes usable after its predecessor changes the real current card.");
            g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1);
            Reach(g, p => p.SkillPrompt?.SkillId == Final);
        }
        else Require(P(g)!.SkillPrompt?.SkillId == Final,
            "A binding initially eligible is rechecked and offers no cards after the predecessor makes all its materials the same color as the current judgment.");
        Require(Facts<ProgramJudgmentReplacementResolvedEvent>(g).Count(e => e.TriggerId == "a-predecessor" && e.Activated) == 1 &&
                Facts<ProgramJudgmentReplacementResolvedEvent>(g).Count(e => e.TriggerId == "replace" && e.Activated) == (laterInitiallyUsable ? 0 : 1) &&
                g.CardMovements.Count(m => m.CardId == initialId && m.Reason == CardMoveReasons.ProgramJudgmentOldCard) == 1,
            "Each actual binding and original judgment payment occurs once at its real chronological color boundary.");
        g = Cold(g, registry); Continue(g); ReachPlay(g); _ = Cold(g, registry);
        return laterInitiallyUsable;
    }

    private static (GameEngine, ContentRegistry) Start(bool ownerRed, bool subjectRed, Suit suit, bool? flag = true)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(ownerRed, subjectRed, suit, flag));
        return (Start(registry), registry);
    }
    private static GameEngine Start(ContentRegistry registry)
    {
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode, HumanSeat = 0, HumanRole = Role.Lord,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); ReachPlay(g); return g;
    }
    private static void Use(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Driver, "judge", [], [target], g.Revision, P(g)!.PromptId));
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).PendingDecision;
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) => Accept(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First(predicate).Id, g.Revision));
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 128; step++)
        {
            if (P(g) is { } p && predicate(p)) return;
            Require(P(g) is null, "Unexpected human judgment boundary: " + JsonSerializer.Serialize(P(g)));
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed judgment fixture exceeded its bounded child return.");
    }
    private static void AssertPrivateAndFrozen(GameEngine g)
    {
        var p = P(g)!;
        Require(p.IsPrivate && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null &&
                g.CreateSnapshot(s).Players[0].Hand.Count == 0), "Only the true owner sees private eligible hand faces and replacement choices.");
        try { ((IList<int>)p.ValidCardIds)[0] = -1; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Published replacement candidate IDs must be immutable.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(restored) == State(g), "Cold accepted-command replay restores the actual judgment, private choices, paid material and typed child exactly."); return restored;
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real judgment command rejected."); }
    private static void Reject(GameEngine g, GameCommand command) { var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(!result.Accepted && result.Error is not null && State(g) == before, "Wrong actor, unpublished or stale choice rejects atomically."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool ownerRed, bool subjectRed, Suit suit, bool? flag, Suit? chainAllowed = null) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-different-color-judgment", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse(Rules(flag))!;
            if (chainAllowed is { } allowed)
            {
                var triggers = rules["skills"]![1]!["triggers"]!.AsArray();
                var predecessor = triggers[0]!.DeepClone(); predecessor["id"] = "a-predecessor";
                predecessor["effects"]![0]!.AsObject().Remove("requireDifferentColor");
                triggers[0]!["effects"]![0]!["suits"] = new JsonArray(JsonValue.Create(allowed.ToString().ToLowerInvariant()));
                triggers.Insert(0, predecessor);
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), Presentation);
            foreach (var program in catalog.Programs.Values)
                b.AddSkill(new ContentSkillDefinition(program.Id, catalog.Presentations[program.Id].Name, catalog.Presentations[program.Id].Description) { Program = program });
            b.AddGeneral(new ContentGeneralDefinition(Owner, "异色改判", "cao_cao", Driver, "jin", BaseHp: 4,
                AdditionalSkillIds: ownerRed ? [Replace, Final, Red] : [Replace, Final]));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:different-color-peer-{s}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new ContentGeneralDefinition(peer, "判定对象", "cao_cao", subjectRed ? Red : "standard:none", "wei", BaseHp: 4));
            b.AddDeck(new ContentDeckRecipe("fixture:different-color-deck", "固定花色装备", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("standard:offensive_horse", chainAllowed is null ? suit : i % 2 == 0 ? Suit.Spade : Suit.Heart, 5)).ToArray() });
            b.AddMode(new ContentModeDefinition(Mode, "异色判定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                DeckId: "fixture:different-color-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }

    private static string Rules(bool? flag) => $$$"""
      {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
        {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"judge","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[
          {"op":"startJudgment","target":"selectedTarget","judgmentReason":"{{{Reason}}}","resultBind":"judged","visibility":"public"},
          {"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]}]},
        {"id":"{{{Replace}}}","revision":1,"triggers":[{"id":"replace","window":"judgmentReplacing","subject":"any","excludedReasons":[],"optional":true,"effects":[
          {"op":"replaceJudgment","target":"owner","zones":["hand","equipment"],"suits":["spade","club","heart","diamond","none"],"oldCardDestination":"discardPile"{{{(flag is { } value ? ",\"requireDifferentColor\":" + (value ? "true" : "false") : "")}}}}]}]},
        {"id":"{{{Final}}}","revision":1,"triggers":[{"id":"final-child","window":"judgmentFinalized","subject":"any","judgmentSource":"owner","judgmentReasons":["{{{Reason}}}"],"suits":["spade","club","heart","diamond","none"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finished","options":[{"id":"continue"}]}]}]},
        {"id":"{{{Red}}}","revision":1,"cardPolicies":[{"id":"effective-red","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]}
      ]}
      """;
    private static string Presentation => $$$"""
      {"schemaVersion":3,"skills":{
        "{{{Driver}}}":{"name":"判定驱动","description":"真实选择对象判定。"},
        "{{{Replace}}}":{"name":"异色改判","description":"用有效颜色不同的自有牌改判。"},
        "{{{Final}}}":{"name":"判定子窗","description":"暂停实际最终判定。","optionLabels":{"continue":"继续"}},
        "{{{Red}}}":{"name":"有效红色","description":"黑桃视为红桃。"}
      }}
      """;
}
