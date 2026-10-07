using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualHandGainAndCategoryGiftChecks
{
    private const string Driver = "fixture:actual-gain-driver", Watch = "fixture:actual-gain-watch", Mode = "identity:actual-hand-gains-4";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void ActualHandGainsPreserveBatchIdentityAndExcludeSelfRecursion()
    {
        var (g, registry) = Start(holdInitialDraw: true);
        var f = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:zishu");
        Require(f.ActualHandGain is { Operation: SkillProgramEffectOp.DrawAfterActualOwnHandGain, PaidIds.Count: 1, Gains.Count: 2 } &&
            E<ActualHandGainDrawPaidEvent>(g).Length == 1 && E<ForeignTurnHandGainsRecordedEvent>(g).Length == 0,
            "The original normal-Draw batch of two issues one real bonus, with original entity identity on its paid frame.");
        Reject(g); Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); ReachPlay(g);
        var before = E<ActualHandGainDrawPaidEvent>(g).Length;
        Use(g, "draw-two", [], []); Reach(g, p => p.SkillPrompt?.SkillId == Watch); Cold(g, registry); Continue(g); ReachPlay(g);
        Use(g, "two-singles", [], []); Reach(g, p => p.SkillPrompt?.SkillId == Watch); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Watch); Continue(g); ReachPlay(g);
        var draws = E<ActualHandGainDrawPaidEvent>(g).Skip(before).ToArray();
        Require(draws.Length == 3 && draws.Select(e => e.OriginalBatchId).Distinct().Count() == 3 && draws.All(e => e.ActualCount == 1),
            "One two-card gain and two independent single gains have three identities; each bonus excludes its own subsequent gain.");
        Cold(g, registry);
        // A source-boundary audit is intentionally synthetic: no hand entity changes during this read-only provenance query.
        var id = V(g).Hand[0].Id; var turn = g.CreateSnapshot(0).TurnNumber;
        var batch = new CardMovementBatchContext(999999, null, null, turn,
            [new(900000, turn, id, CardKind.SilverLion, CardLocation.Hand(0), CardLocation.Processing, new("fixture.hop")),
             new(900001, turn, id, CardKind.SilverLion, CardLocation.Processing, CardLocation.Hand(0), new("fixture.hop"))], []);
        var indexes = (int[])typeof(GameEngine).GetMethod("MatchingActualHandGainIndexes", Flags)!.Invoke(g, [batch, 0, CardLocation.Hand(0)])!;
        Require(indexes.Length == 0, "Moving an existing hand entity through Processing and back is not a new hand acquisition.");
        var reacquired = batch with { Movements =
            [new(900002, turn, id, CardKind.SilverLion, CardLocation.Hand(0), CardLocation.Equipment(0), new("fixture.leave")),
             new(900003, turn, id, CardKind.SilverLion, CardLocation.Equipment(0), CardLocation.Hand(0), new("fixture.reacquire"))] };
        var entities = (ActualHandGainEntity[])typeof(GameEngine).GetMethod("ActualHandGainEntities", Flags)!.Invoke(g, [reacquired, 0])!;
        Require(entities is [var regained] && regained.GainSequence == 900003, "An entity that genuinely leaves Hand and returns from Equipment has the new real acquisition identity.");
    }
    public static void ForeignHandGainsWaitForActualEndAndKeepOnlySurvivingAcquisitions()
    {
        foreach (var killEndedActor in new[] { false, true })
        {
            var (g, registry) = Start(foreign: true, killEndedActor: killEndedActor); var original = V(g).Hand.Select(c => c.Id).ToHashSet();
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
            var recorded = E<ForeignTurnHandGainsRecordedEvent>(g).Where(e => e.ActualTurnOwner != 0).Last();
            var gained = recorded.Gains.Select(e => e.CardId).ToArray();
            Require(gained.Length == 2 && gained.All(id => V(g).Hand.Any(c => c.Id == id)) &&
                E<ForeignTurnHandGainsCleanupPaidEvent>(g).Length == 0 && E<ActualHandGainDrawPaidEvent>(g).Length == 1,
                "Actual foreign gains remain usable in hand, without immediate discard or own-turn bonus.");
            Cold(g, registry); Continue(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Cards.Count == 1));
            Answer(g, c => c.Cards.SequenceEqual([gained[0]]));
            if (killEndedActor)
            { Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Targets.SequenceEqual([recorded.ActualTurnOwner])));
                Answer(g, c => c.Targets.SequenceEqual([recorded.ActualTurnOwner])); }
            Reach(g, p => p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
            var cleanup = E<ForeignTurnHandGainsCleanupPaidEvent>(g).Single();
            var parent = g.ResolutionStack.OfType<DeferredTurnEndFrame>().Single();
            Require(cleanup.CardIds.SequenceEqual([gained[1]]) && E<TurnEndedEvent>(g).Any(e => e.TurnNumber == recorded.ActualTurn && e.ActorSeat == recorded.ActualTurnOwner) &&
                parent.Prelude is { Completed: true } && parent.Current is null && parent.ItemIndex == parent.DueIds.Count &&
                g.CardMovements.Count(m => m.CardId == gained[0] && m.Reason.Value == "program.foreign-turn-hand-gain.cleanup") == 0 &&
                (!killEndedActor || !g.CreateSnapshot(0).Players[recorded.ActualTurnOwner].IsAlive),
                "Cleanup runs after the actual-ended fact and mature due chain, even when that turn's actor died, and discards only a still-held acquired entity.");
            Require(cleanup.CardIds.All(id => !original.Contains(id)), "Starting hand entities never enter the foreign gain cleanup roster.");
            Reject(g); Cold(g, registry); Continue(g);
        }
    }
    public static void CategoryDeckGiftsUseTrueCompletionQuotaAndPrivateTargets()
    {
        VerifyCompletedTargetChanges();
        VerifyCompletedCompoundTargetsAfterNullification();
        var (g, registry) = Start();
        Use(g, "virtual", [], [1]); Reach(g, p => p.SkillPrompt?.SkillId == "ol:yingyuan");
        var gift = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:yingyuan");
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == gift.ActualHandGain!.OriginalCardUseFrameId);
        Require(use is { CardId: 0, Step: ResolutionFrameStep.Completed, Action.Type: CardActionType.Use } &&
            gift.ActualHandGain is { Category: SkillProgramCardCategory.Basic } && P(g)!.Choices.All(c => c.Cards.Count == 0) &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "A completed virtual true Use has its actual actor and Basic category; choices disclose no draw-pile entities or matching availability.");
        Reject(g); Cold(g, registry); Answer(g, c => c.Targets.Count == 0); ReachPlay(g);
        Require(E<SameCategoryDeckGiftIssuedEvent>(g).Length == 0, "Declining leaves the category quota unspent.");
        Use(g, "virtual", [], [1]); Reach(g, p => p.SkillPrompt?.SkillId == "ol:yingyuan"); Answer(g, c => c.Targets.SequenceEqual([1])); ReachPlay(g);
        Require(E<SameCategoryDeckGiftIssuedEvent>(g).Single() is { Category: SkillProgramCardCategory.Basic, ActualCount: 0 },
            "A chosen activation spends Basic once even when this fixed equipment-only deck contains no matching Basic entity.");
        Use(g, "virtual", [], [2]); ReachPlay(g);
        Require(E<SameCategoryDeckGiftIssuedEvent>(g).Length == 1, "A subsequent true Basic use cannot reopen an issued actual-turn category quota.");
        var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(g, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, g.Revision, P(g)!.PromptId, equip.PlayedCardKind));
        Reach(g, p => p.SkillPrompt?.SkillId == "ol:yingyuan"); Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:yingyuan");
        var physical = paid.ActualHandGain!.PaidIds.Single();
        Require(paid.ActualHandGain is { Stage: ActualHandGainProgramStage.MovementChildren, Category: SkillProgramCardCategory.Equipment } &&
            E<SameCategoryDeckGiftIssuedEvent>(g).Last().ActualCount == 1 &&
            g.CardMovements.Count(m => m.CardId == physical && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) &&
                m.Reason.Value == "program.same-category-deck.gift") == 1 &&
            !g.CreateSnapshot(0).Players[1].Hand.Any() && g.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == physical),
            "Equipment has its independent quota, and the paid card is a new deck entity visible only in its actual recipient's hand.");
        Reject(g); Cold(g, registry);
        // An explicit host audit follows the command-replayed paid prefix: source loss cannot repay or cancel issued movement.
        var owner = HostPlayers(g)[0]; var grant = owner.SkillGrants.Grants.Single(s => s.SkillId == "ol:yingyuan");
        owner.SkillGrants.RemoveGrant(grant.GrantId); Continue(g); ReachPlay(g);
        Require(E<SameCategoryDeckGiftIssuedEvent>(g).Length == 2 && g.CardMovements.Count(m => m.CardId == physical &&
            m.Reason.Value == "program.same-category-deck.gift") == 1, "The paid tail drains once after source loss.");
        owner.SkillGrants.Grant(new("fixture:later-yingyuan", grant.SkillId, grant.SkillInstanceId + ":later", "acquired:host-audit"));
        ((SkillRuntimeStateStore)typeof(GameEngine).GetField("_skillRuntimeState", Flags)!.GetValue(g)!).ResetSkill(0, grant.SkillId);
        Use(g, "virtual", [], [3]); ReachPlay(g);
        Require(E<SameCategoryDeckGiftIssuedEvent>(g).Length == 2, "A later skill instance shares the same owner/skill/actual-turn/category quota.");
    }
    private static void VerifyCompletedTargetChanges()
    {
        foreach (var convert in new[] { false, true })
        {
            var (g, registry) = Start(completedTargets: true);
            Use(g, "virtual", [], [1]); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-slash-fire"));
            Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == (convert ? "convert" : "skip"));
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "short-range-slash-target"));
            Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3]));
            Reach(g, p => p.SkillPrompt?.SkillId == "ol:yingyuan");
            var gift = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:yingyuan");
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == gift.ActualHandGain!.OriginalCardUseFrameId);
            Require(use is { Step: ResolutionFrameStep.Completed, ShortRangeSlashTarget: not null, Action.Type: CardActionType.Use } &&
                use.Action.TargetSeats.SequenceEqual([1, 3]) && use.TargetSeats.SequenceEqual([1, 3]) &&
                use.CardKind == (convert ? CardKind.FireSlash : CardKind.Slash) && gift.ActualHandGain!.Category == SkillProgramCardCategory.Basic &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActionId == use.Action.ActionId && e.Action.TargetSeats.SequenceEqual([1])),
                "An accepted one-target native Use completes with its issued short-range tail and, independently, its exact Slash-to-FireSlash conversion; Basic gifting follows the final true Use.");
            Require(P(g)!.Choices.All(c => c.Cards.Count == 0), "Target and kind proof does not disclose unknown deck identities.");
            Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2])); ReachPlay(g);
            Require(E<SameCategoryDeckGiftIssuedEvent>(g).Single() is { Category: SkillProgramCardCategory.Basic, ActualCount: 0 },
                "The complete two-target Use offers and spends the category once, after both native targets.");
        }
    }
    private static void VerifyCompletedCompoundTargetsAfterNullification()
    {
        var (g, registry) = Start(compoundTargets: true);
        foreach (var target in new[] { 1, 3 }) { Use(g, "fixture-equipment", [], [target]); ReachPlay(g); }
        Use(g, "fixture-many", [], []); Reach(g, p => p.SkillPrompt?.SkillId == Watch); Continue(g); ReachPlay(g);
        var borrowed = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.SequenceEqual([1, 2]));
        Accept(g, new PlayCardCommand(0, borrowed.CardId!.Value, borrowed.TargetSeats, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == "fixture:gain-compound");
        Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([3]));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "add-compound-card-target"));
        Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        var issued = E<CompletedCategoryCompoundTargetsIssuedEvent>(g).Single();
        Require(issued.OriginalTargets.SequenceEqual([1, 2]) && issued.Targets.SequenceEqual([1, 2, 3, 2]),
            "The original compound prefix and both new public target seats freeze at the exact native addition.");
        var changedInput = new[] { 3, 2 }; var detached = issued with { Targets = changedInput }; changedInput[0] = 1;
        Require(detached.Targets.SequenceEqual([3, 2]) && issued.Targets is IList<int> { IsReadOnly: true } &&
            issued.OriginalTargets is IList<int> { IsReadOnly: true }, "Compound payloads freeze constructor/init/with inputs and committed lists.");
        for (var step = 0; step < 192 && P(g)?.SkillPrompt?.SkillId != "ol:yingyuan"; step++)
        {
            if (P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 })
            {
                var window = g.ResolutionStack.OfType<NullificationWindowFrame>().Last();
                if (!window.EffectNullified) Answer(g, c => c.Cards.Count == 1);
                else Answer(g, c => c.Cards.Count == 0);
            }
            else Advance(g);
        }
        Require(P(g)?.SkillPrompt?.SkillId == "ol:yingyuan" && E<NullificationResolvedEvent>(g).Any(e => e.EffectCardId == borrowed.CardId && e.EffectNullified),
            "A truly nullified Borrowed Sword still reaches its native completed Use and category gift prompt.");
        var gift = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:yingyuan");
        Require(gift.ActualHandGain is { Category: SkillProgramCardCategory.Trick } paid && paid.OriginalCardUseFrameId == issued.CardUseFrameId &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 1 &&
            E<CompletedCategoryCompoundTargetsIssuedEvent>(g).Length == 1,
            "One accepted action, one compound issuance and one completed native Use establish Trick independently of target effects.");
        Cold(g, registry); Answer(g, c => c.Targets.Count == 0); ReachPlay(g);
    }
    private static (GameEngine, ContentRegistry) Start(bool holdInitialDraw = false, bool foreign = false, bool killEndedActor = false, bool completedTargets = false, bool compoundTargets = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(foreign, killEndedActor, completedTargets, compoundTargets));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:actual-gain-owner", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch); if (holdInitialDraw) return (g, registry);
        Continue(g); ReachPlay(g); return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Use(GameEngine g, string id, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 128; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed actual-gain fixture did not reach its boundary."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected fixed actual-gain command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)), "Cold command replay preserves original gains, paid entities, native parents and private prompts.");
    private static void Reject(GameEngine g)
    { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice changes neither the paid frame nor any viewer."); }
    private static IReadOnlyList<CharacterState> HostPlayers(GameEngine g) => (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(g)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(bool foreign, bool killEndedActor, bool completedTargets, bool compoundTargets) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-actual-hand-gains", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var foreignTrigger = foreign ? $$"""
            ,"triggers":[{"id":"actual-foreign-gain","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":false,"effects":[
              {"op":"draw","target":"owner","amount":2},{"op":"chooseOption","target":"owner","resultBind":"gains-usable","options":[{"id":"continue"}]},
              {"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"spent"},
              {"op":"moveBoundCards","target":"owner","sourceBind":"spent","destination":"discardPile","awaitMovementTriggers":true}
              {{(killEndedActor ? ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventSource\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}" : "")}}]}]
            """ : "";
            var extraPrograms = completedTargets ? """
            ,{"id":"fixture:gain-short","revision":1,"triggers":[{"id":"issued-tail","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"offerShortRangeSlashTarget","target":"owner"}]}]}
            ,{"id":"fixture:gain-fire","revision":1,"triggers":[{"id":"committed-change","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"offerCurrentSlashFireAndExtraTarget","target":"owner"}]}]}
            """ : "";
            if (compoundTargets) extraPrograms += """
            ,{"id":"fixture:gain-compound","revision":1,"triggers":[{"id":"new-pair","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["borrowedSword"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]}]}
            """;
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                {"id":"two-singles","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1}]},
                {"id":"fixture-many","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"fixture-equipment","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]}]{{foreignTrigger}}},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-hand-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.actual-hand-gain.draw","program.same-category-deck.gift"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-child","options":[{"id":"continue"}]}]},
                {"id":"paid-foreign-cleanup","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.foreign-turn-hand-gain.cleanup"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cleanup-child","options":[{"id":"continue"}]}]}]}{{extraPrograms}}]}
            """;
            var names = new[] { Driver, Watch }.Concat(completedTargets ? ["fixture:gain-short", "fixture:gain-fire"] : Array.Empty<string>())
                .Concat(compoundTargets ? ["fixture:gain-compound"] : Array.Empty<string>())
                .ToDictionary(id => id, id => (object)new { name = id, description = "真实取得与原生使用完结", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "固定共享边界") { Program = pair.Value });
            b.AddSkill(new("fixture:actual-gain-idle", "无技能", "固定对照"));
            var skills = new List<string> { "ol:yingyuan", Driver, Watch };
            if (completedTargets) skills.AddRange(["fixture:gain-short", "fixture:gain-fire"]);
            if (compoundTargets) skills.Add("fixture:gain-compound");
            b.AddGeneral(new("fixture:actual-gain-owner", "固定拥有者", "supporter", "ol:zishu", "shu", 4, skills, GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:actual-gain-peer-{i}", "固定其他角色", "supporter", "fixture:actual-gain-idle", "wei", 4, [Watch], GeneralGender.Male));
            b.AddDeck(new("fixture:actual-gain-deck", "固定装备实体", 4, 2, []) { PhysicalCards = compoundTargets
                ? Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(i % 3 == 0 ? "standard:crossbow" : i % 3 == 1 ? "classic:borrowed-sword" : "standard:nullification", Suit.Spade, 7)).ToArray()
                : Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("classic:silver-lion", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "固定取得与赠牌边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:actual-gain-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:actual-gain-owner", "fixture:actual-gain-peer-1", "fixture:actual-gain-peer-2", "fixture:actual-gain-peer-3"]));
        }
    }
}
