using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversarySunHuanChecks
{
    private const string Hero = "anniversary:sun-huan", Niji = "anniversary:niji", Peer = "fixture:sun-huan-peer", Driver = "fixture:sun-huan-driver",
        NoDraw = "fixture:sun-huan-no-draw", Quiet = "fixture:sun-huan-quiet", Shield = "fixture:sun-huan-shield", DrawHold = "fixture:sun-huan-draw-hold", UseHold = "fixture:sun-huan-use-hold", Deck = "fixture:sun-huan-deck";

    public static void TrueDrawEntitiesEndingNativeUseAndDiscardResumeCold()
    {
        var s = Scene.Start(); s.Play();
        var primed = s.V(0).Hand!.First(c => c.Kind == CardKind.DrawTwo).Id;
        s.Accept(new UseProgramSkillCommand(0, Driver, "prime", [primed], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Card(CardKind.Slash, 3); s.Play();
        s.Card(CardKind.DrawTwo); s.Offer(); s.Activate(); s.HoldDraw();
        var drawing = s.Root!; var original = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardKind == CardKind.DrawTwo);
        s.Need(drawing.AnniversarySunHuan is { Stage: AnniversarySunHuanStage.Drawing } && s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == drawing.Id && f.Context.RequestedCount == 1) &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == original.Id), "Niji really pauses the original self-targeted trick for its own native draw, before that trick's two normal draws.");
        s.Cold(); s.Skip(); s.HoldUse(); s.Continue(); s.Play();
        var first = s.E<AnniversarySunHuanDrawReturnedEvent>().Single(); var firstId = s.DrawIds(first).Single();
        s.Need(firstId == primed && s.Zone(firstId) == CardLocation.Hand(0) && s.E<CardsDrawnEvent>().Count(e => e.PlayerSeat == 0 && e.Count == 2) == 1,
            "The returned Niji invoice contains the actual top DrawTwo entity; the trick's separate two draws do not acquire that provenance, even after cold restore.");
        s.Card(CardKind.Alcohol); s.Offer(); s.Activate(); s.HoldDraw(); s.Skip(); s.Play();
        var second = s.E<AnniversarySunHuanDrawReturnedEvent>().Last(); var secondId = s.DrawIds(second).Single();
        s.Need(first.FrameId != second.FrameId && first.Turn == second.Turn && s.E<AnniversarySunHuanDrawReturnedEvent>().Length == 2,
            "A second actual Basic self target grants another independent draw in the same actual turn.");

        s.End(); s.Menu();
        var ending = s.Root!; var endingId = ending.Id;
        s.Need(s.P!.Choices.Where(c => Key(c) == "anniversary-niji" && c.Cards.Count == 1).SelectMany(c => c.Cards).Distinct().Order().SequenceEqual(new[] { firstId, secondId }.Order()) &&
            s.P.Choices.Any(c => c.Cards.SequenceEqual([firstId])), "The ending can use only actual Niji-drawn Hand entities, while normal draws remain excluded.");
        s.Answer(c => Key(c) == "anniversary-niji" && c.Cards.SequenceEqual([firstId])); s.Offer(); s.Activate(); s.HoldDraw(); s.Skip(); s.HoldUse();
        ending = s.Root!; var pending = ending.AnniversarySunHuan!; var native = pending.Native!;
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == native.UseId);
        s.Need(pending is { Stage: AnniversarySunHuanStage.NativeUse, Returned: false } && use.CardKind == CardKind.DrawTwo && use.TargetSeats.SequenceEqual([0]) &&
            use.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, PhysicalCards: [{ CardId: var paid, From: var from }] } && paid == firstId && from == CardLocation.Hand(0) &&
            !s.E<AnniversarySunHuanDiscardedEvent>().Any(e => e.FrameId == endingId), "The chosen Hand entity pays a genuine native DrawTwo Use, including its new Niji target/draw, before any ending discard.");
        var third = s.E<AnniversarySunHuanDrawReturnedEvent>().Last(); var thirdId = s.DrawIds(third).Single();
        s.Cold(); s.Continue(); s.Reach(() => s.E<AnniversarySunHuanDiscardedEvent>().Any(e => e.FrameId == endingId));
        var discard = s.E<AnniversarySunHuanDiscardedEvent>().Single(e => e.FrameId == endingId);
        s.Need(s.Zone(firstId) == CardLocation.DiscardPile && s.Zone(secondId) == CardLocation.DiscardPile && s.Zone(thirdId) == CardLocation.DiscardPile && discard.Count == 2 && s.V(0).HandCount == 5 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == endingId && e.Reason.Value == "skill.anniversary.niji.discard" && e.ActualCount == 2 && e.HandCount == 2) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == native.UseId && e.CardId == firstId) == 1 && s.E<AnniversarySunHuanUseReturnedEvent>().Count(e => e.FrameId == endingId) == 1,
            "After the entire native use returns, one real discard removes both still-held Niji Slashes, including the one newly drawn within this ending; four ordinary Slashes and the original Dodge remain.");
        var history = s.G.Events.Select(e => e.Payload).ToArray();
        s.Need(Array.FindIndex(history, e => e is CardUseFinishedEvent finished && finished.ResolutionId == native.UseId) < Array.FindIndex(history, e => e is AnniversarySunHuanDiscardedEvent discarded && discarded.FrameId == endingId),
            "The native use must finish before Niji's ending discard, and cold may not repay either cost.");

        s.Offer(); s.Activate(); s.HoldDraw(); s.Skip(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var foreignTurn = s.G.State.TurnNumber; var fourth = s.E<AnniversarySunHuanDrawReturnedEvent>().Last(); var fourthId = s.DrawIds(fourth).Single();
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "dodge" && c.Cards.Count == 1); s.Menu();
        s.Need(s.G.State.CurrentSeat == 3 && s.Root!.AnniversarySunHuan!.Turn == foreignTurn && s.P!.Choices.Any(c => c.Cards.SequenceEqual([fourthId]) && c.Targets.SequenceEqual([3])) &&
            s.E<AnniversarySunHuanDrawReturnedEvent>().Length == 4, "The foreign actor's ending offers the genuine newly drawn Slash; pure Dodge play grants no extra Niji target draw.");
        var foreignEnding = s.Root!.Id;
        s.Answer(c => c.Cards.SequenceEqual([fourthId]) && c.Targets.SequenceEqual([3])); s.Play();
        var foreignUse = s.E<AnniversarySunHuanUseIssuedEvent>().Single(e => e.FrameId == foreignEnding);
        s.Need(s.E<DamageAppliedEvent>().Count(e => e.SourceCardUseResolutionId == foreignUse.UseId && e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 1) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == foreignUse.UseId && e.CardId == fourthId) == 1 && s.Zone(fourthId) == CardLocation.DiscardPile &&
            s.E<AnniversarySunHuanUseReturnedEvent>().Length == 2 && s.E<AnniversarySunHuanDiscardedEvent>().Single(e => e.FrameId == foreignEnding).Count == 0 && s.G.State.TurnNumber > foreignTurn,
            "An off-turn exact Hand Slash resolves through native response/damage/cleanup and returns once, without charging the foreign actor's Play quota or carrying old draw debt into the next actual turn.");
    }
    private static string Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") ?? "";
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-sun-huan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var c = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定原生角色", "", NoDraw, "wei", 6));
            b.AddDeck(new(Deck, "固定实际杀", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "孙桓真实摸牌与逐结束阶段", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        private static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        { [NoDraw] = new { name = "不补普通摸牌", description = "固定短局" }, [Quiet] = new { name = "其他角色跳过出牌", description = "固定短局" }, [Shield] = new { name = "固定原生目标", description = "原生杀仅能选择实际合法目标" },
          [Driver] = new { name = "固定真实顶牌", description = "原手牌实体置顶" }, [DrawHold] = new { name = "摸牌子窗", description = "原生摸牌前暂停" },
          [UseHold] = new { name = "使用子窗", description = "实际无中生有结算前暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
        private static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"quiet-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Shield}}","revision":1,"cardPolicies":[{"id":"only-human-remains-target","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash"]}]},
          {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-real-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[{"id":"prime","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]}]},
          {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"native-draw","window":"nativeDrawStarting","subject":"owner","optional":true,"isSkillInvocation":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
          {"id":"{{UseHold}}","revision":1,"triggers":[{"id":"native-use","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["drawTwo"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-use","options":[{"id":"continue"}]}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversarySunHuan is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public int[] DrawIds(AnniversarySunHuanDrawReturnedEvent e) => G.CardMovements.Where(m => m.Sequence > e.Before && m.Sequence <= e.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(e.Owner) && m.Reason.Value == "skill.anniversary.niji.draw").Select(m => m.CardId).ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Niji && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Skip() => Answer(c => Key(c) == "skip");
        public void HoldDraw() => Reach(() => P?.SkillPrompt?.SkillId == DrawHold);
        public void HoldUse() => Reach(() => P?.SkillPrompt?.SkillId == UseHold);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Menu() => Reach(() => Root?.AnniversarySunHuan?.Stage == AnniversarySunHuanStage.Choice && P?.Choices.Any(c => Key(c) == "anniversary-niji") == true);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Card(CardKind kind, int? target = null)
        {
            var ids = V(0).Hand!.Where(c => c.Kind == kind).Select(c => c.Id).ToArray();
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && ids.Contains(id) && a.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill) && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(a is not null, "The exact physical native card must have a currently legal action.");
            Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Only the actual human answers this exact current choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        { for (var i = 0; i < 160; i++) { if (ready()) return; if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == DrawHold) { Skip(); continue; } if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Sun Huan boundary. " + Dump()); Accept(new AdvanceOneStepCommand(G.Revision)); } Need(false, "Sun Huan missed a bounded native continuation."); }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(); var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold retains the original private draw entities, accepted ending, once-paid native Use and typed child return.");
        }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Sun Huan command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversarySunHuan, Hand = V(0).Hand, Draw = E<AnniversarySunHuanDrawReturnedEvent>(), Use = E<AnniversarySunHuanUseIssuedEvent>(), Returned = E<AnniversarySunHuanUseReturnedEvent>(), Discard = E<AnniversarySunHuanDiscardedEvent>() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(71, 1, "sun-huan-native-ending", Hero, 4, 0, 0, 0, 0, [NoDraw, Driver, DrawHold, UseHold],
                [new(CardKind.DrawTwo, Suit.Heart, 7), new(CardKind.DrawTwo, Suit.Heart, 8), new(CardKind.Slash, Suit.Club, 9), new(CardKind.Alcohol, Suit.Spade, 3), new(CardKind.Dodge, Suit.Heart, 2)], [],
                [new(Peer, ExtraSkillIds: [Quiet, Shield]) { StartingHand = [] }, new(Peer) { StartingHand = [new(CardKind.Slash, Suit.Club, 10)] }],
                [new(Peer, ExtraSkillIds: [Quiet, Shield]) { StartingHand = [] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
