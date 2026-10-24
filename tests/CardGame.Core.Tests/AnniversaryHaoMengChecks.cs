using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryHaoMengChecks
{
 private const string Hero = "anniversary:hao-meng", Skill = "anniversary:xiongmang", Peer = "fixture:hao-meng-peer-", Quiet = "fixture:hao-meng-no-draw", Deck = "fixture:hao-meng-deck", Mode = "identity:classic-hao-meng";
 private static readonly Lazy<ContentRegistry> FormalCards = new(StandardContentRegistry.CreateWithClassicGenerals);
 public static void DistinctHandWholeMultiTargetNativeDamagePhaseQuotaAndPaidCold()
 {
  var rewritten = Scene.Start(false, hongyan: true); var spade = rewritten.V(0).Hand.Single(c => c.Kind == CardKind.FireSlash).Id; var heart = rewritten.V(0).Hand.Single(c => c.Kind == CardKind.ThunderSlash).Id;
  rewritten.Begin(); rewritten.Menu(AnniversaryHaoMengStage.Cards); rewritten.Answer(c => c.Cards.SequenceEqual([spade])); rewritten.Menu(AnniversaryHaoMengStage.Cards);
  rewritten.Need(rewritten.E<AnniversaryHaoMengCardChosenEvent>() is [{ Suit: Suit.Heart }] && !rewritten.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "card" && c.Cards.Contains(heart)) && rewritten.P.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "card"), "A genuinely held Hongyan rewrites the Spade material to Heart: the actual Xiongmang entry excludes the printed Heart as a duplicate effective suit while retaining other suits."); rewritten.Answer(c => c.Parameters.GetValueOrDefault("verb") == "cancel"); rewritten.Play(0);
  var s = Scene.Start(false); var materials = s.V(0).Hand.Where(c => c.Kind is CardKind.FireSlash or CardKind.ThunderSlash).Select(c => c.Id).ToArray();
  s.Begin(); s.Menu(AnniversaryHaoMengStage.Cards); s.Answer(c => c.Cards.SequenceEqual([materials[0]])); s.Menu(AnniversaryHaoMengStage.Cards);
  var suit = s.V(0).Hand.Single(c => c.Id == materials[0]).Suit;
  s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("verb") == "card").All(c => s.V(0).Hand.Single(card => card.Id == c.Cards.Single()).Suit != suit), "The actual selection excludes every second Hand material of the already selected effective suit, including printed Slash variants.");
  s.Answer(c => c.Cards.SequenceEqual([materials[1]])); s.Menu(AnniversaryHaoMengStage.Cards); s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "targets"); s.Menu(AnniversaryHaoMengStage.Targets); s.Answer(c => c.Targets.SequenceEqual([1])); s.Menu(AnniversaryHaoMengStage.Targets); s.Answer(c => c.Targets.SequenceEqual([3])); s.Menu(AnniversaryHaoMengStage.Targets);
  s.Need(!s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "target") && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "use" && c.Cards.SequenceEqual(materials) && c.Targets.SequenceEqual([1, 3])), "Two distinct suits authorize precisely two selected original legal targets, with the complete original materials retained.");
  s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "use"); s.Reach(() => s.P is { Kind: DecisionKind.RespondDodge, PlayerSeat: 1 });
  var issued = s.E<AnniversaryHaoMengUseIssuedEvent>().Single().Origin;
  var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId);
  s.Need(issued.MaterialCount == 2 && native.CardKind == CardKind.Slash && native.TargetSeats.SequenceEqual([1, 3]) && native.Action!.PhysicalCards.Select(c => c.CardKind).Order().SequenceEqual(new[] { CardKind.FireSlash, CardKind.ThunderSlash }.Order()) && materials.All(id => s.Zone(id) == CardLocation.Processing && s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1) && s.E<AnniversaryHaoMengConsequenceEvent>().Length == 0, "Both real Slash-family Hand entities pay once into the original ordinary native Slash before the first actual Dodge, with no premature damage consequence.");
  s.Cold(); s.Play(0);
  var result = s.E<AnniversaryHaoMengConsequenceEvent>().Single(); var lease = s.E<AnniversaryHaoMengPhaseQuotaEvent>().Single();
  s.Need(result.Damaged && result.UseId == issued.UseId && result.MaximumBefore == result.MaximumAfter && lease.UseId == issued.UseId && lease.Owner == 0 && lease.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && lease.Phase.ActorSeat == 0 && s.V(1).Hp == 11 && s.V(3).Hp == 11 && materials.All(id => s.Zone(id) == CardLocation.DiscardPile) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1 && s.E<AnniversaryHaoMengUseReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1, "The entire original two-target native Slash causes real damage to both targets and grants only one extra Slash for that actual phase, then returns and cleans up all materials once.");
  s.NativeKind(0, CardKind.Slash, [1]); s.Play(0); s.Need(!s.G.GetLegalActions(0).Any(a => a.Kind == LegalActionKind.Slash || a.ProgramSkillId == Skill) && s.E<AnniversaryHaoMengPhaseQuotaEvent>().Length == 1, "Exactly the earned second ordinary Slash is payable; a normal physical Slash does not issue another Xiongmang quota.");
  s.End(0); s.Play(0); s.NativeKind(0, CardKind.Slash, [1]); s.Play(0);
  s.Need(s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && e.Phase.ActorSeat == 0).Phase != lease.Phase && !s.G.GetLegalActions(0).Any(a => a.Kind == LegalActionKind.Slash || a.ProgramSkillId == Skill), "Leaving the actual phase expires its earned quota: after one ordinary Slash in the next genuine Play phase, the remaining real Hand Slash cannot spend the previous phase's allowance."); s.Replay();
 }
 public static void UndamagedMaximumHpAndNativeRequestedUseVersusPurePlayed()
 {
  var s = Scene.Start(true); s.NativeKind(0, CardKind.FangtianHalberd); s.Play(0); var maximum = s.V(0).MaxHp; var hp = s.V(0).Hp; var first = s.V(0).Hand.Single(c => c.Kind == CardKind.Peach && c.Suit == Suit.Club).Id;
  s.Use([first], [1]); s.Dodge = true; s.Play(0); s.Dodge = false;
  s.Need(s.E<AnniversaryHaoMengConsequenceEvent>() is [{ Damaged: false, MaximumBefore: var before, MaximumAfter: var after }] && before == maximum && after == maximum - 1 && s.V(0).MaxHp == maximum - 1 && s.V(0).Hp == hp && s.E<AnniversaryHaoMengPhaseQuotaEvent>().Length == 0 && s.Zone(first) == CardLocation.DiscardPile && !s.G.GetLegalActions(0).Any(a => a.ProgramSkillId == Skill), "A genuinely dodged whole Xiongmang Slash loses one native maximum HP, preserves legal current HP and provides no phase quota.");
  s.End(0); s.Play(1); var peach = s.V(0).Hand.Single(c => c.Kind == CardKind.Peach && c.Suit == Suit.Spade).Id; var pureSlash = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id;
  s.NativeKind(1, CardKind.Duel, [0]); s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0, IncomingCard: CardKind.Duel });
  s.Need(s.P!.Choices.All(c => !c.Cards.Contains(peach)) && !s.P.ValidCardIds.Contains(peach), "A Duel's pure Played Slash response cannot gain permission to play a Peach through Xiongmang's use-only rule."); s.Answer(c => c.Cards.SequenceEqual([pureSlash])); s.Play(1);
  s.Need(s.E<AnniversaryHaoMengUseIssuedEvent>().Length == 1 && s.E<AnniversaryHaoMengConsequenceEvent>().Length == 1, "Actually playing the natural printed Slash in the Duel is not a Xiongmang Use and produces no conversion consequence.");
  s.NativeKind(1, CardKind.BorrowedSword, [0, 3]); s.Reach(() => s.P is { PlayerSeat: 0, IncomingCard: CardKind.BorrowedSword }); s.Answer(c => c.Cards.SequenceEqual([peach])); s.Play(1);
  var forced = s.E<AnniversaryHaoMengUseIssuedEvent>().Last().Origin; var quota = s.E<AnniversaryHaoMengPhaseQuotaEvent>().Single();
  s.Need(forced.ProducerFrameId == 0 && forced.MaterialCount == 1 && forced.Source.OwnerSeat == 0 && s.E<AnniversaryHaoMengConsequenceEvent>().Count(e => e.UseId == forced.UseId && e.Damaged) == 1 && s.V(3).Hp == 11 && s.Zone(peach) == CardLocation.DiscardPile && quota.UseId == forced.UseId && quota.Owner == 0 && quota.Phase.ActorSeat == 1, "The native Borrowed Sword request really permits the original Hand Peach as a true Xiongmang Slash Use, deals native damage and binds its one earned quota to the foreign actual phase.");
  s.End(1); s.Play(0); s.NativeKind(0, CardKind.Slash, [1]); s.Play(0);
  s.Need(s.E<AnniversaryHaoMengUseIssuedEvent>().Length == 2 && !s.G.GetLegalActions(0).Any(a => a.ProgramSkillId == Skill || a.ConversionSource?.SkillId == Skill), "The foreign phase's true-use reward does not leak into Hao Meng's next own Play phase after its ordinary Slash is spent."); s.Replay();
 }
 private sealed class Scene(GameEngine game, ContentRegistry content)
 {
  public GameEngine G { get; private set; } = game; public bool Dodge { get; set; }
  public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
  public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f => f.AnniversaryHaoMeng?.Operation == SkillProgramEffectOp.ResolveAnniversaryHaoMengXiongmangUse);
  public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
  public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
  public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
  public void Play(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat);
  public void End(int seat) => Accept(new EndPlayPhaseCommand(seat, G.Revision, P!.PromptId));
  public void Begin() => Accept(new UseProgramSkillCommand(0, Skill, "distinct-hand-native-slash", [], [], G.Revision, P!.PromptId));
  public void Menu(AnniversaryHaoMengStage stage) => Reach(() => Root?.AnniversaryHaoMeng?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-hao-meng") == true);
  public void Use(int[] cards, int[] targets)
  { Begin(); foreach (var id in cards) { Menu(AnniversaryHaoMengStage.Cards); Answer(c => c.Cards.SequenceEqual([id])); } Menu(AnniversaryHaoMengStage.Cards); Answer(c => c.Parameters.GetValueOrDefault("verb") == "targets"); foreach (var target in targets) { Menu(AnniversaryHaoMengStage.Targets); Answer(c => c.Targets.SequenceEqual([target])); } Menu(AnniversaryHaoMengStage.Targets); Answer(c => c.Parameters.GetValueOrDefault("verb") == "use"); }
  public void NativeKind(int actor, CardKind kind, int[]? targets = null)
  { var id = V(actor).Hand.First(c => c.Kind == kind).Id; var a = G.GetLegalActions(actor).First(a => a.CardId == id && a.ConversionSource is null && (targets is null || a.TargetSeats.SequenceEqual(targets))); Accept(new PlayCardCommand(actor, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
  public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(c is not null, "The real native actor receives the intended exact card, target or response choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
  public void Reach(Func<bool> ready)
  {
   for (var n = 0; n < 400; n++)
   {
    if (ready()) return;
    if (P is { } p)
    {
     if (p.Kind == DecisionKind.PlayCard) { End(p.PlayerSeat); continue; }
     if (p.Kind == DecisionKind.DiscardCards) { Accept(new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision)); continue; }
     if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
     if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => Dodge && p.Kind == DecisionKind.RespondDodge ? c.Cards.Count == 1 : c.Cards.Count == 0); continue; }
     if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") != "activate"); continue; }
     Need(false, "Unexpected Hao Meng native boundary.");
    }
    Need(G.State.Winner == Winner.None, "The short native table retains its living actors."); Accept(new AdvanceOneStepCommand(G.Revision));
   }
   Need(false, "The bounded native scene missed its requested original continuation.");
  }
  private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
  public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), content); Need(before == State(), "Cold preserves all already paid distinct-suit materials, the exact native target group and its original parent return."); }
  public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, content); Need(before == State(), "The actual command journal reproduces native materials, target effects, maximum HP and the exact actual-phase quota."); }
  public void Need(bool okay, string why) { if (!okay) throw new InvalidOperationException(why + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryHaoMeng, Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() })); }
  private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Hao Meng input."); }
  public static Scene Start(bool requested, bool hongyan = false)
  {
   var stock = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray(); var hands = Hands(requested); var probe = Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(requested, stock, hongyan)));
   for (var seat = 0; seat < 4; seat++) { var ids = probe.V(seat).Hand.Select(c => c.Id).ToArray(); probe.Need(ids.Length == hands[seat].Length, "The fixed native opening retains its sparse real Hand counts."); for (var i = 0; i < ids.Length; i++) stock[ids[i] - 1] = new(probe.content.Cards.Values.First(c => c.LegacyKind == hands[seat][i].Kind).Id, hands[seat][i].Suit, 7); }
   return Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(requested, stock, hongyan)));
  }
  private readonly ContentRegistry content = content;
  private static Scene Boot(ContentRegistry r)
  {
   var g = GameEngine.CreateStandard(new GameOptions { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r); var s = new Scene(g, r); s.Accept(new StartGameCommand());
   for (var n = 0; n < 30; n++) { if (s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return s; if (s.P is { Kind: DecisionKind.SelectGeneral } p) { s.Accept(new SelectGeneralCommand(p.PlayerSeat, p.PlayerSeat == 0 ? Hero : Peer + p.PlayerSeat, g.Revision, p.PromptId)); continue; } if (s.P is { Kind: DecisionKind.SelectFaction }) { s.Answer(_ => true); continue; } s.Need(s.P is null, "The fixed identity opening retains native setup and deal boundaries."); s.Accept(new AdvanceOneStepCommand(g.Revision)); }
   throw new InvalidOperationException("The native identity opening missed its first Play phase.");
  }
 }
 private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Club) => new(kind, suit, 7);
 private static ShanheStartingCard[][] Hands(bool requested) => requested ? [[C(CardKind.FangtianHalberd, Suit.Spade), C(CardKind.Peach), C(CardKind.Slash, Suit.Heart), C(CardKind.Peach, Suit.Spade), C(CardKind.Peach, Suit.Diamond), C(CardKind.Slash, Suit.Diamond)], [C(CardKind.Dodge), C(CardKind.Duel, Suit.Heart), C(CardKind.BorrowedSword, Suit.Heart)], [C(CardKind.Peach, Suit.Heart)], [C(CardKind.Peach, Suit.Heart)]] : [[C(CardKind.FireSlash, Suit.Spade), C(CardKind.ThunderSlash, Suit.Heart), C(CardKind.Slash), C(CardKind.Slash, Suit.Diamond), C(CardKind.Slash, Suit.Spade)], [C(CardKind.Dodge)], [C(CardKind.Peach, Suit.Heart)], [C(CardKind.Peach, Suit.Heart)]];
 private static string Hand(int seat) => "fixture:hao-meng-hand-" + seat;
 private sealed class Fixture(bool requested, IReadOnlyList<ContentDeckPhysicalCard> stock, bool hongyan) : IGameContentPackage
 {
  public PackageManifest Manifest { get; } = new("fixture-anniversary-hao-meng", new(1, 0, 0));
  public void Register(IContentRegistryBuilder b)
  {
   if (requested) { b.AddCard(FormalCards.Value.GetCard("classic:fangtian-halberd")); b.AddCard(FormalCards.Value.GetCard("classic:borrowed-sword")); }
   if (hongyan) b.AddSkill(FormalCards.Value.GetSkill("boundary:hongyan"));
   CharacterFixtureContent.Register(new OwnerBuilder(b, hongyan), Hero); var rules = JsonNode.Parse($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}]}""")!; var names = new Dictionary<string, object> { [Quiet] = new { name = "固定真实手牌", description = "正常摸牌调整为零。" } };
   for (var seat = 0; seat < 4; seat++) { rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Hand(seat)}}","revision":1,"modifiers":[{"id":"native-sparse-hand","query":"initialHandSize","operation":"add","value":{{Hands(requested)[seat].Length}},"priority":0}]}""")); names[Hand(seat)] = new { name = "固定实际发牌", description = "保留固定种子和原生实体发牌。" }; }
   var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names })); foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
   for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer + seat, "固定真实同桌", "", Quiet, "wei", 12, [Hand(seat)])); b.AddDeck(new(Deck, "固定实际实体牌堆", 0, 2, []) { PhysicalCards = stock }); b.AddMode(new(Mode, "郝萌完整真实杀", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck, GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer + 1, Peer + 2, Peer + 3]));
  }
 }
 private sealed class OwnerBuilder(IContentRegistryBuilder b, bool hongyan) : IContentRegistryBuilder
 {
  public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { InitialHp = 3, AdditionalSkillIds = [.. d.AdditionalSkillIds ?? [], Quiet, Hand(0), .. (hongyan ? new[] { "boundary:hongyan" } : Array.Empty<string>())] } : d);
  public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d); public void AddCard(ContentCardDefinition d) => b.AddCard(d); public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d); public void AddMode(ContentModeDefinition d) => b.AddMode(d);
 }
}
