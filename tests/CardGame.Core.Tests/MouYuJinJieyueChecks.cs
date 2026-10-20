using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouYuJinJieyueChecks
{
    private const string Jieyue = "mou-mobile:jieyue", Xiayuan = "mou-mobile:xiayuan",
        Driver = "fixture:myjj-driver", Watch = "fixture:myjj-watch", Quiet = "fixture:myjj-quiet",
        Owner = "fixture:myjj-recipient", Ally = "fixture:myjj-source", EnemyA = "fixture:myjj-enemy-a",
        EnemyB = "fixture:myjj-enemy-b", Deck = "fixture:myjj-deck",
        GiftReason = "skill-program.mou-mobile:jieyue.gift";
    private static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);

    public static void JieyueActualEndingArmorFirstRecipientOptionalHandEquipmentGiftCold()
    {
        var decline = Scene.Start(new("recipient-declines") { Hand = [C(CardKind.Slash)] });
        decline.GiftPrompt();
        var declined = decline.Reserved();
        decline.Need(decline.V().Armor == 1 && decline.G.State.CurrentSeat == 1 && decline.G.State.Phase != TurnPhase.Play &&
            decline.E<ArmorGrantedEvent>().Count(e => e.ProgramFrameId == declined.FrameId && e.InstructionIndex == 2 && e.Amount == 1 && e.PlayerSeat == 0) == 1,
            "The allied real source reaches its own actual Ending and grants Armor1 before the different human recipient can choose a gift.");
        decline.Need(declined.Source.OwnerSeat == 1 && declined.RecipientSeat == 0 && declined.Phase.ActorSeat == 1 &&
            declined.Phase.Kind == ActualDiscardRecoveryPhaseKind.Ending,
            "The real source, actual Ending and selected beneficiary are fixed in the reserved native receipt.");
        decline.Private(); decline.RejectGift(); decline.Cold();
        var retained = decline.V().Hand.Single().Id;
        decline.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "decline");
        decline.Finish(declined);
        decline.Need(decline.V().Armor == 1 && decline.V().Hand.Single().Id == retained &&
            decline.E<MouYuJinJieyueGiftDeclinedEvent>().Count(e => e.FrameId == declined.FrameId) == 1 &&
            decline.E<MouYuJinJieyueGiftPaidEvent>().Length == 0 &&
            !decline.G.CardMovements.Any(m => m.Reason.Value == GiftReason),
            "Refusing the independent gift preserves the already granted Armor and moves no card."); decline.Cold();

        var hand = Scene.Start(new("real-hand-gift") { Hand = [C(CardKind.Dodge)] });
        var handId = hand.V().Hand.Single().Id; hand.GiftPrompt(); var handRoot = hand.Reserved();
        hand.Private(); hand.RejectGift(); hand.Cold(); hand.Give(handId);
        hand.Reach(() => hand.Hold("gift-child"));
        hand.Need(hand.V().Armor == 1 && hand.V().HandCount == 0 && hand.V(1).HandCount == 1 &&
            hand.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == handRoot.FrameId &&
                f.MouYuJinJieyue is { Stage: MouYuJinJieyueStage.GiftChildren, GiftCardId: var id } && id == handId) &&
            !hand.E<TurnEndedEvent>().Any(e => e.ActorSeat == 1 && e.TurnNumber == handRoot.Phase.TurnNumber),
            "The real Hand entity is paid before its movement child; the original Ending stays suspended on the exact once-paid gift.");
        hand.Private(); hand.RejectGift(); hand.Cold(); hand.Continue(); hand.Finish(handRoot);
        hand.CheckGift(handRoot, handId, CardLocation.Hand(0));
        hand.Need(hand.G.CreateSnapshot(0).Players[1].HandCount == 1 && hand.G.CreateSnapshot(0).Players[1].Hand.Count == 0 &&
            hand.G.CreateSnapshot(2).Players[1].Hand.Count == 0,
            "The actual Hand transfer exposes its count while the receiving source's new Hand stays opaque to other viewers."); hand.Cold();

        var lion = Scene.Start(new("equipped-silver-lion") { Equipment = [C(CardKind.SilverLion)] });
        lion.LoseOne(); lion.Play(); var lionId = lion.V().Equipment.Single().Id;
        lion.Need(lion.V().Hp == 3, "A real native HP-loss instruction wounds the beneficiary before equipment departure.");
        lion.GiftPrompt(); var lionRoot = lion.Reserved(); lion.Private(); lion.Cold(); lion.Give(lionId);
        lion.Reach(() => lion.Hold("recovery-child"));
        lion.Need(lion.V() is { Hp: 4, Armor: 1 } &&
            lion.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            lion.E<MouYuJinJieyueGiftPaidEvent>().Count(e => e.FrameId == lionRoot.FrameId && e.CardId == lionId) == 1 &&
            !lion.E<MouYuJinJieyueCompletedEvent>().Any(e => e.FrameId == lionRoot.FrameId),
            "Equipped SilverLion departs once and performs its real native recovery child before the already-paid Jieyue can complete.");
        lion.Private(); lion.RejectGift(); lion.Cold(); lion.Continue(); lion.Finish(lionRoot);
        lion.CheckGift(lionRoot, lionId, CardLocation.Equipment(0));
        lion.Need(lion.V().Equipment.Count == 0 && lion.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1,
            "The actual equipped entity departs the public slot and its native removal recovery never repeats."); lion.Cold();

        var empty = Scene.Start(new("no-hand-or-equipment")); empty.End();
        empty.Reach(() => empty.E<MouYuJinJieyueCompletedEvent>().Length == 1);
        var emptyRoot = empty.Reserved(); empty.Finish(emptyRoot);
        empty.Need(empty.V() is { Armor: 1, HandCount: 0 } && empty.V().Equipment.Count == 0 &&
            empty.E<MouYuJinJieyueGiftPaidEvent>().Length == 0 && empty.E<MouYuJinJieyueGiftDeclinedEvent>().Length == 0 &&
            !empty.SeenEmptyGiftPrompt && !empty.G.CardMovements.Any(m => m.Reason.Value == GiftReason),
            "An empty beneficiary still receives real Armor1 and completes without an empty gift prompt, fake refusal or imaginary card payment."); empty.Cold();

        var ox = Scene.Start(new("equipped-wooden-ox") { Hand = [C(CardKind.Slash)], Equipment = [C(CardKind.WoodenOx)] });
        var grain = ox.V().Hand.Single().Id; var oxId = ox.V().Equipment.Single().Id;
        ox.Need(ox.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.WoodenOx &&
            a.SelectableCardIds.Contains(grain)), "The real WoodenOx publishes its native storage action.");
        ox.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], ox.G.Revision, ox.P!.PromptId)); ox.Play();
        ox.Need(ox.V().WoodenOxGrainCount == 1, "The fixture stores a real Hand entity as private Ox grain without injecting runtime state.");
        ox.GiftPrompt(); var oxRoot = ox.Reserved(); ox.Private(); ox.Cold(); ox.Give(oxId);
        ox.Reach(() => ox.Hold("gift-child")); ox.Cold(); ox.Continue(); ox.Finish(oxRoot);
        ox.CheckGift(oxRoot, oxId, CardLocation.Equipment(0));
        ox.Need(ox.V().WoodenOxGrainCount == 0 && ox.G.CardMovements.Count(m => m.CardId == grain &&
            m.From == new CardLocation(CardZoneKind.WoodenOxGrain, 0) && m.To == CardLocation.DiscardPile &&
            m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 &&
            ox.E<MouYuJinJieyueGiftPaidEvent>().Count(e => e.FrameId == oxRoot.FrameId) == 1 &&
            !ox.G.CardMovements.Any(m => m.CardId == grain && m.Reason.Value == GiftReason),
            "Gifting the equipped Ox to Hand triggers native grain cleanup once; collateral grain is neither a second gift nor a duplicated payment."); ox.Cold();
    }

    private sealed record Spec(string Name)
    {
        public ShanheStartingCard[] Hand { get; init; } = [];
        public ShanheStartingCard[] Equipment { get; init; } = [];
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-yu-jin-jieyue", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms.mou-yu-jin-mobile." + suffix + ".json") ??
                    throw new InvalidOperationException("Missing complete mobile Yu Jin bundle.");
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var real = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Xiayuan, Jieyue })
                b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description)
                { Program = real.Programs[id], ProgramPresentation = real.Presentations[id] });
            foreach (var kind in spec.Hand.Concat(spec.Equipment).Select(c => c.Kind).Append(CardKind.Slash).Distinct())
            {
                var card = CardCatalog.Get(kind);
                b.AddCard(new("fixture:myjj-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind));
            }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var definitions = new JsonArray(
                N("{\"id\":\"" + Driver + "\",\"revision\":1,\"activations\":[{\"id\":\"lose-one\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"usesPerPhase\":1,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]}"),
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var watches = new JsonArray();
            foreach (var zone in new[] { "hand", "equipment" })
                watches.Add(N("{\"id\":\"actual-gift-" + zone + "\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"" + zone +
                    "\"],\"movementReasons\":[\"" + GiftReason + "\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("gift-child") + "]}"));
            watches.Add(N("{\"id\":\"native-removal-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("recovery-child") + "]}"));
            definitions.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            var labels = definitions.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new
            {
                name = n!["id"]!.GetValue<string>(), description = "谋于禁真实结束阶段受益人赠牌检查",
                optionLabels = n["id"]!.GetValue<string>() == Watch
                    ? new Dictionary<string, string> { ["gift-child"] = "真实赠牌子窗", ["recovery-child"] = "真实装备回复子窗" }
                    : new Dictionary<string, string>()
            });
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "节钺真实受益人", "", "standard:none", "wei", 4, [Driver, Watch]));
            b.AddGeneral(new(Ally, "完整谋于禁盟友", "", "standard:none", "wei", 4, [Xiayuan, Jieyue, Quiet]));
            b.AddGeneral(new(EnemyA, "静默对手甲", "", "standard:none", "qun", 12, [Quiet]));
            b.AddGeneral(new(EnemyB, "静默对手乙", "", "standard:none", "qun", 12, [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张实体", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:myjj-card-" + CardKind.Slash, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋于禁真实节钺小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public bool SeenEmptyGiftPrompt { get; private set; }
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public MouYuJinJieyueReservedEvent Reserved() => E<MouYuJinJieyueReservedEvent>().Single();
        public bool Gift() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-yu-jin-jieyue-gift") == true;
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch &&
            P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void GiftPrompt()
        {
            Need(E<MouYuJinJieyueReservedEvent>().Length == 0 && V().Armor == 0,
                "Normal human Play and another player's Ending cannot launch the ally's Jieyue early.");
            End(); Reach(Gift);
            Need(P is { PlayerSeat: 0, IsPrivate: true } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "give"),
                "The real AI source must voluntarily choose its allied human recipient using native public Armor scoring.");
        }
        public void Give(int id) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-yu-jin-jieyue-gift" &&
            c.Parameters.GetValueOrDefault("option-id") == "give" && c.Cards.SequenceEqual([id]));
        public void LoseOne() => Accept(new UseProgramSkillCommand(0, Driver, "lose-one", [], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") is "gift-child" or "recovery-child");
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var selected = p.Choices.SingleOrDefault(predicate);
            Need(p.PlayerSeat == 0 && selected is not null, "A genuinely published recipient choice is required.");
            Accept(new AnswerPromptCommand(0, p.PromptId, selected!.Id, G.Revision));
        }
        public void Finish(MouYuJinJieyueReservedEvent r)
        {
            Reach(() => E<TurnEndedEvent>().Any(e => e.ActorSeat == 1 && e.TurnNumber == r.Phase.TurnNumber) &&
                !G.ResolutionStack.Any(f => f.Id == r.FrameId || f.Id == r.OriginalEndingWindowId));
            Need(E<ArmorGrantedEvent>().Count(e => e.ProgramFrameId == r.FrameId && e.PlayerSeat == 0 && e.Amount == 1) == 1 &&
                E<MouYuJinJieyueCompletedEvent>().Count(e => e.FrameId == r.FrameId) == 1 &&
                E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == r.FrameId && e.SkillId == Jieyue &&
                    e.BindingId == "ending-armor-and-gift" && e.Activated && e.Completed) == 1 &&
                E<TurnEndedEvent>().Count(e => e.ActorSeat == 1 && e.TurnNumber == r.Phase.TurnNumber) == 1,
                "The original exact Ending, native Armor grant and Jieyue binding each complete once.");
        }
        public void CheckGift(MouYuJinJieyueReservedEvent r, int id, CardLocation from)
        {
            Need(E<MouYuJinJieyueGiftPaidEvent>().Count(e => e.FrameId == r.FrameId && e.RecipientSeat == 0 &&
                e.CardId == id && e.From == from && e.To == CardLocation.Hand(1)) == 1 &&
                G.CardMovements.Count(m => m.CardId == id && m.From == from && m.To == CardLocation.Hand(1) && m.Reason.Value == GiftReason) == 1 &&
                G.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == id),
                "One actual recipient entity leaves its exact original zone once and reaches the original source's Hand once.");
            var facts = G.Events.Select(e => e.Payload).ToArray();
            Need(Array.FindIndex(facts, e => e is ArmorGrantedEvent grant && grant.ProgramFrameId == r.FrameId) <
                Array.FindIndex(facts, e => e is MouYuJinJieyueGiftPaidEvent paid && paid.FrameId == r.FrameId),
                "Actual Armor grant precedes the real physical gift payment.");
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 240; step++)
            {
                if (Gift() && P!.Choices.All(c => c.Parameters.GetValueOrDefault("option-id") == "decline")) SeenEmptyGiftPrompt = true;
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The fixture ended before boundary " + line);
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind == DecisionKind.PlayCard) End();
                else if (Hold("gift-child") || Hold("recovery-child")) Continue();
                else throw new InvalidOperationException("Unexpected human boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded real boundary " + line + " " + Diagnostic());
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null) &&
                Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)),
                "Only the actual recipient sees its private choice; the skill source and spectators see no foreign Hand identities.");
            Immutable(P!.Choices); foreach (var c in P.Choices) { Immutable(c.Cards); Immutable(c.Targets); }
        }
        private void Immutable<T>(IReadOnlyList<T> values)
        {
            if (values is IList mutable && mutable.Count > 0)
            {
                var before = JsonSerializer.Serialize(values); var rejected = false;
                try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; }
                Need(rejected && before == JsonSerializer.Serialize(values), "Prepared nested choices are immutable.");
            }
        }
        public void RejectGift()
        {
            var p = P!;
            Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision));
            Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-jieyue-unowned-card"), G.Revision));
            Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves all private views, original Ending, Armor grant, paid gift and native child cursor.");
        }
        public void Reject(GameCommand command)
        {
            var before = State(); Need(!G.Submit(command).Accepted && before == State(),
                "Wrong-seat, forged-card and stale commands cannot mutate payment, Armor or original Ending.");
        }
        public void Accept(GameCommand command)
        {
            try
            {
                var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
                Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message);
            }
            catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new
        {
            spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).Armor, V(i).HandCount, Equipment = V(i).Equipment.Select(c => c.Kind), V(i).WoodenOxGrainCount }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Rules = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouYuJin", StringComparison.Ordinal)).TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Movements = G.CardMovements.TakeLast(10), Recovery = E<RecoveryAppliedEvent>().TakeLast(3)
        });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry;
            try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); }
            catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var setup = new ShanheBattleSetup(83, 1, "myjj-" + spec.Name, Owner, 4, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                [new(EnemyA) { StartingHand = [] }, new(EnemyB) { StartingHand = [] }], [new(Ally) { StartingHand = [] }], new string('d', 64));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }
}
