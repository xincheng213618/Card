using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryXunXuChecks
{
    private const string Hero = "anniversary:xun-xu", Diyin = "anniversary:diyin", Boqia = "anniversary:boqia", Peer = "fixture:xun-xu-peer", Driver = "fixture:xun-xu-driver",
        Twice = "fixture:xun-xu-twice", NoDraw = "fixture:xun-xu-no-draw", Quiet = "fixture:xun-xu-quiet", Hold = "fixture:xun-xu-hold", Deck = "fixture:xun-xu-deck";
    public static void SeventhLossThreeMaterialsAndRearmedUseResumeCold()
    {
        var s = Scene.Start(); s.Play();
        var rig = s.V(0).Hand!.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).ToArray();
        s.Use("prime", rig); s.Play();
        s.Use("damage"); s.Play();
        s.Need(s.V(0).Hp == 2 && s.V(0).HandCount == 8 && s.V(2).Hp == 2 && s.V(2).HandCount == 1 && s.V(1).HandCount == 0 &&
            s.E<AnniversaryXunXuDrawEvent>().Select(e => e.Recipient).SequenceEqual([0, 2]), "The real damage's same-HP query draws exactly once for its original owner and equal-HP peer, excluding the other two living participants.");
        foreach (var card in s.V(0).Hand!.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray()) { s.Use("give", [card], [1]); s.Play(); }
        s.Need(s.E<AnniversaryXunXuLossEvent>().Last().Occurrence == 6 && s.E<AnniversaryXunXuClaimEvent>().Length == 0,
            "The four-card top-deck move is one actual loss, and five separate true gifts bring it to six, rather than nine card-count losses.");
        var handMaterials = s.V(0).Hand!.Where(c => c.Kind is CardKind.Peach or CardKind.Dismantlement).Select(c => c.Id).ToArray();
        var equipment = s.V(0).Equipment.Single().Id; var paid = handMaterials.Append(equipment).ToArray();
        s.BoqiaUse(CardKind.DrawTwo, paid, []); s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Diyin && f.TriggerId == "next-discard");
        var receipt = frame.AnniversaryXunXu!; var first = s.E<AnniversaryXunXuUseEvent>().Single();
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Action?.ActionId == first.ActionId);
        var loss = s.E<AnniversaryXunXuLossEvent>().Single(e => e.Occurrence == 7); var claim = s.E<AnniversaryXunXuClaimEvent>().Single();
        s.Need(first is { Name: CardKind.DrawTwo, Reward: true } && use.PhysicalCardIds!.Order().SequenceEqual(paid.Order()) &&
            use.Action!.PhysicalCards.Count(c => c.From == CardLocation.Equipment(0)) == 1 && paid.Select(s.Card).Select(c => c.Suit).Distinct().Count() == 1 &&
            receipt is { Mode: "claim", Stage: "children", Gain: not null } && receipt.Cards.Order().SequenceEqual(paid.Order()) && frame.PendingMovementContinuation is not null &&
            claim.BatchId != loss.BatchId && claim.SequenceBefore >= loss.SequenceAfter && s.E<AnniversaryXunXuLossEvent>().Last().Occurrence == 7 &&
            paid.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1 && s.Zone(id) == CardLocation.Hand(0)),
            "A three-HE native DrawTwo pays once as the seventh loss, qualifies through all three printed categories despite one suit, and Diyin obtains all three real materials only from the following atomic finish batch.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Menu("target"); s.Answer(c => c.Targets.SequenceEqual([0]));
        s.Menu("discard");
        s.Need(s.P is { RequiredCardCount: 3, MinimumCardCount: 3 } && s.P.Choices.All(c =>
                c.Parameters.GetValueOrDefault("owned-selection-role") == "pick"),
            "The real Boqia receiver sees the complete three-card excess Hand draft before its first native selection.");
        var beforeDiscard = s.G.CardMovements.Count;
        s.Answer(c => c.Cards.SequenceEqual([paid[0]])); s.Menu("discard");
        s.Need(s.P is { RequiredCardCount: 2, MinimumCardCount: 2 } && s.G.CardMovements.Count == beforeDiscard &&
            s.P.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-selected") == paid[0].ToString()),
            "The first native Boqia discard material is buffered, remains highlighted and reduces the same collection without moving any card.");
        s.Cold();
        foreach (var id in paid.Skip(1)) { s.Menu("discard"); s.Answer(c => c.Cards.SequenceEqual([id])); }
        s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) == 1 && s.E<AnniversaryXunXuRearmedEvent>().Count(e => e.ActionId == first.ActionId) == 1 &&
            s.V(0).HandCount == 3 && s.V(0).Hand!.All(c => c.Kind == CardKind.Dodge) && s.V(0).Hand!.Select(c => c.Suit).Distinct().Count() == 3 &&
            !s.Actions().Any(a => a.PlayedCardKind == CardKind.DrawTwo) && s.Actions().Any(a => a.PlayedCardKind == CardKind.Peach),
            "Cold resumes the paid claim once, then the real receiver privately discards exactly three excess hand entities; the new allowance admits a new card name while DrawTwo remains spent this actual turn.");
        var rainbow = s.V(0).Hand!.Select(c => c.Id).ToArray();
        s.BoqiaUse(CardKind.Peach, rainbow, [0]); s.Menu("target"); s.Answer(c => c.Targets.SequenceEqual([3])); s.Play();
        var second = s.E<AnniversaryXunXuUseEvent>().Last();
        s.Need(second is { Name: CardKind.Peach, Reward: true } && second.ActionId != first.ActionId && s.V(0).Hp == 3 && s.V(0).HandCount == 0 && s.V(3).HandCount == 5 &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1 && s.E<AnniversaryXunXuRearmedEvent>().Length == 2,
            "Three distinct suits in the same Basic category qualify independently: the actual three-material Peach recovers once, then another chosen living participant draws only to its true MaxHp.");
        s.Use("draw"); s.ContinueHolds(); s.Play();
        s.Need(s.V(0).HandCount == 3 && s.V(0).Hand!.All(c => c.Suit == Suit.Club) && !s.Actions().Any(a => a.PlayedCardKind is CardKind.DrawTwo or CardKind.Peach), "The true actual-turn card-name ledger survives two successful rearmings.");
        s.BoqiaUse(CardKind.IronChain, s.V(0).Hand!.Select(c => c.Id).ToArray(), [0]); s.Play();
        var third = s.E<AnniversaryXunXuUseEvent>().Last();
        s.Need(third is { Name: CardKind.IronChain, Reward: false } && s.E<AnniversaryXunXuRearmedEvent>().Length == 2 && s.V(0).IsChained,
            "A legal triple with neither three suits nor three categories still truly uses IronChain, but has no hand adjustment or rearming reward.");
        s.Use("draw"); s.ContinueHolds(); s.Play();
        s.Need(s.V(0).HandCount == 3 && !s.Actions().Any(), "After an unqualified actual Use, three fresh legal materials cannot bypass the exhausted shared actual-turn allowance.");
    }
    public static void ThreeMaterialDodgeCompletedUseRearmsWithAtomicCosts()
    {
        var s = Scene.Start(defensive: true); s.Play();
        var handCosts = s.V(0).Hand!.Where(c => c.Kind is CardKind.Peach or CardKind.Dismantlement).Select(c => c.Id).ToArray();
        var equipment = s.V(0).Equipment.Single().Id; var paid = handCosts.Append(equipment).ToArray();
        s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == Boqia && c.Parameters.GetValueOrDefault("output-kind") == "Dodge" && c.Cards.Order().SequenceEqual(paid.Order()));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "response") == true);
        var issued = s.E<AnniversaryXunXuUseEvent>().Single();
        var response = s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion?.ActionId == issued.ActionId);
        var completion = response.ResponseCompletion!; var action = response.Action; var slash = completion.ParentFrameId;
        var finish = completion.CostBatches.Where(b => b.Movements.Any(m => m.Reason == CardMoveReasons.ResponseFinished)).ToArray();
        var entry = completion.CostBatches.Where(b => b.Movements.Any(m => m.Reason == CardMoveReasons.Respond && m.To == CardLocation.Processing)).ToArray();
        s.Need(issued is { Name: CardKind.Dodge, Reward: true } && action.Type == CardActionType.Response && action.ActorSeat == 0 && action.ProviderSeat == 0 &&
            action.PhysicalCards.Count == 3 && action.PhysicalCards.Count(c => c.From == CardLocation.Equipment(0)) == 1 && completion.NativeCosts.Count == 6 && completion.CostsDrained &&
            finish is [var finished] && finished.Movements.Count == 3 && finished.Movements.All(m => m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) &&
            finished.Movements.Select(m => m.CardId).Order().SequenceEqual(paid.Order()) && entry is [var entered] && entered.Movements.Count == 3 &&
            paid.All(id => s.Zone(id) == CardLocation.DiscardPile && s.G.CardMovements.Count(m => m.CardId == id && m.Reason == CardMoveReasons.ResponseFinished) == 1) &&
            s.E<AnniversaryXunXuLossEvent>() is [{ Occurrence: 1 }] && s.V(0).HandCount == 3 && s.V(0).Equipment.Count == 0 &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == slash),
            "The real native Slash accepts a genuine three-HE Dodge: one atomic response payment and one atomic finish settle all entities before the completed-response observer, counting one loss and retaining the original Slash.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("result-bind") == "response" && c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Menu("target");
        s.Need(s.G.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == issued.ActionId && w.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Dodge) &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == slash), "The same finished Dodge really enters opt-in CardUseCompleted before its native Slash returns.");
        s.Answer(c => c.Targets.SequenceEqual([0]));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.E<AnniversaryXunXuRearmedEvent>().Count(e => e.ActionId == issued.ActionId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == slash) == 1 &&
            s.V(0).HandCount == 3 && s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("conversion-skill-id") != Boqia),
            "The completed-response adjustment to the owner's already-exact MaxHp rearms once, but the next real Slash in this same actual turn cannot reuse the spent Dodge card name despite three legal materials.");
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "dodge" && c.Cards.Count == 1);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "response") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.E<AnniversaryXunXuUseEvent>().Length == 1 && s.E<AnniversaryXunXuRearmedEvent>().Length == 1 && s.V(0).Hp == 3 && s.V(0).HandCount == 2 &&
            s.E<CardUseDeclaredEvent>().Where(e => e.SourceSeat == 3 && e.CardKind == CardKind.Slash).Select(e => e.ResolutionId).ToArray() is [var firstSlash, var secondSlash] &&
            firstSlash != secondSlash && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == firstSlash || e.ResolutionId == secondSlash) == 2,
            "Cold never repays any of the three original materials, and an ordinary second Dodge neither invokes Boqia nor duplicates its completed-use reward; both independent native Slashes finish once.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-xun-xu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "固定体力角色", "", NoDraw, "wei", 5));
            b.AddDeck(new(Deck, "固定实体短牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 36).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "荀勖原生实际使用", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        private static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        { [NoDraw] = new { name = "不摸普通牌", description = "固定短局" }, [Quiet] = new { name = "跳过其他人出牌", description = "固定时点" }, [Driver] = new { name = "实际原生小操作", description = "真实置顶、给牌、伤害和摸牌" },
          [Twice] = new { name = "两张真实杀", description = "同一实际回合的原生杀额度" }, [Hold] = new { name = "三实体获得暂停", description = "代表性原移动子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
        private static string Rules => $$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
          {"id":"{{{NoDraw}}}","revision":1,"triggers":[{"id":"quiet-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{{Twice}}}","revision":1,"modifiers":[{"id":"second-real-slash","query":"slashLimit","operation":"add","value":1,"priority":0}]},
          {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
            {"id":"prime","minCards":4,"maxCards":4,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]},
            {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},
            {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
            {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}]},
          {"id":"{{{Hold}}}","revision":1,"triggers":[{"id":"hold-three","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,
            "condition":{"kind":"compare","left":{"kind":"movedCardCount"},"operator":"equal","right":{"kind":"integerConstant","value":3}},
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment","options":[{"id":"continue"}]}]},
            {"id":"hold-completed-dodge","window":"cardResponseCompleted","ownerRelation":"actor","cardKinds":["dodge"],"optional":false,"isSkillInvocation":false,
             "effects":[{"op":"chooseOption","target":"owner","resultBind":"response","options":[{"id":"continue"}]}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardSnapshot Card(int id) => Enumerable.Range(0, 4).SelectMany(i => (V(i).Hand ?? []).Concat(V(i).Equipment)).Single(c => c.Id == id);
        public CardLocation Zone(int id) => G.CardMovements.Last(m => m.CardId == id).To;
        public LegalAction[] Actions() => G.GetHumanLegalActions().Where(a => a.ProgramSkillId == Boqia).ToArray();
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void BoqiaUse(CardKind kind, int[] cards, int[] targets)
        {
            var action = Actions().FirstOrDefault(a => a.PlayedCardKind == kind && a.SelectableCardIds!.Order().SequenceEqual(cards.Order()) && a.SelectableTargetSeats!.SequenceEqual(targets));
            Need(action is not null, "The real exact three-HE output and native target choice must be legal: " + kind);
            Accept(new UseProgramSkillCommand(0, Boqia, action!.ProgramActivationId!, cards, targets, G.Revision, P!.PromptId));
        }
        public void Menu(string stage) => Reach(() => P?.SkillPrompt?.SkillId == Boqia && G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SkillId == Boqia)?.AnniversaryXunXu?.Stage == stage);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void ContinueHolds() { Reach(() => P is { PlayerSeat: 0 }); while (P?.SkillPrompt?.SkillId == Hold) { Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(() => P is { PlayerSeat: 0 }); } }
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Only the original human's exact current private choice may be answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready) { for (var i = 0; i < 160; i++) { if (ready()) return; if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Xun Xu boundary. " + Dump()); Accept(new AdvanceOneStepCommand(G.Revision)); } Need(false, "The bounded native continuation was not reached."); }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(); var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold preserves the original native action, frozen physical costs, pending child, viewer state and once-paid typed return.");
        }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Xun Xu command."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount, V(i).Equipment }),
            Loss = E<AnniversaryXunXuLossEvent>(), Claims = E<AnniversaryXunXuClaimEvent>(), Uses = E<AnniversaryXunXuUseEvent>(), Rearmed = E<AnniversaryXunXuRearmedEvent>(), Draws = E<AnniversaryXunXuDrawEvent>(), Actions = G.GetHumanLegalActions(), Frames = G.ResolutionStack });
        public static Scene Start(bool defensive = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var hand = defensive ? new ShanheStartingCard[] { new(CardKind.Peach, Suit.Spade, 2), new(CardKind.Dismantlement, Suit.Spade, 3),
                new(CardKind.Dodge, Suit.Heart, 4), new(CardKind.Dodge, Suit.Club, 5), new(CardKind.Dodge, Suit.Diamond, 6) } : Enumerable.Range(5, 5).Select(rank => new ShanheStartingCard(CardKind.Slash, Suit.Club, rank)).Concat([
                new(CardKind.Peach, Suit.Spade, 2), new(CardKind.Dismantlement, Suit.Spade, 3), new(CardKind.Dodge, Suit.Heart, 10), new(CardKind.Dodge, Suit.Spade, 11), new(CardKind.Dodge, Suit.Club, 12), new(CardKind.Dodge, Suit.Diamond, 13)]).ToArray();
            var setup = new ShanheBattleSetup(79, 1, "xun-xu-native", Hero, 3, 0, 0, 0, 0, [NoDraw, Driver, Hold], hand, [new(CardKind.Crossbow, Suit.Spade, 7)],
                [new(Peer, Hp: 2, ExtraSkillIds: [Quiet]) { StartingHand = [] }, new(Peer, Hp: 4, ExtraSkillIds: defensive ? [Twice] : [Quiet]) { StartingHand = defensive ? [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Club, 8)] : [] }],
                [new(Peer, ExtraSkillIds: [Quiet]) { StartingHand = [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
