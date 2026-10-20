using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheShiduChecks
{
    private const string Skill = StandardShanheS5ShiduPackage.ShiduId, Activation = "contest-take-return";
    private const string ShiduBoss = StandardShanheS5ShiduPackage.CaoxingShiduId, TianbianBoss = StandardShanheS5ShiduPackage.CaoxingTianbianId;
    private const string Liushi = "ol:liu-shi", Zhanwan = "ol:zhan-wan", Tianbian = "classic:tianbian";
    private const string Owner = "fixture:shidu-owner", Victim = "fixture:shidu-victim", Peer = "fixture:shidu-peer", Deck = "fixture:shidu-deck";
    private const string Gain = "fixture:shidu-gain", Gate = "fixture:shidu-health-source", Watch = "fixture:shidu-damage";
    private const string EndWatch = "fixture:shidu-discard-end", Quiet = "fixture:shidu-quiet";
    private static readonly CardMoveReason TransferReason = new("skill-program." + Skill + ".MoveBoundCards");
    private static readonly CardMoveReason ChildDrawReason = new("skill-program." + Gain + ".Draw");
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:shidu-production", TianbianBoss, 4, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('d', 64))));

    public static void LiushiTopHeartPindianObtainsWholeHandThenCapturesLiveHalfCold()
    {
        var s = Scene.Start("win"); s.Play();
        VerifyPublicAiEstimate(s);
        var top = s.PrepareTop(); var oldHand = s.V().Hand.Select(card => card.Id).ToHashSet();
        var victimHand = s.V(1).Hand.Select(card => card.Id).ToHashSet();
        Need(s.V(1).Hp == 3 && s.Mark(1) == 1 && oldHand.Count == 3 && victimHand.Count == 5,
            "The real Liushi Slash leaves its original Heart card on top and lowers the four-HP victim to three with one persistent hand-limit effect.");
        s.BeginContest(s.V().Hand.First().Id, top: true, expectedTop: top);
        s.Until(() => s.AtGain("obtained"));
        var root = s.Root; var result = s.Result();
        var acquired = victimHand.Where(id => id != result.OpponentCardId).ToHashSet();
        Need(result is { SourceSeat: 0, OpponentSeat: 1, SourceRank: 13, SourceWon: true } && result.SourceCardId == top &&
            acquired.Count == 4 && s.V().HandCount == 7 && s.V(1).HandCount == 0 && s.Returns().Length == 0 &&
            s.E<ProgramPindianHalfReturnCountEvent>().Length == 0,
            "Native Tianbian uses the exact previously topdecked printed Heart-one entity as King; winning obtains all four remaining victim hand cards before any half-hand amount is captured.");
        s.AssertAcquisition(root, acquired); s.AssertPindianEntity(top, CardLocation.DrawPile); s.Cold(); s.Continue();
        s.Until(() => s.AtGain("after-gain"));
        Need(s.V().HandCount == 8 && s.V(1).HandCount == 0 && s.ChildDraws().Length == 1 &&
            s.Root.PindianHandReturn is { AcquisitionPaid: true, ReturnCount: null } && s.E<ProgramPindianHalfReturnCountEvent>().Length == 0,
            "The genuine acquisition child draws one additional original entity, changing the live source hand from seven to eight before return-count capture.");
        s.Cold(); s.Continue(); s.Until(s.AtReturn);
        s.AssertReturnDraft(4, 8); s.Private(); s.Cold();
        var toReturn = s.V().Hand.Where(card => !acquired.Contains(card.Id)).Select(card => card.Id).ToArray();
        Need(toReturn.Length == 4 && oldHand.IsSubsetOf(toReturn),
            "The source may choose its three old hand cards and the real child-drawn card, rather than being restricted to the newly obtained cards.");
        s.ReturnCards(toReturn, coldAfterFirst: true); s.Play();
        Need(s.V().Hand.Select(card => card.Id).ToHashSet().SetEquals(acquired) &&
            s.V(1).Hand.Select(card => card.Id).ToHashSet().SetEquals(toReturn) && s.Returns().Length == 4 &&
            toReturn.All(id => s.Returns().Count(move => move.CardId == id) == 1) &&
            s.E<ProgramPindianHalfReturnCountEvent>() is [{ HandCount: 8, ReturnCount: 4 }] &&
            s.E<ProgramSkillResolvedEvent>().Count(fact => fact.FrameId == root.Id && fact.SkillId == Skill && fact.Completed) == 1 &&
            s.E<CardUseDeclaredEvent>() is [{ SourceSeat: 0, CardKind: CardKind.Slash }],
            "One real half-hand return transfers exactly four freely chosen original Hand entities, keeps the stolen four and finishes once; Pindian itself is never a Slash/card Use.");
        s.AssertAcquisition(root, acquired); s.AssertPindianEntity(top, CardLocation.DrawPile); s.Cold();
        Need(s.G.GetHumanLegalActions().All(action => action.ProgramSkillId != Skill), "A completed true Pindian consumes this activation's one opportunity in the current Play phase.");
        s.Reject(new UseProgramSkillCommand(0, Skill, Activation, [s.V().Hand.First().Id], [1], s.G.Revision, s.P!.PromptId));

        s.EndPlay(); s.Until(() => s.AtEnd("before"));
        var discards = s.HandDiscards();
        Need(s.V(1) is { Hp: 3, HandLimit: 2, HandCount: 2 } && discards.Length == 2 && discards.All(move => toReturn.Contains(move.CardId)) && s.Mark(1) == 1,
            "The same real returned hand reaches its native discard phase: HP3 minus Liushi gives limit two and exactly two of the four returned entities are discarded.");
        s.Cold(); s.Continue(); s.Until(() => s.AtEnd("after"));
        Need(s.V().HandCount == 6 && s.Mark(1) == 0 && s.V(1).HandLimit == 3 &&
            s.E<ProgramLiushiDrawPaidEvent>() is [{ Payment.RequestedCount: 2, Payment.ActualCount: 2 }] &&
            s.E<ProgramLiushiEffectClearedEvent>() is [{ RemovedCount: 1, OwnerSeat: 0, TargetSeat: 1 }],
            "The original Zhanwan responds to those two actual discards with one real Draw2 and clears precisely the earlier Liushi effect, without replaying Shidu's acquisition or return.");
        s.AssertAcquisition(root, acquired); Need(s.Returns().Length == 4, "The later discard reward cannot repeat the earlier four-card hand transfer."); s.Cold();
    }

    public static void LosingTiedAndEmptiedContestsDoNotInventTransfersCold()
    {
        foreach (var scene in new[] { "loss", "tie" })
        {
            var s = Scene.Start(scene); s.Play();
            var card = s.V().Hand.First(card => card.Suit == Suit.Club && card.Rank == 3).Id;
            s.BeginContest(card, top: false); s.Play();
            var result = s.Result();
            Need(!result.SourceWon && result.SourceCardId == card && result.SourceRank == 3 &&
                (scene == "tie" ? result.OpponentRank == 3 : result.OpponentRank > result.SourceRank) &&
                s.Taken().Length == 0 && s.Returns().Length == 0 && s.ChildDraws().Length == 0 &&
                s.E<ProgramPindianHandAcquiredEvent>().Length == 0 && s.E<ProgramPindianHalfReturnCountEvent>().Length == 0 &&
                s.V().HandCount == 3 && s.V(1).HandCount == 4,
                "A genuine lost or tied native contest pays its two actual Pindian cards but cannot obtain a foreign hand, invent a return, run acquisition children or treat equality as victory. " + s.Diagnostic());
            s.AssertPindianEntity(card, CardLocation.Hand(0)); s.Cold();
            Need(s.E<ProgramSkillResolvedEvent>().Count(fact => fact.SkillId == Skill && fact.ActivationId == Activation) == 1 &&
                s.G.GetHumanLegalActions().All(action => action.ProgramSkillId != Skill),
                "An unwon actual contest still consumes the phase-once activation and returns to native Play exactly once.");
        }
        var empty = Scene.Start("empty"); empty.Play();
        var king = empty.V().Hand.Single(card => card.Rank == 13).Id;
        var remaining = empty.V().Hand.Where(card => card.Id != king).Select(card => card.Id).ToHashSet();
        empty.BeginContest(king, top: false); empty.Until(empty.AtReturn);
        empty.AssertAcquisition(empty.Root, new HashSet<int>()); empty.AssertReturnDraft(1, 3); empty.Private(); empty.Cold();
        var returned = empty.V().Hand.First().Id;
        Need(remaining.Count == 3 && remaining.Contains(returned), "The zero-acquisition return must use one of the source's three genuine remaining original hand cards.");
        empty.ReturnCards([returned], coldAfterFirst: false); empty.Play();
        Need(empty.Result() is { SourceWon: true, SourceRank: 13, OpponentRank: 2 } && empty.V().HandCount == 2 && empty.V(1).HandCount == 1 &&
            empty.V(1).Hand.Single().Id == returned && empty.V().Hand.Select(card => card.Id).ToHashSet().SetEquals(remaining.Where(id => id != returned)) &&
            empty.E<ProgramPindianHandAcquiredEvent>() is [{ Receipt.AcquisitionPaid: true, Receipt.Materials.Count: 0 }] &&
            empty.E<ProgramPindianHalfReturnCountEvent>() is [{ HandCount: 3, ReturnCount: 1 }] && empty.Taken().Length == 0 &&
            empty.Returns() is [{ CardId: var returnedId }] && returnedId == returned && empty.ChildDraws().Length == 0 &&
            empty.E<ProgramSkillResolvedEvent>().Count(fact => fact.SkillId == Skill && fact.Completed) == 1,
            "When Pindian consumes the opponent's only card, zero acquired entities still leave a real floor(three/2)=one obligation: exactly one pre-existing source card is returned, without inventing an acquisition or gain child. " + empty.Diagnostic());
        empty.AssertPindianEntity(king, CardLocation.Hand(0)); empty.Cold();
    }

    public static void PaidObtainSourceLossTargetDeathAndActualRecipesStayIsolatedCold()
    {
        foreach (var scene in new[] { "paid-loss", "victim-death" })
        {
            var s = Scene.Start(scene); s.Play(); var top = s.PrepareTop(); var oldHand = s.V(1).Hand.Select(card => card.Id).ToHashSet();
            s.BeginContest(s.V().Hand.First().Id, top: true, expectedTop: top); s.Until(() => s.AtGain("obtained"));
            var root = s.Root; var acquired = oldHand.Where(id => id != s.Result().OpponentCardId).ToHashSet();
            s.AssertAcquisition(root, acquired); s.Cold(); s.Continue();
            if (scene == "victim-death")
            {
                s.Until(() => s.At(Gain) && s.P!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target" && choice.Targets.SequenceEqual([1])));
                s.Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target" && choice.Targets.SequenceEqual([1]));
            }
            s.Until(() => s.AtGain("after-gain"));
            Need(s.V().HandCount == 8 && s.Root.PindianHandReturn is { AcquisitionPaid: true, ReturnCount: null } &&
                (scene == "paid-loss" ? s.V().Hp == 19 && !s.Has(Skill) : !s.V(1).IsAlive && s.G.State.Winner == Winner.None),
                "Real acquisition children either remove the HP-derived Shidu source or kill its original opponent after the whole-hand acquisition was paid, before capturing a return count. " + s.Diagnostic());
            s.Cold(); s.Continue();
            if (scene == "paid-loss")
            {
                s.Until(s.AtReturn); s.AssertReturnDraft(4, 8); s.Private();
                var returned = s.V().Hand.Take(4).Select(card => card.Id).ToArray();
                s.ReturnCards(returned, coldAfterFirst: true); s.Play();
                Need(s.V().HandCount == 4 && s.V(1).HandCount == 4 && s.Returns().Length == 4 &&
                    returned.ToHashSet().SetEquals(s.Returns().Select(move => move.CardId)) && !s.Has(Skill),
                    "Loss of the original source after paid acquisition cannot let its owner retain the whole hand for free; the exact four-card obligation still pays and returns once.");
            }
            else
            {
                s.Play();
                Need(s.V().HandCount == 8 && s.Returns().Length == 0 && s.E<ProgramPindianHalfReturnCountEvent>().Length == 0 &&
                    s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(frame => frame.Id != root.Id),
                    "A genuinely dead opponent is not offered a half-hand return or a fabricated destination; all already-issued acquisition children drain before the paid root safely completes.");
            }
            Need(s.E<ProgramSkillResolvedEvent>().Count(fact => fact.FrameId == root.Id && fact.SkillId == Skill) == 1 && s.ChildDraws().Length == 1,
                "The original paid root and its real child Draw1 each finish exactly once across source or recipient invalidation.");
            s.AssertAcquisition(root, acquired); s.AssertPindianEntity(top, CardLocation.DrawPile); s.Cold();
        }
        var dead = Scene.Start("owner-death"); dead.Play();
        var selected = dead.V().Hand.Single(card => card.Rank == 13).Id;
        var victimHand = dead.V(dead.TargetSeat).Hand.Select(card => card.Id).ToHashSet();
        Need(dead.V(1).IsAlive && dead.V(1).TeamId == dead.V().TeamId && dead.V(dead.TargetSeat).TeamId != dead.V().TeamId,
            "The source has a genuine living ally in the two-versus-two recipe, so its later death cannot be concealed by game-over stack cleanup.");
        dead.BeginContest(selected, top: false); dead.Until(() => dead.AtGain("obtained"));
        var paidRoot = dead.Root;
        var gainFrame = dead.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Gain);
        var paidCards = victimHand.Where(id => id != dead.Result().OpponentCardId).ToHashSet();
        Need(paidCards.Count == 4 && dead.V().HandCount == 7 && dead.V(dead.TargetSeat).HandCount == 0,
            "The actual winning contest has already paid the four-card enemy hand acquisition before its genuine gain child kills the source.");
        dead.AssertAcquisition(paidRoot, paidCards); dead.AssertPindianEntity(selected, CardLocation.Hand(0)); dead.Cold(); dead.Continue();
        dead.Until(() => !dead.V().IsAlive && !dead.G.ResolutionStack.Any(frame => frame.Id == paidRoot.Id || frame.Id == gainFrame.Id) &&
            dead.E<ProgramSkillResolvedEvent>().Any(fact => fact.FrameId == paidRoot.Id));
        Need(dead.G.State.Winner == Winner.None && dead.V(1).IsAlive && dead.V(dead.TargetSeat).IsAlive &&
            dead.E<PlayerDiedEvent>().Count(fact => fact.VictimSeat == 0) == 1 &&
            dead.E<ProgramSkillResolvedEvent>().Where(fact => fact.FrameId == paidRoot.Id).ToArray() is
                [{ SkillId: Skill, ActivationId: Activation, OwnerSeat: 0, Completed: false }] &&
            dead.E<ProgramBindingResolvedEvent>().Count(fact => fact.FrameId == gainFrame.Id && fact.SkillId == Gain) == 1 &&
            dead.E<ProgramPindianHalfReturnCountEvent>().Length == 0 && dead.Returns().Length == 0 && dead.ChildDraws().Length == 0,
            "Actual lose-HP/dying/let-die drains the original gain child and returns the paid Shidu root once while battle continues: no half-hand prompt, gift or dead-source Draw is invented. " + dead.Diagnostic());
        dead.AssertAcquisition(paidRoot, paidCards); dead.AssertPindianEntity(selected, CardLocation.Hand(0)); dead.Cold();
        VerifyActualRecipes(); VerifyLoaderBoundaries();
    }

    private static void VerifyPublicAiEstimate(Scene scene)
    {
        var before = State(scene.G); var view = scene.G.CreateSnapshot(0);
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.G)!;
        var contextFactory = typeof(GameEngine).GetMethod("CreateProgramAiPublicContext", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(CharacterState)], null)!;
        var actorFactory = typeof(GameEngine).GetMethod("CreateAiSkillContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var publicContext = (ProgramAiPublicContext)contextFactory.Invoke(scene.G, [players[0]])!;
        var source = (PlayerSkillContext)actorFactory.Invoke(scene.G, [players[0], 0, view])!;
        var target = (PlayerSkillContext)actorFactory.Invoke(scene.G, [players[1], 0, view])!;
        var effects = Production.Value.GetSkill(Skill).Program!.Activations.Single(activation => activation.Id == Activation).Effects;
        var facts = publicContext with { SelectedTarget = target, ActivationCardCount = 1 };
        var won = ProgramCompositionAi.Estimate(effects, source, publicContext: facts with { PindianWon = _ => true, PindianOutcome = _ => true });
        var lost = ProgramCompositionAi.Estimate(effects, source, publicContext: facts with { PindianWon = _ => false, PindianOutcome = _ => false });
        var unknown = ProgramCompositionAi.Estimate(effects, source, publicContext: facts);
        Need(source.HandCount == 4 && target.HandCount == 5 && won.Hint is { OwnerDraw: 1, TargetDraw: 0, TargetValueAdjustment: -14 } &&
            lost.Hint is { OwnerDraw: 0, TargetDraw: 0, TargetValueAdjustment: -7 } &&
            unknown.Hint.OwnerDraw == (int)Math.Round((won.Hint.OwnerDraw + lost.Hint.OwnerDraw) / 2d) &&
            unknown.Hint.TargetDraw == (int)Math.Round((won.Hint.TargetDraw + lost.Hint.TargetDraw) / 2d) &&
            unknown.Hint.TargetValueAdjustment == (won.Hint.TargetValueAdjustment + lost.Hint.TargetValueAdjustment) / 2d &&
            double.IsFinite(won.Score) && double.IsFinite(lost.Score) && double.IsFinite(unknown.Score) &&
            Math.Abs(unknown.Score - (won.Score + lost.Score) / 2d) < 0.000000001d &&
            view.Players.Where(player => player.Seat != 0).All(player => player.Hand.Count == 0) && State(scene.G) == before,
            "The actual five-node program's pure public estimate deducts both ordinary Pindian inputs before taking four and returning three; negative target card balance uses the shared value adjustment, the source activation card is priced separately, and unknown victory averages the two coherent branches without reading foreign faces or changing runtime. " +
            JsonSerializer.Serialize(new { source.HandCount, TargetHand = target.HandCount, Won = won, Lost = lost, Unknown = unknown }));
    }

    private static void VerifyActualRecipes()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(node => node.Encounters ?? []).SelectMany(encounter => encounter.Enemies).All(member => member.GeneralId is not (ShiduBoss or TianbianBoss)),
            "The two user-approved local S5 skill combinations are absent from the retained 2024 edition.");
        for (var tier = 1; tier <= 3; tier++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var node = definition.Nodes.Single(node => node.Id == "guangcheng");
            var encounters = node.Encounters ?? throw new InvalidOperationException("The actual Guangcheng route must contain encounter recipes.");
            Need(encounters.Any(encounter => encounter.Id == "guangcheng:caoxing-s5") &&
                encounters.Any(encounter => encounter.Id == "guangcheng:caoxing-shidu-s5") == (tier >= 2) &&
                encounters.Any(encounter => encounter.Id == "guangcheng:caoxing-shidu-tianbian-s5") == (tier == 3),
                "Every local tier retains the base recipe; the explicitly local Shidu and Shidu-plus-Tianbian candidates appear only in the selected tiers.");
            if (tier == 1) continue;
            var id = tier == 2 ? ShiduBoss : TianbianBoss;
            var encounter = encounters.Single(encounter => encounter.Enemies.Any(member => member.GeneralId == id));
            var setup = new ShanheBattleSetup(83, 1, node.Id, "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies,
                encounter.Allies ?? [], ShanheCampaignContent.GetContentHash(definition));
            var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var game = StandardShanheCampaignCatalog.CreateBattle(setup);
            var result = game.Submit(new StartGameCommand()); Need(result.Accepted, result.Error?.Message ?? "The actual Shidu candidate did not start.");
            var enemy = game.CreateSnapshot(0).Players.Single(player => player.GeneralId == id);
            string[] skills = tier == 2 ? [Liushi, Zhanwan, Skill] : [Liushi, Zhanwan, Skill, Tianbian];
            Need(enemy is { Hp: 4, MaxHp: 4, HandCount: 4, Armor: 0 } && enemy.Equipment.Count == 0 &&
                enemy.Skills?.Select(skill => skill.ContentId).ToHashSet().SetEquals(skills) == true &&
                encounter.Enemies is [{ MaxHpBonus: 0, Hp: null, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] &&
                (encounter.Enemies[0].ExtraSkillIds?.Count ?? 0) == 0,
                "The actual encounter uses raw four HP and four genuine opening cards with the exact selected printed programs, no invented gear or high-tier growth.");
            var before = State(game); game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Need(State(game) == before, "The actual conditional boss package and its frozen recipe survive native cold startup.");
            var plain = setup with { EnemyRoster = [new(StandardShanheS5CaoxingPackage.CaoxingId)] };
            var plainRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(plain);
            Need(!plainRegistry.Generals.ContainsKey(ShiduBoss) && !plainRegistry.Generals.ContainsKey(TianbianBoss) && !plainRegistry.Skills.ContainsKey(Skill),
                "Warming a combination recipe cannot leak its conditional package into the unchanged ordinary Cao Xing recipe.");
            if (tier == 2)
            {
                var extra = plain with { ExtraSkillIds = [Skill] };
                var extraRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(extra);
                Need(extraRegistry.Skills.ContainsKey(Skill) && extraRegistry.Generals.ContainsKey(StandardShanheS5CaoxingPackage.CaoxingId) &&
                    !StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Skill) &&
                    StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Skill),
                    "The same basic general recipe separates its real extra-Shidu package in both warm-cache directions; no equipped skill is lost and no unequipped recipe gains it.");
            }
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        string Read(string suffix)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Read("shanhe-s5-shidu.rules.json"); var presentation = Read("shanhe-s5-shidu.presentation.json");
        _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var change in new Action<JsonObject>[]
        {
            activation => activation["pindianHandReturn"] = false,
            activation => activation["minCards"] = 0,
            activation => activation["usageGroup"] = "foreign-ledger",
            activation => activation["otherCharactersOnly"] = true,
            activation => activation["effects"]![3]!["zones"] = new JsonArray("equipment"),
            activation => activation["effects"]![4]!["destination"] = "discardPile"
        })
        {
            var copy = JsonNode.Parse(rules)!; change(copy["skills"]![0]!["activations"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The shared standalone paid hand-return opt-in rejects false configuration, missing source payment, foreign usage ledgers or target domains, non-Hand half selection and a mismatched return destination.");
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Facts = game.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
        game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public int TargetSeat => scene == "owner-death" ? 2 : 1;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public ProgramSkillFrame Root => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Skill);
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public PindianResult Result() => E<PindianResultDeterminedEvent>().Single(fact => fact.SkillId == Skill).Result;
        public int Mark(int seat) => V(seat).Markers?.SingleOrDefault(marker => marker.Kind == PlayerMarkerKind.LiuShi)?.Count ?? 0;
        public bool Has(string id) => V().Skills?.Any(skill => skill.ContentId == id) == true;
        public bool At(string id) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == id;
        public bool AtGain(string option) => At(Gain) && P!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == option);
        public bool AtEnd(string option) => At(EndWatch) && P!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == option);
        public bool AtReturn() => P is { PlayerSeat: 0 } && P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && choice.Parameters.GetValueOrDefault("result-bind") == "returned");
        public CardMovementRecord[] Taken() => G.CardMovements.Where(move => move.From == CardLocation.Hand(TargetSeat) && move.To == CardLocation.Hand(0) && move.Reason == TransferReason).ToArray();
        public CardMovementRecord[] Returns() => G.CardMovements.Where(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.Hand(TargetSeat) && move.Reason == TransferReason).ToArray();
        public CardMovementRecord[] ChildDraws() => G.CardMovements.Where(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) && move.Reason == ChildDrawReason).ToArray();
        public CardMovementRecord[] HandDiscards() => G.CardMovements.Where(move => move.From == CardLocation.Hand(1) && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.HandLimitDiscard).ToArray();
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, (result.Error?.Message ?? "Actual Shidu command rejected.") + " " + Diagnostic()); }
        public void Reject(GameCommand command)
        { var before = State(G); var result = G.Submit(command); Need(!result.Accepted && State(G) == before, "A forged, premature or spent-phase input is atomically rejected without changing an original entity or the real prompt."); }
        public void Answer(Func<PromptChoice, bool> predicate) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(predicate).Id, G.Revision));
        public void Continue() => Answer(choice => choice.Parameters.ContainsKey("option-id"));
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public int PrepareTop()
        {
            var card = V().Hand.Single(card => card.Suit == Suit.Heart).Id;
            Accept(new UseProgramSkillCommand(0, Liushi, "topdeck-heart-slash", [card], [1], G.Revision, P!.PromptId)); Play();
            Need(G.CreateCardZoneDiagnostics().Single(item => item.CardId == card).Location == CardLocation.DrawPile &&
                G.CreateCardZoneDiagnostics().Where(item => item.Location == CardLocation.DrawPile).OrderBy(item => item.ZoneIndex).Last().CardId == card &&
                G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.DrawPile) == 1,
                "The true preceding Liushi pays and leaves exactly its original low printed Heart card on the native deck top.");
            Cold(); return card;
        }
        public void BeginContest(int card, bool top, int? expectedTop = null)
        {
            Accept(new UseProgramSkillCommand(0, Skill, Activation, [card], [TargetSeat], G.Revision, P!.PromptId));
            Until(() => P is { PlayerSeat: 0 } prompt && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") is "pindian-top" or "pindian-source-card") ||
                G.CreateSnapshot(TargetSeat).PendingDecision is { IsPrivate: true } opponent && opponent.PlayerSeat == TargetSeat);
            if (P is { PlayerSeat: 0 } sourcePrompt)
            {
                Need(sourcePrompt.SkillPrompt?.SkillId == Skill && sourcePrompt.IsPrivate && Enumerable.Range(1, 3).All(seat => G.CreateSnapshot(seat).PendingDecision is null),
                    "Only the actual Pindian source sees its real source/top-card choice; the test does not manufacture a private prompt.");
                Need(sourcePrompt.Choices.Count == 2 &&
                    sourcePrompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "pindian-source-card").Cards.SequenceEqual([card]) &&
                    sourcePrompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "pindian-top").Cards.Count == 0 &&
                    sourcePrompt.Choices.All(choice => choice.Targets.Count == 0),
                    "The native source/top prompt offers only the activation's originally selected hand card or the hidden deck top; it cannot reselect an arbitrary different hand card.");
                if (top)
                {
                    var choice = sourcePrompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "pindian-top");
                    Need(choice.Cards.Count == 0, "The top-source choice carries no concealed physical card ID before reservation."); Cold();
                    Answer(choice => choice.Parameters.GetValueOrDefault("action") == "pindian-top");
                }
                else Answer(choice => choice.Cards.SequenceEqual([card]) && choice.Parameters.GetValueOrDefault("action") == "pindian-source-card");
            }
            else Need(!top, "The actual source must receive Tianbian's native top-card choice before the opponent commits.");
            Need(P is null && G.CreateSnapshot(TargetSeat).PendingDecision is { IsPrivate: true } opponentPrompt && opponentPrompt.PlayerSeat == TargetSeat &&
                Enumerable.Range(0, 4).All(seat => G.CreateSnapshot(seat).PublicRevealedCards.Count == 0),
                "The original NPC opponent receives its own private real card selection; no observer sees either Pindian card before both commit.");
            if (top)
            {
                var frame = G.ResolutionStack.OfType<PindianFrame>().Single();
                Need(frame.SourceUsesDrawPileTop && frame.SourceCardId == expectedTop &&
                    G.CreateCardZoneDiagnostics().Single(item => item.CardId == expectedTop).Location == CardLocation.Processing,
                    "The native Pindian reserves exactly the preceding Liushi top entity once without consuming the originally preselected hand card.");
            }
            Cold();
        }
        public void AssertAcquisition(ProgramSkillFrame root, IReadOnlySet<int> ids)
        {
            var fact = E<ProgramPindianHandAcquiredEvent>().Single(); var receipt = fact.Receipt; var result = Result();
            Need(receipt.FrameId == root.Id && receipt.Source is { OwnerSeat: 0, SkillId: Skill, BindingId: Activation } &&
                receipt.Source.SkillInstanceId == root.SkillInstanceId && receipt.GameplayHash == root.GameplayHash && receipt.ReturnCount is null &&
                receipt.AcquisitionPaid && receipt.TargetSeat == TargetSeat && receipt.PindianFrameId == E<PindianResultDeterminedEvent>().Single(fact => fact.SkillId == Skill).FrameId &&
                receipt.SourceRank == result.SourceRank && receipt.OpponentRank == result.OpponentRank &&
                receipt.Materials.Select(item => item.CardId).ToHashSet().SetEquals(ids) && receipt.Materials.Count == ids.Count &&
                Taken().Length == ids.Count && Taken().All(move => move.Sequence > receipt.SequenceBefore && move.Sequence <= receipt.SequenceAfter &&
                    receipt.Materials.Any(item => item.CardId == move.CardId && item.Sequence == move.Sequence)) && receipt.Materials is IList<PindianHandReturnMaterial> { IsReadOnly: true },
                "One immutable owning receipt freezes exactly the original post-contest Hand-to-Hand materials and movement interval, with its real source instance, hash and native Pindian result.");
        }
        public void AssertReturnDraft(int count, int handCount)
        {
            Need(Root.OwnedCardSelection is { CardOwnerSeat: 0, ResultBind: "returned" } draft && draft.RequiredCount == count && draft.SelectedCardIds.Count == 0 &&
                draft.CandidateLocations.All(location => location == CardLocation.Hand(0)) && draft.CandidateCardIds.ToHashSet().SetEquals(V().Hand.Select(card => card.Id)) &&
                Root.PindianHandReturn is { AcquisitionPaid: true } receipt && receipt.ReturnCount == count &&
                E<ProgramPindianHalfReturnCountEvent>() is [{ OwnerSeat: 0, TargetSeat: 1 } captured] && captured.FrameId == Root.Id && captured.HandCount == handCount && captured.ReturnCount == count &&
                V().HandCount == handCount && P!.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") != "finish-owned-cards"),
                "Half-floor freezes only after all genuine acquisition children: the exact count and candidate entities are current Hand cards, with no equipment/judgment material or premature finish.");
        }
        public void ReturnCards(int[] ids, bool coldAfterFirst)
        {
            var root = Root.Id; Reject(new AnswerPromptCommand(0, P!.PromptId, new("program-owned-set.frame-" + root + ".finish-0"), G.Revision));
            for (var index = 0; index < ids.Length; index++)
            {
                Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards" && choice.Cards.SequenceEqual([ids[index]]));
                if (index == ids.Length - 1) continue;
                Need(Root.OwnedCardSelection!.SelectedCardIds.Count == index + 1 && Returns().Length == 0 && V(1).HandCount == 0,
                    "Choosing fewer than the frozen required half only updates the native draft and cannot move or refund any selected entity.");
                if (index == 0 && coldAfterFirst) Cold();
            }
        }
        public void AssertPindianEntity(int card, CardLocation origin) => Need(G.CardMovements.Count(move => move.CardId == card && move.From == origin && move.To == CardLocation.Processing &&
                move.Reason == (origin == CardLocation.DrawPile ? new CardMoveReason("pindian.reserve-top") : CardMoveReasons.PindianReveal)) == 1 &&
            G.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile && move.Reason == CardMoveReasons.PindianFinish) == 1,
            "The actual native Pindian entity has one real reveal payment and one cleanup, even when it was earlier a Liushi topdeck cost.");
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(seat => G.CreateSnapshot(seat).PendingDecision is null) &&
                Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(player => player.Seat != viewer).All(player => player.Hand.Count == 0)),
                "The actual source alone sees its return choices and newly owned cards; all foreign Hand identities remain absent from every other player's projection.");
            Reject(new AnswerPromptCommand(1, P!.PromptId, P.Choices[0].Id, G.Revision));
        }
        public void Until(Func<bool> stop)
        {
            for (var step = 0; step < 240; step++)
            {
                if (stop()) return;
                if (P is { PlayerSeat: 0 } prompt)
                {
                    if (prompt.Choices.Any(choice => choice.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (prompt.Kind == DecisionKind.RescueDying) { Answer(choice => choice.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    throw new InvalidOperationException("Unexpected actual Shidu boundary: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded real Shidu scene did not reach its named boundary: " + Diagnostic());
        }
        public void Cold()
        { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold native replay preserves private Pindian choices, all original entities, paid acquisition, current half-count and exact typed return without repeating a payment."); }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, G.State.TurnNumber, G.State.Winner,
            Players = G.CreateSnapshot(0).Players.Select(player => new { player.Seat, player.IsAlive, player.Hp, player.HandCount }),
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(choice => choice.Id) },
            Contest = E<PindianResultDeterminedEvent>(), Acquisition = E<ProgramPindianHandAcquiredEvent>(), Half = E<ProgramPindianHalfReturnCountEvent>(),
            Taken = Taken().Select(move => new { move.CardId, move.Sequence }), Returned = Returns().Select(move => new { move.CardId, move.Sequence }),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            ShanheStartingCard[] own = scene is "empty" or "owner-death" ? [new(CardKind.Nullification, Suit.Club, 13), new(CardKind.Nullification, Suit.Club, 3),
                new(CardKind.Nullification, Suit.Club, 4), new(CardKind.Nullification, Suit.Club, 5)] :
                [new(CardKind.Dodge, Suit.Heart, 1), new(CardKind.Nullification, Suit.Club, 3), new(CardKind.Nullification, Suit.Club, 4), new(CardKind.Nullification, Suit.Club, 5)];
            var count = scene == "empty" ? 1 : 5;
            var opponent = new ShanheCombatantDefinition(Victim) { StartingHand = Enumerable.Range(0, count).Select(index =>
                new ShanheStartingCard(CardKind.Nullification, Suit.Club, scene == "loss" ? 11 : scene == "tie" ? 3 : index + 2)).ToArray() };
            var setup = new ShanheBattleSetup(83, 1, "fixture:shidu-" + scene, Owner, 20, 0, 0, 0, 0, [], own, [],
                scene == "owner-death" ? [opponent, new(Peer) { StartingHand = [] }] :
                    [opponent, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }],
                scene == "owner-death" ? [new(Peer) { StartingHand = [] }] : [], new string('d', 64));
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-shidu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:cao-xing");
            CharacterFixtureContent.Register(builder, "classic:qin-mi");
            builder.AddSkill(Production.Value.GetSkill(Skill));
            var definitions = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string option) => new { op = "chooseOption", target = "owner", resultBind = option, options = new[] { new { id = option } } };
            void Add(string id, string property, object value, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = value });
                labels[id] = new { name = id, description = "真实拼点、整手取得后半手归还及原牌子流程", optionLabels = options.ToDictionary(option => option, option => option) };
            }
            var gainEffects = new List<object> { Pause("obtained") };
            if (scene == "paid-loss") gainEffects.Add(new { op = "loseHp", target = "owner", amount = 1 });
            if (scene == "owner-death") gainEffects.Add(new { op = "loseHp", target = "owner", amount = 20 });
            if (scene == "victim-death") gainEffects.AddRange([new { op = "selectTarget", target = "owner", targetKind = "otherLiving" }, new { op = "damage", target = "selectedTarget", amount = 20 }]);
            gainEffects.Add(new { op = "draw", target = "owner", amount = 1 }); gainEffects.Add(Pause("after-gain"));
            Add(Gain, "triggers", new[] { new { id = "actual-whole-hand-gain-child", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                movementOccurrence = "perBatch", movementReasons = new[] { TransferReason.Value }, usageScope = "game", usageLimit = 1, optional = false,
                effects = gainEffects.ToArray() } }, "obtained", "after-gain");
            Add(Watch, "triggers", new[] { new { id = "real-paid-damage", window = "afterDamageApplied", subject = "any", damageOccurrence = "perDamage", optional = false,
                effects = new[] { Pause("continue") } } }, "continue");
            object PositiveDiscard() => new { kind = "compare", left = new { kind = "turnOwnerDiscardPhaseHandDiscardCount" }, @operator = "greaterThanOrEqual", right = new { kind = "integerConstant", value = 1 } };
            Add(EndWatch, "triggers", new[] { new { id = "before-native-discard-reward", window = "discardPhaseEnded", subject = "owner", turnOwnerScope = "otherLiving", priority = 100, optional = false,
                    condition = PositiveDiscard(), effects = new[] { Pause("before") } },
                new { id = "after-native-discard-reward", window = "discardPhaseEnded", subject = "owner", turnOwnerScope = "otherLiving", priority = -100, optional = false,
                    condition = PositiveDiscard(), effects = new[] { Pause("after") } } }, "before", "after");
            Add(Quiet, "triggers", new[] { new { id = "quiet-native-ai-play", window = "afterNormalDraw", subject = "owner", optional = false,
                effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } });
            Add(Gate, "hpSkillGrants", new[] { new { id = "actual-shidu-source", skillId = Skill, minimumHp = 20 } });
            var programs = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in programs.Programs) builder.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实识度、流矢与天辩来源", "", Liushi, "qun", 20, [Zhanwan, Tianbian, Gain, Watch, EndWatch, scene == "paid-loss" ? Gate : Skill]));
            builder.AddGeneral(new(Victim, "实际拼点与还牌目标", "", Quiet, "wei", 4));
            builder.AddGeneral(new(Peer, "保留实际战斗的其他角色", "", Quiet, "wei", 20));
            builder.AddDeck(new(Deck, "无自然摸牌的确定原牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:nullification", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实识度原生交互", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Victim, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = scene == "owner-death" ? 2 : 1,
                    [ShanheBattleSetup.EnemyTeamId] = scene == "owner-death" ? 2 : 3 }));
        }
    }
}
