using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenMaChaoChecks
{
    private const string Owner = "fixture:shen-ma-chao-owner", Driver = "fixture:shen-ma-chao-driver", Quiet = "fixture:shen-ma-chao-quiet", Probe = "fixture:shen-ma-chao-probe";
    public static (GameEngine Game, ContentRegistry Registry) CreateForeignMountUiScenario()
    { var scene = new Scene(false); scene.Play(); return (scene.G, scene.Registry); }
    public static void StartingMountsForeignSlashDebtAndHengwuCold()
    {
        var s = new Scene(false);
        s.Reach(() => s.Root?.ShenMaChao is { Stage: ShenMaChaoStage.EquipmentUse } && s.P?.Kind == DecisionKind.ProgramTrigger);
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        Need(s.E<ShouliStartingMountIssuedEvent>().Select(e => e.RecipientSeat).SequenceEqual([1, 2, 3, 0]) &&
            s.E<ShouliStartingMountFinishedEvent>().Count() == 4 && Enumerable.Range(0, 4).All(i => s.V(i).Equipment.Count == 1),
            "The original game-start frame uses four real deck mounts, beginning with the next seat and finishing at its owner after cold return.");
        s.EmptyHand();
        var action = s.G.GetHumanLegalActions().First(a => a.ConversionSource is { SkillId: "anniversary:shouli" } &&
            a.CardId is { } id && s.V(1).Equipment.Any(c => c.Id == id) && a.TargetSeat == 1);
        var cardId = action.CardId!.Value; var hp = s.V(1).Hp;
        Need(s.HandLimit(1) == hp + 3, "The original holder has the real enabled nonlocked hand-limit modifier before borrowing.");
        s.PlayCard(action); s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary:hengwu"); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Play();
        var borrowed = s.E<ShouliBorrowedMountEvent>().Single(); var draw = s.E<HengwuDrawIssuedEvent>().Last();
        Need(borrowed.CardId == cardId && borrowed.From == CardLocation.Equipment(1) && borrowed.Source.OwnerSeat == 0 &&
            s.G.CardMovements.Any(m => m.CardId == cardId && m.From == CardLocation.Equipment(1) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) &&
            s.G.CardMovements.Any(m => m.CardId == cardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile),
            "Foreign equipment is a real native conversion cost, retaining the original owner without entering unrelated hand payments.");
        Need(s.V(1).Hp == hp - 2 && s.HandLimit(1) == s.V(1).Hp &&
            s.E<DamageAppliedEvent>().Last(e => e.TargetSeat == 1) is { Amount: 2, Nature: DamageNature.Thunder } &&
            s.E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Any(e => e.Suppression.TargetSeat == 1) &&
            draw.Count == 3 && s.E<HengwuDrawReturnedEvent>().Last() is { Requested: 3, Actual: 3 },
            "The borrowed mount suppresses the original holder's real nonlocked modifier and adds one thunder damage; Hengwu draws exactly the remaining three matching field equipments.");
        var ownerHp = s.V(0).Hp; s.Use("incoming", [1]); s.Play();
        Need(s.V(0).Hp == ownerHp - 2 && s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 2, Nature: DamageNature.Thunder },
            "The same actual-turn debt also affects Shen Ma Chao's incoming noncard damage.");
        s.End(); s.Play(); var later = s.V(1).Hp; s.Use("poke", [1]); s.Play();
        Need(s.V(1).Hp == later - 1 && s.E<DamageAppliedEvent>().Last(e => e.TargetSeat == 1) is { Amount: 1, Nature: DamageNature.Normal } &&
            s.E<CurrentTurnNonLockedSkillSuppressionsExpiredEvent>().Any() && s.HandLimit(1) == s.V(1).Hp + 3,
            "Both issued effects expire at the original actual turn boundary, restoring the actual nonlocked hand-limit modifier.");
        s.Replay();
        var rejected = false;
        try { SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:bad-field-mount","revision":1,"viewAs":[{"id":"bad","inputKinds":["peach"],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"fieldMountConversion":true,"outputKind":"slash","forPlay":true,"forResponse":true}]}]}""",
            JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { ["fixture:bad-field-mount"] = new { name = "非法材料", description = "拒绝手牌和非坐骑。" } } })); }
        catch (InvalidOperationException) { rejected = true; }
        Need(rejected, "The new field-mount metadata rejects a hand/Peach source instead of widening general material ownership.");
    }
    public static void OwnMountDodgeTrueResponseAndZeroHengwuCold()
    {
        var s = new Scene(true); s.Play(); s.EmptyHand(); var ownerHp = s.V(0).Hp;
        s.Use("poke", [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Cold();
        var own = s.V(0).Equipment.Single().Id;
        s.Answer(c => c.Cards.SequenceEqual([own]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == "anniversary:shouli");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary:hengwu"); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        Need(s.V(0).Hp == ownerHp && s.V(0).Equipment.Count == 0 && !s.E<ShouliBorrowedMountEvent>().Any() &&
            s.G.CardMovements.Any(m => m.CardId == own && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) &&
            !s.E<HengwuDrawIssuedEvent>().Any(),
            "The owner's own defensive mount is genuinely played as Dodge, without a debt; declining Hengwu does not ask again in another window.");
        s.Use("incoming", [1]); s.Play();
        Need(s.V(0).Hp == ownerHp - 1 && s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 1, Nature: DamageNature.Normal },
            "Spending one's own mount creates neither self suppression nor an incoming thunder/+1 penalty.");
        foreach (var seat in Enumerable.Range(1, 3)) { s.Use("strip", [seat]); s.Play(); }
        s.EmptyHand(); s.Use("one", []); s.Play();
        var trick = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo && a.ConversionSource?.BindingId == "zero-field-trick");
        var before = s.E<HengwuDrawReturnedEvent>().Count(); s.PlayCard(trick);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == "anniversary:hengwu"); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Play();
        Need(s.E<HengwuDrawIssuedEvent>().Last() is { Count: 0 } && s.E<HengwuDrawReturnedEvent>().Count() == before,
            "Hengwu may resolve at X=0 when an actual converted trick is used with no equipment in the field; it never invents a minimum draw.");
        s.Replay();
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Scene
    {
        public GameEngine G; private readonly ContentRegistry _registry;
        public ContentRegistry Registry => _registry;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShenMaChao is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public int HandLimit(int seat)
        {
            var players = (System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!;
            return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [players[seat]])!;
        }
        public IEnumerable<T> E<T>() where T : IGameEvent => G.Events.Select(e => e.Payload).OfType<T>();
        public Scene(bool defense)
        {
            _registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(defense));
            G = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "identity:classic-shen-ma-chao-fixture", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, _registry);
            Accept(new StartGameCommand()); Reach(() => P is { Kind: DecisionKind.SelectGeneral });
            Accept(new SelectGeneralCommand(0, Owner, G.Revision, P!.PromptId));
        }
        private void Accept(GameCommand c) { var result = G.Submit(c); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        public void Answer(Func<PromptChoice, bool> choose) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, G.Revision)); }
        public void Use(string id, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void EmptyHand() { if (V(0).HandCount == 0) return; Use("empty", []); Play(); }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void PlayCard(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 320; step++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId == "anniversary:hengwu") throw new InvalidOperationException("Hengwu unexpectedly offered another optional trigger for the same actual action.");
                if (P is { Kind: DecisionKind.ProgramTrigger } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "select-god-faction")) Answer(c => c.Parameters.GetValueOrDefault("faction-id") == "wei");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Shen Ma Chao boundary was not reached: " + JsonSerializer.Serialize(new { Prompt = P, Frames = G.ResolutionStack }));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Moves = G.CardMovements });
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), _registry); Need(state == State(), "Cold restore must preserve the original setup/use parent and native accepted costs."); }
        public void Replay() { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, _registry); Need(state == State(), "Accepted-command replay preserves Shouli and Hengwu's genuine entity movements."); }
    }
    private sealed class Fixture(bool defense) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-ma-chao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var json = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"setup-pause","window":"cardUseCommitted","ownerRelation":"actor","cardCategories":["equipment"],"optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]},{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},{"id":"{{Probe}}","revision":1,"modifiers":[{"id":"real-hand-limit","query":"handLimit","operation":"add","value":3,"priority":0},{"id":"counter-distance","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"triggers":[{"id":"real-counter-slash","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},{"id":"{{Driver}}","revision":1,"viewAs":[{"id":"zero-field-trick","inputKinds":["defensiveHorse"],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"activations":[{"id":"empty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},{"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},{"id":"strip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"owner","zones":["equipment"],"amount":1}]},{"id":"one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}""";
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Quiet] = new { name = "安静同桌", description = "开局原生装备使用可暂停；正常摸牌后跳过出牌。" }, [Probe] = new { name = "真实失效与回击", description = "增加手牌上限，首次受伤后用原生虚拟杀回击。" }, [Driver] = new { name = "真实检查驱动", description = "使用原生伤害、弃牌与摸牌。" } } });
            var catalog = SkillProgramCatalog.Load(json, presentation);
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id], Tags = id == Quiet ? SkillTag.Locked : SkillTag.None });
            b.AddGeneral(new(Owner, "神马超夹具", "supporter", "anniversary:shouli", "god", 4, ["anniversary:hengwu", Driver], GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:shen-ma-chao-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "安静同桌", "supporter", Quiet, "wei", 20, [Probe]));
            b.AddDeck(new("fixture:shen-ma-chao-deck", "真实坐骑实体牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard(defense ? "standard:defensive_horse" : "standard:offensive_horse", Suit.Spade, 5)).ToArray() });
            b.AddMode(new("identity:classic-shen-ma-chao-fixture", "神马超原生检查", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:shen-ma-chao-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
