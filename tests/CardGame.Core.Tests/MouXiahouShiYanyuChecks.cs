using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouXiahouShiYanyuChecks
{
    private const string Discard = "skill-program.mou-mobile:yanyu.discard";
    private static ShanheStartingCard C(CardKind k, int rank = 7) => MouXiahouShiScene.C(k, rank);
    public static void ActualDiscardPhaseQuotaWholeTurnInsertedSupportCold()
    {
        var s = MouXiahouShiScene.Start(new("actual-discard-inserted-support") { Yanyu = true, InsertPlay = true,
            Hand = [C(CardKind.Slash), C(CardKind.FireSlash, 8), C(CardKind.ThunderSlash, 9), C(CardKind.Slash, 10), C(CardKind.Dodge), C(CardKind.Peach)],
            Equipment = [C(CardKind.StoneAxe)] });
        var action = s.Actions().Single(a => a.ProgramSkillId == MouXiahouShiScene.Yanyu);
        s.Need(action.MinCardCount == 1 && action.MaxCardCount == 1 && action.SelectableCardIds.Count == 4 &&
            action.SelectableCardIds.All(id => s.V().Hand.Any(c => c.Id == id && c.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) &&
            s.E<CardRecastEvent>().Length == 0, "Only one actual Slash-family entity is a legal discard cost, including both elemental Slashes; this is not a recast.");
        var excluded = s.V().Hand.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Reject(new UseProgramSkillCommand(0, MouXiahouShiScene.Yanyu, "discard-slash", [excluded], [], s.G.Revision, s.P!.PromptId));
        s.Reject(new UseProgramSkillCommand(0, MouXiahouShiScene.Yanyu, "discard-slash", [s.V().Equipment.Single().Id], [], s.G.Revision, s.P!.PromptId));
        s.Discard(CardKind.Slash); s.Play(); s.Discard(CardKind.FireSlash); s.Play();
        var first = s.E<MouXiahouShiYanyuDiscardPaidEvent>();
        s.Need(first.Length == 2 && first.All(e => e.From == CardLocation.Hand(0) && e.To == CardLocation.DiscardPile &&
            s.G.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.Reason.Value == Discard) == 1) &&
            s.E<MouXiahouShiDrawReturnedEvent>().Count(e => e.RecipientSeat == 0 && e.RequestedCount == 1 && e.ActualCount == 1) == 2 &&
            s.E<CardRecastEvent>().Length == 0 && s.E<CardUseDeclaredEvent>().Length == 0 && s.E<CardRespondedEvent>().Length == 0,
            "Two genuine native logical discards each draw one, without inventing a card Use, Played response or recast.");
        s.Need(!s.Actions().Any(a => a.ProgramSkillId == MouXiahouShiScene.Yanyu), "The normal actual Play has spent exactly its two activations.");
        s.Reject(new UseProgramSkillCommand(0, MouXiahouShiScene.Yanyu, "discard-slash", [s.V().Hand.First(c => c.Kind == CardKind.ThunderSlash).Id], [], s.G.Revision, s.P!.PromptId));
        var turn = first[0].Turn; var initialPhase = first[0].Phase;
        var support1 = s.Support(1);
        s.Need(support1.FrozenDiscardCount == 2 && support1.RequestedDraw == 6 &&
            s.E<MouXiahouShiDrawReturnedEvent>().Single(e => e.FrameId == support1.FrameId) is { RecipientSeat: 1, RequestedCount: 6, ActualCount: 6 },
            "The normal Play ending issues six real draws from this actual-turn two-discard ledger.");
        s.Play();
        s.Need(s.E<TurnEndedEvent>().Count(e => e.TurnNumber == turn.TurnNumber && e.ActorSeat == 0) == 1 &&
            !s.E<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == turn.TurnNumber && e.OwnerSeat == 0) &&
            s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 0 && e.SourceLess && e.Amount == 1) &&
            s.E<ProgramPhaseScheduledEvent>().Count(e => e.SkillId == "ol:dianjun" && e.OwnerSeat == 0 && e.Phase == TurnPhase.Play && e.Started) == 1,
            "Real Dianjun enters its late extra Play after raw TurnEnded, while the whole actual-turn cursor remains owned and unresolved."); s.Cold();
        s.Discard(CardKind.ThunderSlash); s.Play(); s.Discard(CardKind.Slash); s.Play();
        var all = s.E<MouXiahouShiYanyuDiscardPaidEvent>();
        s.Need(all.Length == 4 && all.All(e => e.Turn == turn) && all.Take(2).All(e => e.Phase == initialPhase) && all.Skip(2).All(e => e.Phase != initialPhase) &&
            all.Skip(2).Select(e => e.Phase).Distinct().Count() == 1 && !s.Actions().Any(a => a.ProgramSkillId == MouXiahouShiScene.Yanyu),
            "The late inserted Play resets the actual-phase two-use budget while retaining all four paid discards across raw TurnEnded in the same whole actual turn.");
        var support2 = s.Support(2);
        s.Need(support2.FrozenDiscardCount == 4 && support2.RequestedDraw == 12 &&
            s.E<MouXiahouShiDrawReturnedEvent>().Single(e => e.FrameId == support2.FrameId) is { RecipientSeat: 2, RequestedCount: 12, ActualCount: 12 } &&
            s.E<CardRecastEvent>().Length == 0 && s.E<MouXiahouShiYanyuDiscardPaidEvent>().Length == 4,
            "The later Play ending uses whole-turn four times three, without a six-card cap, resetting the earlier count or paying a cost again."); s.Cold();
    }

    public static void PaidDiscardChildrenSourceLossRegrantAndNativeRescueCold()
    {
        foreach (var loss in new[] { 1, 3 })
        {
            var s = MouXiahouShiScene.Start(new("paid-yanyu-child-" + loss) { Yanyu = true, ConditionalMinimum = 3, WatchCost = true, ChildLoss = loss,
                Hand = [C(CardKind.Slash), C(CardKind.FireSlash), C(CardKind.ThunderSlash), C(CardKind.Peach)] });
            var card = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
            var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
            s.Discard(CardKind.Slash); s.Reach(s.Hold);
            var paid = s.E<MouXiahouShiYanyuDiscardPaidEvent>().Single();
            s.Need(paid.CardId == card && s.Location(card) == CardLocation.DiscardPile && s.E<MouXiahouShiDrawReturnedEvent>().Length == 0 &&
                s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == paid.FrameId).MouXiahouShiYanyu is { Payment: not null },
                "The owning frame holds its once-paid physical discard while the real movement child precedes Draw1."); s.Private(); s.RejectPrompt(); s.Cold(); s.Continue();
            if (loss == 3)
            {
                s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); s.Private(); s.Cold();
                s.Answer(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach");
            }
            s.Play();
            s.Need(!s.Has(MouXiahouShiScene.Yanyu) && s.E<MouXiahouShiDrawReturnedEvent>().Single(e => e.FrameId == paid.FrameId) is { RecipientSeat: 0, RequestedCount: 1, ActualCount: 1 } &&
                s.E<MouXiahouShiCompletedEvent>().Count(e => e.FrameId == paid.FrameId && e.Accepted) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == card && m.Reason.Value == Discard) == 1 &&
                (loss != 3 || s.E<DyingResolvedEvent>().Any(e => e.VictimSeat == 0 && e.Survived)),
                "Actual source loss or a native Dying/Peach child cannot refund the paid discard, cancel its living owner's draw or replay its exact completion."); s.Cold();
            if (loss != 1) continue;
            s.Use("recover-one"); s.Play(); s.Need(s.Has(MouXiahouShiScene.Yanyu), "Real recovery regrants the conditional source.");
            s.Discard(CardKind.FireSlash); s.Play();
            s.Need(!s.Actions().Any(a => a.ProgramSkillId == MouXiahouShiScene.Yanyu) && s.E<MouXiahouShiYanyuDiscardPaidEvent>().Length == 2,
                "Regrant in the same actual Play does not reset the already paid activation budget.");
            var target = s.Support(1);
            s.Need(target.FrozenDiscardCount == 2 && target.RequestedDraw == 6 && s.E<MouXiahouShiDrawReturnedEvent>().Single(e => e.FrameId == target.FrameId).ActualCount == 6,
                "The source regrant's ending support includes both old and new source-instance payments from this actual turn."); s.Cold();
        }
    }
}

