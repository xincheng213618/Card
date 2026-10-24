using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryZhangHengChecks
{
    private const string Hero = "anniversary:zhang-heng", Liangjue = "anniversary:liangjue", Dangzai = "anniversary:dangzai",
        Plain = "fixture:zhang-heng-peer", NoDraw = "fixture:zhang-heng-no-draw", Quiet = "fixture:zhang-heng-quiet",
        Driver = "fixture:zhang-heng-driver", DrawHold = "fixture:zhang-heng-draw-hold", PaidHold = "fixture:zhang-heng-paid-hold";
    private const string DrawReason = "skill-program.anniversary:liangjue.Draw";

    public static void NativeJudgmentSubsetPerBlackEntityDrawThenHpAndPaidCold()
    {
        var s = Scene.Start(); s.Play();
        var hand = s.V().Hand!;
        var indulgence = hand.Single(c => c.Kind == CardKind.Indulgence).Id;
        var shortage = hand.Single(c => c.Kind == CardKind.SupplyShortage).Id;
        var blackWeapon = hand.Single(c => c.Kind == CardKind.Crossbow && c.Suit == Suit.Club).Id;
        var redWeapon = hand.Single(c => c.Kind == CardKind.Crossbow && c.Suit == Suit.Heart).Id;
        s.Native(indulgence, 1); s.Play(); s.Native(shortage, 1); s.Play();
        s.Need(s.Zone(indulgence) == CardLocation.Judgment(1) && s.Zone(shortage) == CardLocation.Judgment(1) &&
            !s.E<ProgramBindingStartedEvent>().Any(e => e.SkillId == Liangjue),
            "Two genuine native delayed uses enter the other role's Judgment, without treating Zhang Heng's Hand departures as field movements.");
        s.End(); s.Offer();
        s.Need(s.G.State.TurnNumber == 5 && s.G.State.CurrentSeat == 0 && s.V().Hp == 3,
            "Dangzai waits for Zhang Heng's true next Play-start boundary and uses current HP3 rather than his printed MaxHp8.");
        s.Activate(); s.Stage("donor"); var rootId = s.Root!.Id;
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Stage("cards");
        s.Answer(c => c.Cards.SequenceEqual([indulgence])); s.Stage("cards");
        s.Need(s.Root!.AnniversaryZhangHeng!.Cards.SequenceEqual([indulgence]) &&
            s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "finish") && s.P.Choices.Any(c => c.Cards.SequenceEqual([shortage])) &&
            s.Zone(indulgence) == CardLocation.Judgment(1) && s.Zone(shortage) == CardLocation.Judgment(1),
            "A selected proper subset can already be confirmed, while choosing more remains possible and no tentative selection fabricates a movement.");
        s.Answer(c => c.Cards.SequenceEqual([shortage])); s.Stage("cards");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); s.HoldPaid();
        var root = s.Root!; var paid = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Liangjue);
        var move = s.E<AnniversaryZhangHengMovedEvent>().Single(e => e.FrameId == rootId);
        s.Need(root.Id == rootId && root.AnniversaryZhangHeng is { Stage: "children", Cards.Count: 2 } && move.Count == 2 &&
            new[] { indulgence, shortage }.All(id => s.Zone(id) == CardLocation.Judgment(0) &&
                s.G.CardMovements.Count(m => m.CardId == id && m.Sequence > move.Before && m.Sequence <= move.After &&
                    m.From == CardLocation.Judgment(1) && m.To == CardLocation.Judgment(0) && m.Reason.Value == "skill-program.anniversary:dangzai.move") == 1) &&
            paid.WindowContext?.MovementBatch?.Movements.Count(m => m.From == CardLocation.Judgment(1) && m.To == CardLocation.Judgment(0) &&
                (m.CardId == indulgence || m.CardId == shortage)) == 2,
            "The original selected two-card subset pays one genuine Judgment-to-Judgment batch and keeps its exact Dangzai parent while Liangjue observes each entity.");
        s.Need(paid.NativeDraw is { RequestedCount: 2, RecipientApplied: true } && s.Drawn().Length == 2 && s.V().Hp == 3 &&
            !s.E<ProgramSkillHpLostEvent>().Any(e => e.SkillId == Liangjue),
            "The first physical two-card draw has paid before the HP test; the paid observer pauses with HP still3 on the original native producer.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.Resolved().Select(e => e.BindingId).SequenceEqual(["black-judgment-enters", "black-judgment-enters"]) &&
            s.Drawn().Length == 4 && s.Attempts().Length == 2 && s.Attempts().All(e => e.RequestedCount == 2 && !e.Replaced) &&
            s.E<ProgramSkillHpLostEvent>().Where(e => e.SkillId == Liangjue).Select(e => e.RemainingHp).SequenceEqual([2, 1]) &&
            s.E<AnniversaryZhangHengCompletedEvent>().Count(e => e.FrameId == rootId) == 1 &&
            s.E<AnniversaryZhangHengMovedEvent>().Count(e => e.FrameId == rootId) == 1 && s.V().Hp == 1,
            "Cold returns the paid child once, then the two black entities each draw2 before independently testing the changed HP3→2→1.");
        s.Native(blackWeapon); s.Play();
        s.Need(s.Zone(blackWeapon) == CardLocation.Equipment(0) && s.Drawn().Length == 6 && s.V().Hp == 1,
            "A real black Equipment entry still draws two at HP1, without losing the last HP.");
        s.Native(redWeapon); s.Play();
        s.Need(s.Zone(redWeapon) == CardLocation.Equipment(0) && s.Zone(blackWeapon) == CardLocation.DiscardPile && s.Drawn().Length == 8 && s.V().Hp == 1,
            "A real red replacement observes the departing black Equipment once and does not reward the red entry.");
        s.ClearJudgment(); s.Play();
        var resolved = s.Resolved();
        s.Need(resolved.Select(e => e.BindingId).SequenceEqual(["black-judgment-enters", "black-judgment-enters", "black-equipment-enters", "black-equipment-leaves", "black-judgment-leaves", "black-judgment-leaves"]) &&
            s.Attempts().Length == 6 && s.Attempts().All(e => e.RequestedCount == 2 && !e.Replaced) && s.Drawn().Length == 12 &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Liangjue) == 2 && s.V().Hp == 1 && s.V().HandCount == 12 &&
            new[] { indulgence, shortage }.All(id => s.Zone(id) == CardLocation.DiscardPile &&
                s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Judgment(0) && m.To == CardLocation.DiscardPile) == 1),
            "All four real E/J directions remain per black entity: the final two Judgment departures each draw2 at HP1, with no repeated paid transfer, native request or HP loss.");
        HistoricalFieldColorSurvivesNewHandIdentity();
    }

    private static void HistoricalFieldColorSurvivesNewHandIdentity()
    {
        var s = Scene.Start(identityBoundary: true); s.Play();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var zones = (CardZoneStore)typeof(GameEngine).GetField("_cardZones", flags)!.GetValue(s.G)!;
        var card = zones.CardsAt(CardLocation.Hand(0)).Single();
        s.Need(card is { Kind: CardKind.IronChain, Suit: Suit.Club } && s.V().Hand!.Single(c => c.Id == card.Id).Suit == Suit.None,
            "The boundary regression starts from a genuine printed black IronChain whose actual current Hand identity is colorless under the held Xunshi skill.");
        // RecordMovement captures after the zone store changes. Query only its
        // source-boundary input here; do not fabricate a historical movement.
        var captured = (AnniversaryZhangHengMovement?)typeof(GameEngine).GetMethod("CaptureAnniversaryZhangHengMovement", flags)!
            .Invoke(s.G, [card, CardLocation.Judgment(0), CardLocation.Hand(0)]);
        s.Need(captured is { SourceBlack: true, DestinationBlack: false },
            "The original Judgment boundary remains black after the entity is already colorless in Hand, so the new identity cannot erase Liangjue's source-field qualification.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryZhangHeng is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public CardMovementRecord[] Drawn() => G.CardMovements.Where(m => m.Reason.Value == DrawReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).ToArray();
        public ProgramBindingResolvedEvent[] Resolved() => E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Liangjue && e.Activated && e.Completed).ToArray();
        public NativeDrawAttemptDecidedEvent[] Attempts()
        { var ids = E<ProgramBindingStartedEvent>().Where(e => e.SkillId == Liangjue).Select(e => e.FrameId).ToHashSet(); return E<NativeDrawAttemptDecidedEvent>().Where(e => ids.Contains(e.ParentFrameId)).ToArray(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Dangzai && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Stage(string stage) => Reach(() => Root?.AnniversaryZhangHeng?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-zhang-heng") == true);
        public void HoldPaid() => Reach(() => P?.SkillPrompt?.SkillId == PaidHold);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void ClearJudgment() => Accept(new UseProgramSkillCommand(0, Driver, "clear-judgment", [], [], G.Revision, P!.PromptId));
        public void Native(int id, int? target = null)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(a is not null, "The fixed original card retains a genuine native legal use with its actual selected target.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
                { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only the actual human chooser can answer the exact current canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId is DrawHold or PaidHold) { Continue(); continue; }
                    throw new InvalidOperationException("Unexpected original Zhang Heng prompt at " + line + Dump());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded native Zhang Heng flow not reached at " + line + Dump());
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold preserves the original selected subset, paid native transfer and draw observer subtree before the dynamic HP test.");
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Zhang Heng command."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.AnniversaryZhangHeng,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }), Bindings = Resolved(), Attempts = Attempts(), Drawn = Drawn(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start(bool identityBoundary = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(identityBoundary));
            ShanheStartingCard[] cards = identityBoundary ? [new(CardKind.IronChain, Suit.Club, 5)] :
                [new(CardKind.Indulgence, Suit.Club, 5), new(CardKind.SupplyShortage, Suit.Spade, 6), new(CardKind.Crossbow, Suit.Club, 7), new(CardKind.Crossbow, Suit.Heart, 8)];
            ShanheStartingCard[] peers = [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Diamond, 3)];
            string[] skills = identityBoundary ? [NoDraw, Driver, DrawHold, PaidHold, "anniversary:xunshi"] : [NoDraw, Driver, DrawHold, PaidHold];
            var setup = new ShanheBattleSetup(41, 1, "zhang-heng-native-fields", Hero, 3, 0, 0, 0, 0, skills, cards, [],
                [new(Plain, Hp: 8) { StartingHand = peers }, new(Plain, Hp: 8) { StartingHand = peers }], [new(Plain, Hp: 8) { StartingHand = peers }], new string('d', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(bool identityBoundary) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhang-heng", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            if (identityBoundary) CharacterFixtureContent.Register(b, Hero, "anniversary:shen-zhang-fei");
            else CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"preserve-judgment","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["judgment"]}]},{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[{"id":"clear-judgment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["judgment"],"resultBind":"judgment"},{"op":"moveBoundCards","target":"owner","sourceBind":"judgment","destination":"discardPile","awaitMovementTriggers":true}]}]},
             {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"native-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held-draw","options":[{"id":"continue"}]}]}]},
             {"id":"{{PaidHold}}","revision":1,"triggers":[{"id":"paid-movement","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DrawReason}}"],"priority":1000,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held-paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { NoDraw, Quiet, Driver }) names[id] = new { name = "张横原生固定输入", description = "实际阶段与区域移动" };
            foreach (var id in new[] { DrawHold, PaidHold }) names[id] = new { name = "张横原生暂停", description = "实际请求与已付摸牌", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "固定同桌", "supporter", NoDraw, "wei", 8, AdditionalSkillIds: [Quiet]));
            const string deck = "fixture:zhang-heng-deck";
            b.AddDeck(new(deck, "固定原生闪", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "张横原生判装移动", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
