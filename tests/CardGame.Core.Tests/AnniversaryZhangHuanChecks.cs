using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryZhangHuanChecks
{
    private const string Hero = "anniversary:zhang-huan", Yiju = "anniversary:yiju", Shuguo = "anniversary:shuguo", Peer = "fixture:zhang-huan-peer",
        Driver = "fixture:zhang-huan-driver", NoDraw = "fixture:zhang-huan-no-draw", Quiet = "fixture:zhang-huan-quiet", ZeroHand = "fixture:zhang-huan-zero-hand",
        Watch = "fixture:zhang-huan-use-hold", DrawHold = "fixture:zhang-huan-draw-hold", Claim = "fixture:zhang-huan-claim", Deck = "fixture:zhang-huan-deck";

    public static void ActualDiscardLoopNativeUseYijuAndTrueTurnEndCold()
    {
        var s = Scene.Start(false); s.Play();
        var own = new[] { CardKind.DrawTwo, CardKind.OffensiveHorse, CardKind.Indulgence, CardKind.Slash, CardKind.Dodge }
            .Select(kind => s.V(0).Hand!.Single(c => c.Kind == kind).Id).ToArray();
        var drawTwo = own[0]; var horse = own[1]; var delayed = own[2]; var slash = own[3]; var ownDodge = own[4];
        s.Driver("drop", own); s.Play();
        s.Card(CardKind.Dismantlement, 1); s.Play();
        s.Need(s.V(1).HandCount == 0, "The real Dismantlement removes the fixed target's sole Dodge before the later native Slash damage assertion.");
        var normalUse = s.E<CardUseFinishedEvent>().Last(e => e.CardKind == CardKind.Dismantlement);
        s.End(); s.Offer();
        s.Need(s.G.State.Phase == TurnPhase.Finished && s.E<TurnEndedEvent>().Any(e => e.TurnNumber == s.G.State.TurnNumber && e.ActorSeat == 0) &&
            s.G.ResolutionStack.Last() is DeferredTurnEndFrame && !s.G.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(),
            "Shuguo is offered after the actual TurnEnded event and completed Ending phase, on the native DeferredTurnEnd parent.");
        s.Activate(); s.Menu(); var firstRoot = s.Root!.Id;
        s.Need(s.P!.Choices.Where(c => c.Cards.Count == 1).All(c => c.Cards[0] != ownDodge && c.Cards[0] != normalUse.CardId) &&
            s.P.Choices.Any(c => c.Cards.SequenceEqual([drawTwo])) && s.P.Choices.Any(c => c.Cards.SequenceEqual([horse])) && s.P.Choices.Any(c => c.Cards.SequenceEqual([delayed])),
            "The pool contains original discarded equipment and delayed tricks, excludes proactively unusable Dodge, and never treats UseFinished as a discard.");
        s.UseDiscard(drawTwo); s.HoldUse();
        var issued = s.E<AnniversaryZhangHuanUseIssuedEvent>().Single(e => e.FrameId == firstRoot);
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.UseId);
        s.Need(use.CardKind == CardKind.DrawTwo && use.TargetSeats.SequenceEqual([0]) && use.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, PhysicalCards: [{ CardId: var id, From: var from }] } &&
            id == drawTwo && from == CardLocation.DiscardPile && use.Action.ConversionChain.Count == 0 && !s.E<AnniversaryZhangHuanUseReturnedEvent>().Any(e => e.UseId == issued.UseId),
            "The accepted original discard entity pays a real DrawTwo Use and pauses its true native child before returning or selecting the next card.");
        s.Cold(); s.Continue(); s.Menu();
        s.Need(s.Root!.AnniversaryZhangHuan!.Used.SequenceEqual([drawTwo]) && s.G.CardMovements.Count(m => m.CardId == drawTwo && m.From == CardLocation.DiscardPile && m.To == CardLocation.Processing) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1 && s.E<AnniversaryZhangHuanUseReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1,
            "Cold restores the exact paid native use and returns it once, with no payment repetition or repeat-use choice for the same entity.");
        s.UseDiscard(horse); s.Menu();
        s.Need(s.Zone(horse) == CardLocation.Equipment(0), "A discarded equipment entity completes its native slot placement without first entering Hand.");
        s.UseDiscard(delayed, 1); s.Menu();
        s.Need(s.Zone(delayed) == CardLocation.Judgment(1), "A discarded delayed trick enters its selected real judgment area.");
        var hp = s.V(1).Hp; s.UseDiscard(slash, 1); s.Reach(() => s.E<AnniversaryZhangHuanDrawIssuedEvent>().Any(e => e.FrameId == firstRoot));
        s.Need(s.V(1).Hp == hp - 1 && s.Root!.AnniversaryZhangHuan!.Used.Count == 4 &&
            s.E<AnniversaryZhangHuanUseReturnedEvent>().Where(e => e.FrameId == firstRoot).Select(e => e.CardId).SequenceEqual([drawTwo, horse, delayed, slash]),
            "The owner's fourth consecutive original discard Use reaches native Slash damage, then the exhausted legal pool advances after exactly four complete original entity returns.");
        s.Reach(() => s.E<AnniversaryZhangHuanCompletedEvent>().Any(e => e.FrameId == firstRoot));
        var firstDraw = s.E<AnniversaryZhangHuanDrawIssuedEvent>().Single(e => e.FrameId == firstRoot);
        s.Need(firstDraw is { Discards: 6, Used: 4, Requested: 2, UniqueMaximum: false },
            "Only the five true owned discards and real dismantled card enter the historical total; the normally used Dismantlement does not inflate it.");

        s.Reach(() => s.P?.SkillPrompt?.SkillId == Yiju);
        var foreignTurn = s.G.State.TurnNumber;
        var foreignSlashUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.SourceSeat == 3 && f.CardKind == CardKind.Slash).Id;
        var hpBeforeForeignSlash = s.V(0).Hp;
        var yijuSlash = s.V(0).Hand!.First(c => c.Kind == CardKind.Slash).Id;
        s.Answer(c => c.Cards.SequenceEqual([yijuSlash]));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } || s.P?.SkillPrompt?.SkillId == Shuguo);
        if (s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) s.Answer(c => c.Cards.Count == 0);
        s.Offer();
        var yijuDiscard = s.E<NativeLogicalDiscardMaterialCompletedEvent>().Single(e => e.CardId == yijuSlash && e.To == CardLocation.DiscardPile);
        s.Need(s.V(0).Hand!.All(c => c.Kind != CardKind.Dodge) && s.V(0).Hp == hpBeforeForeignSlash - 1 &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceCardUseResolutionId == foreignSlashUse && e.SourceSeat == 3 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == foreignSlashUse) == 1 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.InvocationId == yijuDiscard.InvocationId).ExecutorSeat == 0,
            "Yiju truly discards the owner's selected material; with no native Dodge option the original foreign Slash finishes and applies its one real damage before the actual turn-end offer.");
        s.Activate(); s.Menu(); var foreignRoot = s.Root!.Id;
        s.Need(s.G.State.CurrentSeat == 3 && s.G.State.Phase == TurnPhase.Finished && s.Root.AnniversaryZhangHuan!.Turn == foreignTurn && s.P!.Choices.Any(c => c.Cards.SequenceEqual([yijuSlash])),
            "The real foreign unique-target Slash makes Yiju discard the chosen physical card, which is available at that actor's actual completed turn.");
        s.UseDiscard(yijuSlash, 3); s.Menu(); s.Cold();
        var peach = s.P!.Choices.First(c => c.Cards.Count == 1 && s.Kind(c.Cards[0]) == CardKind.Peach).Cards[0];
        var foreignDiscard = s.E<NativeLogicalDiscardMaterialCompletedEvent>().Single(e => e.CardId == peach && e.To == CardLocation.DiscardPile);
        var executor = s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.InvocationId == foreignDiscard.InvocationId).ExecutorSeat;
        s.Need(executor == 3 && s.Root!.AnniversaryZhangHuan!.Used.SequenceEqual([yijuSlash]), "The next actual original Peach is proved discarded by the other actor, while the owner's Yiju discard permits continuing.");
        s.UseDiscard(peach);
        s.Reach(() => s.E<AnniversaryZhangHuanCompletedEvent>().Any(e => e.FrameId == foreignRoot));
        var finalDraw = s.E<AnniversaryZhangHuanDrawIssuedEvent>().Single(e => e.FrameId == foreignRoot);
        s.Need(s.E<AnniversaryZhangHuanUseIssuedEvent>().Where(e => e.FrameId == foreignRoot).Select(e => e.CardId).SequenceEqual([yijuSlash, peach]) &&
            s.E<AnniversaryZhangHuanUseReturnedEvent>().Count(e => e.FrameId == foreignRoot) == 2 && s.V(0).Hp == 4 && finalDraw.Used == 2 && finalDraw.Discards > 7 && finalDraw.Requested == 5 &&
            s.E<AnniversaryZhangHuanDrawReturnedEvent>().Single(e => e.FrameId == foreignRoot).Actual == 5,
            "The first other-executor entity completes its true native recovery, then stops the loop; remaining historical discards yield exactly the current five-card cap.");
    }

    public static void TakenDiscardHistoryAndUniqueMaximum()
    {
        var s = Scene.Start(true, true); s.Play(); var ids = s.V(0).Hand!.Take(6).Select(c => c.Id).ToArray();
        s.Driver("drop", ids); s.Play();
        s.Need(ids.All(id => s.Zone(id) == CardLocation.Hand(1)) && s.E<NativeLogicalDiscardMaterialCompletedEvent>().Count(e => ids.Contains(e.CardId) && e.To == CardLocation.DiscardPile) == 6,
            "The fixture really discards six original materials and another role claims them through the native discard-received mechanism.");
        s.End(); s.Offer(); s.Activate(); s.HoldDraw(); var frame = s.Root!.Id;
        s.Need(s.Root.AnniversaryZhangHuan is { Stage: AnniversaryZhangHuanStage.Drawing, TotalDiscards: 6, RequestedDraw: 5, Used.Count: 0 } &&
            s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == frame && f.Context.RequestedCount == 5),
            "Cards already taken out of Discard remain in the actual-turn history, and an empty usable pool still issues the native capped draw.");
        s.Continue(); s.Reach(() => s.E<AnniversaryZhangHuanCompletedEvent>().Any(e => e.FrameId == frame));
        s.Need(s.E<AnniversaryZhangHuanDrawReturnedEvent>().Single(e => e.FrameId == frame).Actual == 5, "The native draw really gains five cards after the paid movement chain.");

        var unique = Scene.Start(true); unique.Play(); unique.Driver("drop", unique.V(0).Hand!.Take(6).Select(c => c.Id).ToArray()); unique.Play();
        unique.Need(unique.V(0).HandCount == 3 && Enumerable.Range(1, 3).All(i => unique.V(i).HandCount < 3), "The owner is truly the sole maximum before the actual turn completes.");
        unique.End(); unique.Offer(); unique.Activate(); unique.Reach(() => unique.E<AnniversaryZhangHuanCompletedEvent>().Length == 1);
        unique.Need(unique.E<AnniversaryZhangHuanDrawIssuedEvent>() is [{ Discards: 6, Used: 0, UniqueMaximum: true, Requested: 0 }] && unique.E<AnniversaryZhangHuanDrawReturnedEvent>().Length == 0 && unique.V(0).HandCount == 3,
            "The actual current sole maximum prevents the unused-discard draw entirely, without creating a fake zero-card native draw outcome.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-zhang-huan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{ZeroHand}}","revision":1,"modifiers":[{"id":"discard-unused-materials","query":"handLimit","operation":"set","value":0,"priority":100}]},
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold-real-inputs","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"drop","minCards":1,"maxCards":20,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"drop"},{"op":"moveBoundCards","target":"owner","sourceBind":"drop","destination":"discardPile","awaitMovementTriggers":true}]}
              ]},
              {"id":"{{Claim}}","revision":1,"triggers":[{"id":"claim-discarded-heart","window":"discardPileReceived","subject":"owner","suits":["heart"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"claimMovedCards","target":"owner"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-native-use","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["drawTwo"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native","options":[{"id":"continue"}]}]}]},
              {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"actual-native-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-draw","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { NoDraw, Quiet, ZeroHand, Driver, Claim }) names[id] = new { name = "张奂固定原生输入", description = "固定短局与真实原生暂停" };
            foreach (var id in new[] { Watch, DrawHold }) names[id] = new { name = "张奂固定原生暂停", description = "保留实际原生选择", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定同桌", "", NoDraw, "wei", 4));
            b.AddDeck(new(Deck, "固定杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "张奂实际弃置与真回合结束", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryZhangHuan is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public CardKind Kind(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).CardKind;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Shuguo && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void HoldUse() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void HoldDraw() => Reach(() => P?.SkillPrompt?.SkillId == DrawHold && Root?.AnniversaryZhangHuan?.Stage == AnniversaryZhangHuanStage.Drawing);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Menu() => Reach(() => Root?.AnniversaryZhangHuan?.Stage == AnniversaryZhangHuanStage.Choice && P?.Choices.Any(c => Key(c) == "anniversary-shuguo") == true);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Driver(string activation, IReadOnlyList<int> ids, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, AnniversaryZhangHuanChecks.Driver, activation, ids, targets ?? [], G.Revision, P!.PromptId));
        public void Card(CardKind kind, int target)
        {
            var id = V(0).Hand!.Single(c => c.Kind == kind).Id;
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.TargetSeats.SequenceEqual([target]) && a.ConversionSource is null);
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void UseDiscard(int id, int? target = null) => Answer(c => c.Cards.SequenceEqual([id]) && (target is null || c.Targets.SequenceEqual([target.Value])));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The actual human must answer a currently published canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 180; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == DrawHold) { Continue(); continue; }
                if (P is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard }) { Answer(_ => true); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Zhang Huan boundary. " + Dump());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded actual Zhang Huan flow was not reached.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold retains the real completed turn parent, private choice, native paid entity and used-once ledger.");
        }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Zhang Huan command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Receipt = Root?.AnniversaryZhangHuan,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }), Use = E<AnniversaryZhangHuanUseIssuedEvent>(), Returned = E<AnniversaryZhangHuanUseReturnedEvent>(), Draw = E<AnniversaryZhangHuanDrawIssuedEvent>() });
        private static string Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") ?? "";
        public static Scene Start(bool history, bool claim = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            IReadOnlyList<ShanheStartingCard> hand = history ? Enumerable.Range(0, 9).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, i + 1)).ToArray() :
                [new(CardKind.DrawTwo, Suit.Heart, 7), new(CardKind.OffensiveHorse, Suit.Heart, 5), new(CardKind.Indulgence, Suit.Club, 6), new(CardKind.Slash, Suit.Spade, 9), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dismantlement, Suit.Club, 3)];
            var foreignHand = new List<ShanheStartingCard> { new(CardKind.Slash, Suit.Club, 10), new(CardKind.Peach, Suit.Heart, 12) };
            foreignHand.AddRange(Enumerable.Range(0, 10).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i + 1)));
            var setup = new ShanheBattleSetup(71, 1, "zhang-huan-actual-discard", Hero, 4, 0, 0, 0, 0, [NoDraw, AnniversaryZhangHuanChecks.Driver, Watch, DrawHold], hand, [],
                [new(Peer, ExtraSkillIds: [Quiet, AnniversaryZhangHuanChecks.Driver]) { StartingHand = history ? [] : Enumerable.Range(0, 6).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i + 1)).ToArray() },
                 new(Peer, ExtraSkillIds: history ? [Quiet] : [ZeroHand]) { StartingHand = history ? [] : foreignHand }],
                [new(Peer, ExtraSkillIds: claim ? [Quiet, Claim] : [Quiet]) { StartingHand = [new(CardKind.Dodge, Suit.Club, 1)] }], new string('e', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
