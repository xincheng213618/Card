using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class AnniversaryShenSunQuanChecks
{
    private const string Hero = "anniversary:shen-sun-quan", Sea = "anniversary:cangming", Copy = "anniversary:chouxi", Surge = "anniversary:jichao";
    private const string Driver = "fixture:anniversary-sunquan-driver", Watch = "fixture:anniversary-sunquan-watch", Quiet = "fixture:anniversary-sunquan-quiet";
    private const string Mode = "identity:classic-anniversary-sunquan-fixture", MingReason = "skill-program.anniversary-sun-quan.ming", ColorReason = "skill-program.anniversary-sun-quan.colors";
    public static void PrivateMingNativeCopyAndRandomSurgeColdReturnsOnce()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Hero, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var setup = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Sea && f.TriggerId == "initial-sea");
        Need(setup.AnniversarySunQuan is { Movements.Count: 16, Stage: AnniversarySunQuanStage.MovementChildren } &&
            Enumerable.Range(0, 4).All(s => V(g, s).HandCount == 0 && MingCount(g, s) == 4) &&
            V(g, 1).NamedAmbushPiles!.Single(p => p.SkillId == Sea).Entries is null &&
            g.CreateSnapshot(1).Players.Single(p => p.Seat == 1).NamedAmbushPiles!.Single(p => p.SkillId == Sea).Entries!.Count == 4,
            "Initial hands really enter their own private Ming in one paid native batch; only the holder sees its exact materials.");
        Continue(g); Play(g);
        Need(MingCount(g, 0) == 0 && Enumerable.Range(1, 3).All(s => MingCount(g, s) == 4 && V(g, s).HandCount == 0) &&
            g.ResolutionStack.All(f => f is not ProgramSkillFrame { AnniversarySunQuan: not null }) &&
            E<AnniversarySunQuanStartedEvent>(g).Count(e => e.Operation == SkillProgramEffectOp.ResolveAnniversarySunQuanCangmingColors && e.Source.OwnerSeat == 0) == 1 &&
            !g.CardMovements.Any(m => m.Reason.Value == ColorReason && m.To.OwnerSeat != 0),
            "Only Sun Quan draws for the entry colors; actual own turn start returns only his Ming and closes the original paid frame. " +
            JsonSerializer.Serialize(new { Ming = Enumerable.Range(0, 4).Select(s => MingCount(g, s)).ToArray(), Hands = Enumerable.Range(0, 4).Select(s => V(g, s).HandCount).ToArray(),
                Started = E<AnniversarySunQuanStartedEvent>(g).Select(e => new { e.Source.OwnerSeat, e.Source.BindingId, e.Operation }).ToArray(),
                ColorMoves = g.CardMovements.Where(m => m.Reason.Value == ColorReason).Select(m => new { m.From, m.To }).ToArray(), Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind }).ToArray() }));
        Use(g, "draw"); Play(g);
        EquipOther(g, 1); var weapon = g.CreateCardZoneDiagnostics().Single(c => c.Location == CardLocation.Equipment(1)).CardId;
        Use(g, "give", [1]); Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        Answer(g, c => c.Cards.Count == 1); Play(g);
        var delayed = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([1]));
        Native(g, delayed); Play(g);
        var otherHand = V(g, 1).HandCount; Need(otherHand == 1, "The fixture gives a real target one actual hand card.");
        SurgeMenu(g); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "2");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var sweep = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Surge);
        var sweepId = sweep.Id;
        Need(sweep.AnniversarySunQuan is { Branch: 2, Movements.Count: 3 } &&
            MingCount(g, 1) == 7 && V(g, 1).HandCount == 0 && !g.CreateCardZoneDiagnostics().Any(c => c.Location is { OwnerSeat: 1, Zone: CardZoneKind.Equipment or CardZoneKind.Judgment }) &&
            sweep.AnniversarySunQuan.Movements.Any(m => m.CardId == weapon && m.From == CardLocation.Equipment(1) && IsMing(m.To)) &&
            sweep.AnniversarySunQuan.Movements.Any(m => m.CardId == delayed.CardId && m.From == CardLocation.Judgment(1) && IsMing(m.To)),
            "Formal Jichao II stashes actual Hand, Equipment and Judgment entities, preserving existing Ming and returning through a native color-draw child.");
        Continue(g); Play(g);
        Need(E<AnniversarySunQuanCompletedEvent>(g).Count(e => e.FrameId == sweepId) == 1 && !Unlocked(g), "The actual all-card option finishes once and locks until three later actual damage.");
        var copied = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource?.SkillId == Copy && a.TargetSeats.SequenceEqual([1]));
        Native(g, copied); Play(g);
        Need(MingCount(g, 1) == 0 && V(g, 1).HandCount == 7 &&
            E<AnniversaryChouxiNameUsedEvent>(g).Count(e => e.OwnerSeat == 0 && e.Name == CardKind.Slash) == 1 &&
            !g.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Copy && a.PlayedCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null),
            "A true copied Slash keeps the sea exemplar, returns the damaged holder's complete Ming, consumes the shared Slash name once and leaves the ordinary Slash quota unspent.");
        Use(g, "damage", [2]); Play(g); Need(!Unlocked(g), "Two real post-lock damage points do not reset the all-card option.");
        Use(g, "damage", [3]); Play(g); Need(Unlocked(g), "The third real damage point resets it without requiring a kill.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Use(g, "draw"); Play(g);
        EquipOther(g, 1);
        var beforeHand = V(g, 1).HandCount; var beforeMing = MingCount(g, 1);
        SurgeMenu(g);
        Need(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "2"), "The next real Play phase offers the reset all-card branch.");
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "1");
        Reach(g, p => p.SkillPrompt?.SkillId == Surge && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
        Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var random = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Surge);
        var randomId = random.Id; var invoice = random.AnniversarySunQuan!.Movements;
        Need(random.AnniversarySunQuan.Branch == 1 && invoice.Count(m => m.From == CardLocation.Hand(1)) == (beforeHand + 1) / 2 &&
            invoice.Count(m => m.From == CardLocation.Equipment(1)) == 1 && MingCount(g, 1) == beforeMing + (beforeHand + 1) / 2 + 1 &&
            V(g, 1).HandCount == beforeHand / 2,
            "Formal Jichao I freezes a real random ceiling-half Hand selection and every equipment card before child reactions.");
        var movementCount = g.CardMovements.Count(m => m.Reason.Value == MingReason);
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<AnniversarySunQuanCompletedEvent>(g).Count(e => e.FrameId == randomId) == 1 &&
            g.CardMovements.Count(m => m.Reason.Value == MingReason) == movementCount &&
            E<AnniversaryJichaoLockedEvent>(g).Length == 1 && V(g, 1).NamedAmbushPiles!.Single(p => p.SkillId == Sea).Entries is null &&
            g.ResolutionStack.All(f => f is not ProgramSkillFrame { AnniversarySunQuan: not null }),
            "Cold command replay retains the private random allocation and typed draw child; returning neither pays again nor re-locks option II.");
        var starts = E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 1);
        Use(g, "turn-over", [1]); Play(g); Need(V(g, 1).IsFaceDown && MingCount(g, 1) > 0, "A native turnover leaves a real Ming obligation on the face-down holder.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Need(E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 1) == starts + 1 && !V(g, 1).IsFaceDown && MingCount(g, 1) == 0 &&
            E<AnniversaryMingActualStartReturnedEvent>(g).Any(e => e.OwnerSeat == 1),
            "The actual skipped turn still returns the holder's Ming before its native face-up turnover and phase skip.");
    }
    private static bool IsMing(CardLocation l) => l.Zone == CardZoneKind.PrivatePersistentPile && l.PublicPileId?.StartsWith("anniversary-ming:", StringComparison.Ordinal) == true;
    private static int MingCount(GameEngine g, int seat) => g.CreateCardZoneDiagnostics().Count(c => c.Location.OwnerSeat == seat && IsMing(c.Location));
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(0).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual anniversary Sun Quan command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "Missing real anniversary Sun Quan choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    private static void EquipOther(GameEngine g, int target)
    {
        static bool Selection(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" && c.Cards.Count == 1;
        Use(g, "equip-other", [target]);
        Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 || p.SkillPrompt?.SkillId == Driver && p.Choices.Any(Selection));
        if (P(g)!.Kind != DecisionKind.PlayCard) Answer(g, Selection);
        Play(g);
    }
    private static void SurgeMenu(GameEngine g)
    {
        Accept(g, new UseProgramSkillCommand(0, Surge, "surge", [], [], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Surge && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-surge"));
    }
    private static bool Unlocked(GameEngine g)
    {
        var locked = E<AnniversaryJichaoLockedEvent>(g).Last();
        return (bool)typeof(GameEngine).GetMethod("AnniversaryJichaoUnlocked", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(g, [0, locked.SkillInstanceId])!;
    }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 220; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.Kind == DecisionKind.Nullification) { Answer(g, c => c.Cards.Count == 0); continue; }
                throw new InvalidOperationException("Unexpected Sun Quan boundary: " + JsonSerializer.Serialize(new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId,
                    Choices = p.Choices.Take(6).Select(c => new { c.Id, c.Parameters }).ToArray() }));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Sun Quan missed a bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(State(g) == State(restored), "Cold replay preserves all private Ming, random paid allocation, native color draw, original frames, events and commands.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-sunquan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"damage","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"turn-over","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"give","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","awaitMovementTriggers":true}]},
                {"id":"equip-other","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"initial","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{MingReason}}"],"optional":false,"priority":1000,"effects":[{"op":"chooseOption","target":"owner","resultBind":"initial","options":[{"id":"continue"}]}]},
                {"id":"colors","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{ColorReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"colors","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var labels = new Dictionary<string, object> {
                [Driver] = new { name = "真实沧溟驱动", description = "原生摸牌、给牌、装备与伤害。" },
                [Watch] = new { name = "真实入溟与颜色摸牌子窗", description = "暂停真实支付。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "安静角色", description = "跳过其他角色出牌。" } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Native anniversary Sun Quan fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:anniversary-sunquan-peer-{n}", "真实溟持有人", "supporter", Quiet, "wei", 8));
            var kinds = new[] { "standard:crossbow", "standard:indulgence", "standard:dodge", "standard:slash" };
            b.AddDeck(new("fixture:anniversary-sunquan-deck", "固定实体牌堆", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(kinds[i % kinds.Length], (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "沧溟真实实结算", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:anniversary-sunquan-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Hero, "fixture:anniversary-sunquan-peer-1", "fixture:anniversary-sunquan-peer-2", "fixture:anniversary-sunquan-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [..d.AdditionalSkillIds!, Driver, Watch] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
