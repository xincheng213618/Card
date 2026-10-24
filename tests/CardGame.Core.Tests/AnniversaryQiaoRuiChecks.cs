using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryQiaoRuiChecks
{
 private const string Hero = "anniversary:qiao-rui", Aishou = "anniversary:aishou", Saowei = "anniversary:saowei", Peer = "fixture:qiao-rui-peer-", NoDraw = "fixture:qiao-rui-no-draw", Watch = "fixture:qiao-rui-paid-draw", ExtraSlash = "fixture:qiao-rui-extra-slash", Deck = "fixture:qiao-rui-deck", Mode = "identity:classic-qiao-rui";
 private static readonly Lazy<ContentCardDefinition> Halberd = new(() => StandardContentRegistry.CreateWithClassicGenerals().GetCard("classic:fangtian-halberd"));
 public static void ActualDrawMarksPreparationDiscardAndMaximumHpCold()
 {
  var s = Scene.Start(false); var maximum = s.V(0).MaxHp; var hp = s.V(0).Hp;
  s.DrawAi(); s.WatchDraw();
  var marks = s.E<AnniversaryQiaoRuiMarkedEvent>(); var draw = s.E<AnniversaryQiaoRuiDrawReturnedEvent>().Single();
  s.Need(draw.Requested == maximum && draw.Actual == maximum && marks.Length == maximum && marks.All(m => s.Zone(m.CardId) == CardLocation.Hand(0) && s.G.CardMovements.Any(move => move.Sequence == m.EntrySequence && move.CardId == m.CardId && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0))) && s.Root?.AnniversaryQiaoRui?.Stage == AnniversaryQiaoRuiStage.DrawChildren, "Aishou marks only the actual entities returned by its paid native maximum-HP draw, before the original movement observer completes.");
  s.Cold(); s.Continue(); s.Play(0);
  var discard = s.E<AnniversaryQiaoRuiDiscardedEvent>().Single();
  s.Need(discard.Actual == maximum && discard.Actual > hp && s.V(0).MaxHp == maximum + 1 && s.V(0).Hp == hp && marks.All(m => s.Zone(m.CardId) == CardLocation.DiscardPile) && s.E<NativeLogicalDiscardMaterialCompletedEvent>().Count(m => marks.Any(mark => mark.CardId == m.CardId) && m.From == CardLocation.Hand(0)) == maximum && s.E<AnniversaryQiaoRuiMaximumHpEvent>() is [{ LossBatchId: 0, Before: var before, After: var after }] && before == maximum && after == maximum + 1, "The next genuine preparation discards every actual marked Hand entity through completed logical discard, then grows maximum HP once without treating own-turn loss as the outside-turn penalty.");
  s.Replay();
 }
 public static void LastMarkedMaterialWholeMultiTargetSlashDamageClaimAndDodgeCold()
 {
  foreach (var dodgeFollow in new[] { false, true })
  {
   var s = Scene.Start(true); s.NativeKind(0, CardKind.FangtianHalberd); s.Play(0); s.DrawAi(); s.WatchDraw(); s.Continue(); s.Play(1); s.NativeKind(1, CardKind.FangtianHalberd); s.Play(1);
   var marks = s.E<AnniversaryQiaoRuiMarkedEvent>(); var markedDodge = marks.Single(m => m.PrintedKind == CardKind.Dodge).CardId; var material = marks.Single(m => m.CardId != markedDodge).CardId;
   s.NativeKind(1, CardKind.Slash, [0]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); s.Answer(c => c.Cards.SequenceEqual([markedDodge])); s.Play(1);
   s.Need(s.V(0).MaxHp == 2 && s.Zone(markedDodge) == CardLocation.DiscardPile && s.E<AnniversaryQiaoRuiMaximumHpEvent>().Length == 0, "Actually playing one of two Ai as Dodge does not lose the final marked entity or decrease maximum HP.");
   var original = s.V(1).Hand.Single(c => c.Kind == CardKind.Slash).Id; s.Native(1, original, [2, 3]); s.Offer(Saowei); s.Activate(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-qiao-rui") == true);
   s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([material]) && c.Targets.SequenceEqual([2, 3])) && s.P.Choices.Where(c => c.Parameters.GetValueOrDefault("verb") == "use").All(c => c.Targets.Count > 0 && c.Targets.All(t => t is 2 or 3)), "The original completed two-target Slash remains whole; Saowei offers its original eligible target groups using Qiao Rui's own real Fangtian permission.");
   s.Answer(c => c.Cards.SequenceEqual([material]) && c.Targets.SequenceEqual([2, 3])); s.Reach(() => s.P is { Kind: DecisionKind.RespondDodge, PlayerSeat: 2 });
   var issued = s.E<AnniversaryQiaoRuiUseIssuedEvent>().Single().Origin;
   s.Need(s.Zone(material) == CardLocation.Processing && s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 && s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId).Action!.PhysicalCards.Select(c => c.CardId).SequenceEqual([material]) && s.E<AnniversaryQiaoRuiDamageEvent>().Length == 0, "Before the first actual Dodge, the exact last Ai has really paid the original native Slash once and no damage or claim has been manufactured.");
   if (!dodgeFollow) s.Cold(); s.DodgeFollow = dodgeFollow; s.Play(1);
   s.Need(s.V(0).MaxHp == 1 && s.V(0).Hp == 1 && s.E<AnniversaryQiaoRuiMaximumHpEvent>() is [{ LossBatchId: > 0, Before: 2, After: 1 }] && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardId == material) == 1 && s.E<AnniversaryQiaoRuiUseReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1 && s.E<AnniversaryQiaoRuiMarkedEvent>().Length == 2, "The one true final marked outside-turn payment reduces maximum HP once, clamps HP natively, and the original multi-target Slash returns once without issuing a replacement mark.");
   if (dodgeFollow) s.Need(s.E<AnniversaryQiaoRuiDamageEvent>().Length == 0 && s.E<AnniversaryQiaoRuiClaimedEvent>().Length == 0 && s.Zone(material) == CardLocation.DiscardPile && s.V(2).Hp == 11 && s.V(3).Hp == 11, "Both real Dodge responses prevent Saowei damage and therefore leave its original material in the discard pile without a claim.");
   else s.Need(s.E<AnniversaryQiaoRuiDamageEvent>().Select(e => e.Target).Order().SequenceEqual([2, 3]) && s.E<AnniversaryQiaoRuiClaimedEvent>() is [{ CardId: var claimed, UseId: var use }] && claimed == material && use == issued.UseId && s.Zone(material) == CardLocation.Hand(0) && s.V(2).Hp == 10 && s.V(3).Hp == 10, "Both original legal targets take actual native Slash damage, then the same physical material is obtained once after the whole use without becoming Ai again.");
   s.Replay();
  }
 }
 private sealed class Scene(GameEngine game, ContentRegistry content)
 {
  public GameEngine G { get; private set; } = game;
  public bool DodgeFollow { get; set; }
  public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
  public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryQiaoRui is not null);
  public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
  public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
  public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
  public void Play(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat);
  public void End(int seat) => Accept(new EndPlayPhaseCommand(seat, G.Revision, P!.PromptId));
  public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
  public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
  public void DrawAi() { End(0); Offer(Aishou); Activate(); }
  public void WatchDraw() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
  public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
  public void NativeKind(int actor, CardKind kind, IReadOnlyList<int>? targets = null) => Native(actor, V(actor).Hand.First(c => c.Kind == kind).Id, targets);
  public void Native(int actor, int id, IReadOnlyList<int>? targets = null)
  { var a = G.GetLegalActions(actor).First(a => a.CardId == id && a.ConversionSource is null && (targets is null || a.TargetSeats.SequenceEqual(targets))); Accept(new PlayCardCommand(actor, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
  public void Answer(Func<PromptChoice, bool> match)
  { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(c is not null, "The actual player receives the exact intended native choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
  public void Reach(Func<bool> ready)
  {
   for (var step = 0; step < 400; step++)
   {
    if (ready()) return;
    if (P is { } p)
    {
     if (p.Kind == DecisionKind.PlayCard) { End(p.PlayerSeat); continue; }
     if (p.Kind == DecisionKind.DiscardCards) { Accept(new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision)); continue; }
     if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
     if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
     { Answer(c => DodgeFollow && p.Kind == DecisionKind.RespondDodge && G.ResolutionStack.OfType<CardUseFrame>().Any(u => u.AnniversaryQiaoRuiUse is not null) ? c.Cards.Count == 1 : c.Cards.Count == 0); continue; }
     if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
     if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") != "activate"); continue; }
     if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "decline")) { Answer(c => c.Parameters.GetValueOrDefault("verb") == "decline"); continue; }
     Need(false, "Unexpected Qiao Rui native boundary.");
    }
    Need(G.State.Winner == Winner.None, "The short native table retains all participants."); Accept(new AdvanceOneStepCommand(G.Revision));
   }
   Need(false, "The bounded native scene missed its requested continuation.");
  }
  private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
  public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), content); Need(before == State(), "Cold retains the original marked draw or already paid native multi-target Slash and all exact returns."); }
  public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, content); Need(before == State(), "The command journal reproduces the exact draw marks, logical discard, native targets, damage, HP and original entity claim."); }
  public void Need(bool okay, string message) { if (!okay) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryQiaoRui, Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() })); }
  private void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Qiao Rui command."); }
  public static Scene Start(bool sweep)
  {
   var stock = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray(); var hands = Hands(sweep); var probe = Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(sweep, stock)));
   for (var seat = 0; seat < 4; seat++) { var ids = probe.V(seat).Hand.Select(c => c.Id).ToArray(); probe.Need(ids.Length == hands[seat].Length, "The fixed native deal retains its sparse hand sizes."); for (var i = 0; i < ids.Length; i++) stock[ids[i] - 1] = Physical(probe.content, hands[seat][i].Kind, hands[seat][i].Suit); }
   probe.DrawAi(); probe.Reach(() => probe.E<AnniversaryQiaoRuiMarkedEvent>().Length == probe.V(0).MaxHp);
   var drawn = probe.E<AnniversaryQiaoRuiMarkedEvent>().OrderBy(e => e.EntrySequence).ToArray();
   for (var i = 0; i < drawn.Length; i++) stock[drawn[i].CardId - 1] = Physical(probe.content, sweep && i == 0 ? CardKind.Dodge : CardKind.Peach, sweep && i == 0 ? Suit.Club : Suit.Heart);
   return Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(sweep, stock)));
  }
  private readonly ContentRegistry content = content;
  private static ContentDeckPhysicalCard Physical(ContentRegistry r, CardKind kind, Suit suit) => new(r.Cards.Values.First(c => c.LegacyKind == kind).Id, suit, 7);
  private static Scene Boot(ContentRegistry r)
  {
   var g = GameEngine.CreateStandard(new GameOptions { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r); var s = new Scene(g, r); s.Accept(new StartGameCommand());
   for (var n = 0; n < 30; n++) { if (s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return s; if (s.P is { Kind: DecisionKind.SelectGeneral } p) { s.Accept(new SelectGeneralCommand(p.PlayerSeat, p.PlayerSeat == 0 ? Hero : Peer + p.PlayerSeat, g.Revision, p.PromptId)); continue; } if (s.P is { Kind: DecisionKind.SelectFaction }) { s.Answer(_ => true); continue; } s.Need(s.P is null, "The identity opening retains its native setup and deal."); s.Accept(new AdvanceOneStepCommand(g.Revision)); }
   throw new InvalidOperationException("The fixed native identity opening missed its first Play phase.");
  }
 }
 private static ShanheStartingCard[][] Hands(bool sweep) => sweep ? [[new(CardKind.FangtianHalberd, Suit.Spade, 7)], [new(CardKind.FangtianHalberd, Suit.Spade, 7), new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Club, 7)], [new(CardKind.Dodge, Suit.Club, 7)], [new(CardKind.Dodge, Suit.Club, 7)]] : [[new(CardKind.Peach, Suit.Heart, 7)], [new(CardKind.Peach, Suit.Heart, 7)], [new(CardKind.Peach, Suit.Heart, 7)], [new(CardKind.Peach, Suit.Heart, 7)]];
 private static string HandSkill(int seat) => "fixture:qiao-rui-hand-" + seat;
 private sealed class Fixture(bool sweep, IReadOnlyList<ContentDeckPhysicalCard> stock) : IGameContentPackage
 {
  public PackageManifest Manifest { get; } = new("fixture-anniversary-qiao-rui", new(1, 0, 0));
  public void Register(IContentRegistryBuilder b)
  {
   if (sweep) b.AddCard(Halberd.Value);
   CharacterFixtureContent.Register(new OwnerBuilder(b, sweep), Hero); var rules = JsonNode.Parse($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
    {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
    {"id":"{{ExtraSlash}}","revision":1,"modifiers":[{"id":"two-actual-slashes","query":"slashLimit","operation":"add","value":1,"priority":0}]},
    {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-marked-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.anniversary:aishou.draw"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hold","options":[{"id":"continue"}]}]}]}
   ]}
   """)!;
   var names = new Dictionary<string, object> { [NoDraw] = new { name = "固定真实手牌", description = "正常摸牌调整为零。" }, [ExtraSlash] = new { name = "两次真实杀", description = "保留原生杀次数支付。" }, [Watch] = new { name = "已付隘守摸牌暂停", description = "在原帧的实际移动子窗暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
   for (var seat = 0; seat < 4; seat++) { rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{HandSkill(seat)}}","revision":1,"modifiers":[{"id":"sparse-opening","query":"initialHandSize","operation":"add","value":{{Hands(sweep)[seat].Length}},"priority":0}]}""")); names[HandSkill(seat)] = new { name = "固定原生发牌", description = "使用固定种子与原生实体发牌。" }; }
   var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names })); foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
   for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer + seat, "固定真实同桌", "", NoDraw, "wei", 12, [HandSkill(seat), ExtraSlash]));
   b.AddDeck(new(Deck, "固定原生实体库存", 0, 2, []) { PhysicalCards = stock }); b.AddMode(new(Mode, "桥蕤实际标记与原生扫围", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck, GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer + 1, Peer + 2, Peer + 3]));
  }
 }
 private sealed class OwnerBuilder(IContentRegistryBuilder b, bool sweep) : IContentRegistryBuilder
 {
  public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { BaseHp = sweep ? 1 : d.BaseHp, InitialHp = sweep ? 1 : 2, AdditionalSkillIds = [.. d.AdditionalSkillIds ?? [], NoDraw, Watch, HandSkill(0)] } : d);
  public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d); public void AddCard(ContentCardDefinition d) => b.AddCard(d); public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d); public void AddMode(ContentModeDefinition d) => b.AddMode(d);
 }
}