internal sealed record MouXiahouShiSpec(string Name)
{
    public int HeroSeat { get; init; }
    public bool Yanyu { get; init; }
    public bool Qiaoshi { get; init; }
    public bool InsertPlay { get; init; }
    public int ConditionalMinimum { get; init; }
    public bool WatchCost { get; init; }
    public bool WatchRecovery { get; init; }
    public bool WatchDraw { get; init; }
    public int ChildLoss { get; init; }
    public bool RecoveryReplacement { get; init; }
    public ShanheStartingCard[] Hand { get; init; } = [MouXiahouShiScene.C(CardKind.Dodge), MouXiahouShiScene.C(CardKind.Peach)];
    public ShanheStartingCard[] Equipment { get; init; } = [];
}

internal sealed class MouXiahouShiScene(MouXiahouShiSpec spec, GameEngine game, ContentRegistry registry)
{
    public const string Yanyu = "mou-mobile:yanyu", Qiaoshi = "mou-mobile:qiaoshi";
    public const string Driver = "fixture:mxhs-driver", Quiet = "fixture:mxhs-quiet", Watch = "fixture:mxhs-watch", Conditional = "fixture:mxhs-conditional";
    private const string Owner = "fixture:mxhs-owner", Ally = "fixture:mxhs-ally", EnemyA = "fixture:mxhs-enemy-a", EnemyB = "fixture:mxhs-enemy-b", Deck = "fixture:mxhs-deck";
    public static ShanheStartingCard C(CardKind kind, int rank = 7, Suit suit = Suit.Spade) => new(kind, suit, rank);
    public GameEngine G { get; private set; } = game;
    public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
    public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
    public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
    public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
    public bool Has(string skill, int? seat = null) => V(seat ?? spec.HeroSeat).Skills?.Any(s => s.ContentId == skill) == true;
    public CardLocation Location(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    public bool Action(string action) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    public bool Hold() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    public bool QiaoshiChoice() => Action("mou-xiahou-shi-qiaoshi");
    public void Use(string id, params int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
    public void Discard(CardKind kind)
    { var id = V().Hand.First(c => c.Kind == kind).Id; Accept(new UseProgramSkillCommand(0, Yanyu, "discard-slash", [id], [], G.Revision, P!.PromptId)); }
    public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
    public void Answer(Func<PromptChoice, bool> predicate)
    { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && choice is not null, "Only a published human choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
    public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    public void ChooseBranch(string branch) => Answer(c => c.Parameters.GetValueOrDefault("branch") == branch);
    public MouXiahouShiYanyuTargetAcceptedEvent Support(int target)
    {
        End(); Reach(() => P?.SkillPrompt?.SkillId == Yanyu && Action("activate")); Private(); RejectPrompt(); Cold();
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => Action("mou-xiahou-shi-yanyu-support")); Private(); Cold();
        Answer(c => c.Parameters.GetValueOrDefault("target-seat") == target.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var accepted = E<MouXiahouShiYanyuTargetAcceptedEvent>().Last(); Reach(() => E<MouXiahouShiCompletedEvent>().Any(e => e.FrameId == accepted.FrameId)); return accepted;
    }
    public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var n = 0; n < 260; n++)
        {
            if (done()) return; Need(G.State.Winner == Winner.None, "Native terminal cutoff before requested boundary " + line);
            var p = P;
            if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End();
            else if (Hold()) Continue();
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (Action("skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
        }
        throw new InvalidOperationException("Bounded native fixture could not reach " + line + " " + Diagnostic());
    }
    public void Private()
    {
        var p = P!; Need(p.IsPrivate && Enumerable.Range(0, 4).Where(v => v != p.PlayerSeat).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)),
            "The exact chooser alone sees its decision; foreign Hands remain opaque.");
        Frozen(p.Choices); foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); }
    }
    private void Frozen<T>(IReadOnlyList<T> list)
    { if (list is IList mutable && list.Count > 0) { var before = JsonSerializer.Serialize(list); var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && before == JsonSerializer.Serialize(list), "Prepared nested choice collections are immutable."); } }
    public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Zones = G.CreateCardZoneDiagnostics(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
    public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves all safe snapshots, exact typed frames, native movement/health receipts and accepted commands."); }
    public void RejectPrompt()
    { var p = P!; Need(p.PlayerSeat == 0, "Invalid-input checks use the actual human decision."); Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-mou-xiahou-shi"), G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1)); }
    public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(c).Accepted && before == State(), "Invalid actor, stale or unpublished input changes no state, RNG, stock or history."); }
    public void Accept(GameCommand c) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command"); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
    public void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why + " " + Diagnostic()); }
    private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
        Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).Armor, V(i).IsAlive, V(i).HandCount }),
        Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.Contains("MouXiahouShi", StringComparison.Ordinal)).TakeLast(18).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Moves = G.CardMovements.TakeLast(12) });
    public static MouXiahouShiScene Start(MouXiahouShiSpec spec)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec));
        GameOptions options;
        {
            var hp = spec.HeroSeat == 0 ? 3 : 6;
            var setup = new ShanheBattleSetup(7, 1, "mxhs-" + spec.Name, Owner, hp, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                [new(EnemyA, Hp: 12) { StartingHand = [C(CardKind.Dodge)] }, new(EnemyB, Hp: 12) { StartingHand = [C(CardKind.Dodge)] }],
                [new(Ally, Hp: spec.RecoveryReplacement ? 1 : spec.HeroSeat == 1 ? 3 : 12) { StartingHand = [C(CardKind.Dodge)] }], new string('a', 64));
            options = new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 };
        }
        var scene = new MouXiahouShiScene(spec, GameEngine.CreateStandard(options, registry), registry); scene.Accept(new StartGameCommand());
        scene.Play(); return scene;
    }
    private sealed class Fixture(MouXiahouShiSpec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-xiahou-shi-native", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, spec.InsertPlay ? ["mou:xiahou-shi-mobile", "ol:zhang-yi"] : ["mou:xiahou-shi-mobile"]);
            foreach (var k in spec.Hand.Concat(spec.Equipment).Select(c => c.Kind).Concat(new[] { CardKind.Dodge, CardKind.Peach, CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }).Distinct())
            { var d = CardCatalog.Get(k); b.AddCard(new("fixture:mxhs-card-" + k, d.DisplayName, d.CategoryName, d.Description, k)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string Pause() => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"continue\"}]}";
            static JsonNode A(string id, string e, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + e + "]}");
            var acts = new JsonArray(A("loss-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("loss-target", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", 1), A("recover-target-two", "{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":2}", 1),
                A("damage-one", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", 1), A("damage-three", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":3}", 1),
                A("self-damage-one", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"), A("foreign-one", "{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":1}", 1),
                A("armor-one", "{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":1}", 1));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts,
                ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":30,\"priority\":0}]") },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var watches = new JsonArray();
            var tail = spec.ChildLoss == 0 ? "" : ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.ChildLoss + "}";
            if (spec.WatchCost) watches.Add(N("{\"id\":\"cost-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.mou-mobile:yanyu.discard\"],\"usageScope\":\"game\",\"usageLimit\":1,\"priority\":1000,\"optional\":false,\"effects\":[" + Pause() + tail + "]}"));
            if (spec.WatchRecovery) watches.Add(N("{\"id\":\"recovery-child\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"priority\":1000,\"optional\":false,\"effects\":[" + Pause() + tail + "]}"));
            if (spec.WatchDraw) watches.Add(N("{\"id\":\"draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.mou-mobile:qiaoshi.Draw\"],\"usageScope\":\"game\",\"usageLimit\":1,\"priority\":1000,\"optional\":false,\"effects\":[" + Pause() + tail + "]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            if (spec.ConditionalMinimum > 0) defs.Add(new JsonObject { ["id"] = Conditional, ["revision"] = 1,
                ["hpSkillGrants"] = new JsonArray(new[] { Yanyu, Qiaoshi }.Where(id => id == Yanyu && spec.Yanyu || id == Qiaoshi && spec.Qiaoshi)
                    .Select(id => (JsonNode)new JsonObject { ["id"] = "healthy-" + id, ["skillId"] = id, ["minimumHp"] = spec.ConditionalMinimum }).ToArray()) });
            if (spec.RecoveryReplacement) defs.Add(N("{\"id\":\"fixture:mxhs-redirect\",\"revision\":1,\"cardPolicies\":[{\"id\":\"native-ally-recovery\",\"kind\":\"redirectOwnTurnFactionRecovery\",\"factionId\":\"wu\",\"value\":1,\"providerDrawCount\":1}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "谋夏侯氏真实原生小局",
                optionLabels = n!["id"]!.GetValue<string>() == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            string[] Skills(int seat)
            {
                var ids = new List<string>(); if (seat == 0) ids.Add(Driver); else ids.Add(Quiet);
                if (watches.Count > 0) ids.Add(Watch);
                if (seat == spec.HeroSeat) { if (spec.ConditionalMinimum > 0) ids.Add(Conditional); else { if (spec.Yanyu) ids.Add(Yanyu); if (spec.Qiaoshi) ids.Add(Qiaoshi); } }
                if (seat == 0 && spec.InsertPlay) ids.Add("ol:dianjun");
                if (seat == 1 && spec.RecoveryReplacement) ids.Add("fixture:mxhs-redirect");
                return ids.ToArray();
            }
            b.AddGeneral(new(Owner, "谋夏侯氏真实检查人", "", "standard:none", spec.RecoveryReplacement ? "wu" : "shu", spec.HeroSeat == 0 ? 3 : 6, Skills(0)));
            foreach (var (id, seat) in new[] { (Ally, 1), (EnemyA, 2), (EnemyB, 3) })
                b.AddGeneral(new(id, "实际其他角色", "", "standard:none", "wu", spec.HeroSeat == seat ? 3 : 12, Skills(seat)));
            b.AddDeck(new(Deck, "固定真实点数牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:mxhs-card-Dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋夏侯氏原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
