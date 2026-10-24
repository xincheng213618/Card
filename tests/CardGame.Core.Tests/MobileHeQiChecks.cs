using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;

namespace CardGame.Core.Tests;

internal static class MobileHeQiChecks
{
 private const string Hero = "mobile:he-qi", Skill = "mobile:shanxi", Activation = "red-basic-discard-and-view";
 private const string Peer = "fixture:mobile-he-qi-peer-", Quiet = "fixture:mobile-he-qi-no-draw", Hand = "fixture:mobile-he-qi-hand", Deck = "fixture:mobile-he-qi-deck", Mode = "identity:classic-mobile-he-qi";
 private static readonly Lazy<ContentRegistry> Formal = new(StandardContentRegistry.CreateWithClassicGenerals);
 public static void RealRedBasicNativeDiscardDirectionalPrivateHandAndCold()
 {
  foreach (var dodge in new[] { true, false })
  {
   var s = Scene.Start(); s.Play();
   var cost = s.V(0).Hand.Single(c => c.Kind == CardKind.Dodge).Id;
   var black = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
   var entry = s.G.GetLegalActions(0).Single(a => a.ProgramSkillId == Skill);
   s.Need(entry.SelectableCardIds.Contains(cost) && !entry.SelectableCardIds.Contains(black) && !entry.SelectableTargetSeats.Contains(2), "Held Hongyan makes the actual Spade basic card red and payable, while the black basic and initially out-of-range target remain excluded.");
   var horse = s.V(0).Hand.Single(c => c.Kind == CardKind.OffensiveHorse).Id;
   var equip = s.G.GetLegalActions(0).Single(a => a.CardId == horse && a.ConversionSource is null);
   s.Accept(new PlayCardCommand(0, horse, equip.TargetSeats, s.G.Revision, s.P!.PromptId, equip.PlayedCardKind)); s.Play();
   s.Need(s.V(0).Skills!.Any(k => k.Id == "classic:mashu") && s.G.GetLegalActions(0).Single(a => a.ProgramSkillId == Skill).SelectableTargetSeats.Contains(2), "Actual equipment issues Qizhou's first qualified skill through the shared grant layer and changes native range qualification.");
   var victim = s.V(2).Hand.Single(c => c.Kind == (dodge ? CardKind.Dodge : CardKind.Peach)).Id;
   var slot = s.V(2).Hand.ToList().FindIndex(c => c.Id == victim);
   s.Accept(new UseProgramSkillCommand(0, Skill, Activation, [cost], [2], s.G.Revision, s.P!.PromptId));
   s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
   s.Need(s.Zone(cost) == CardLocation.DiscardPile && s.Zone(victim) == CardLocation.Hand(2) && s.P!.Choices.All(c => c.Cards.Count == 0) && s.E<MobileHeQiHandViewedEvent>().Length == 0, "The original red basic is really discarded before the hidden foreign Hand selection; opaque slots reveal no foreign card IDs or premature hand view.");
   s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
   s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mobile-he-qi-view") == true);
   var view = s.E<MobileHeQiHandViewedEvent>().Single(); var viewer = dodge ? 0 : 2; var holder = dodge ? 2 : 0;
   var held = s.V(holder).Hand.Select(c => c.Id).ToHashSet();
   s.Need(view.ViewerSeat == viewer && view.HolderSeat == holder && view.DiscardCardId == victim && view.DiscardedKind == (dodge ? CardKind.Dodge : CardKind.Peach) && s.P!.PlayerSeat == viewer && s.Zone(victim) == CardLocation.DiscardPile &&
    s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(i => i.ParentFrameId == view.FrameId && i.ActualCount == 1) == 2 &&
    held.SetEquals(s.G.CreateSnapshot(viewer).PrivateRevealedCards!.Select(c => c.Id)) &&
    Enumerable.Range(0, 4).Where(i => i != viewer).All(i => s.G.CreateSnapshot(i).PrivateRevealedCards is null or { Count: 0 }), "Two actual native logical discards precede exactly the proper directional private view: Dodge lets He Qi view the victim, another card lets the victim view He Qi.");
   if (dodge) s.Cold();
   s.Answer(_ => true); s.Play();
   s.Need(s.E<MobileHeQiViewConfirmedEvent>() is [{ ViewerSeat: var confirmed }] && confirmed == viewer && !s.G.GetLegalActions(0).Any(a => a.ProgramSkillId == Skill) && new[] { cost, victim }.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile) == 1), "The actual private viewer confirms once, the original phase quota stays spent and cold recovery never repeats either material payment."); s.Replay();
  }
 }
 private sealed class Scene(GameEngine game, ContentRegistry registry)
 {
  public GameEngine G { get; private set; } = game;
  public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
  public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
  public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
  public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(x => x.CardId == id).Location;
  public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
  public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual mobile He Qi command."); }
  public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.Single(match); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, G.Revision)); }
  public void Reach(Func<bool> done)
  {
   for (var i = 0; i < 160; i++)
   {
    if (done()) return;
    if (P is { Kind: DecisionKind.SelectGeneral } setup) { Accept(new SelectGeneralCommand(setup.PlayerSeat, setup.PlayerSeat == 0 ? Hero : Peer + setup.PlayerSeat, G.Revision, setup.PromptId)); continue; }
    if (P is { Kind: DecisionKind.SelectFaction }) { Answer(_ => true); continue; }
    Need(P is null, "The short actual table reached an unexpected prompt before the intended native payment or private view."); Accept(new AdvanceOneStepCommand(G.Revision));
   }
   Need(false, "The bounded native mobile He Qi flow missed its next exact boundary.");
  }
  private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands) });
  public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold retains the original paid native discards, hidden-view direction and exact viewer confirmation on its owning frame."); }
  public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "The real command journal reproduces native payments, Qizhou qualification and directional private hand view."); }
  public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + " " + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.Phase, P, Stack = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Recent = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() })); }
  public static Scene Start()
  {
   var stock = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray();
   var probe = Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(stock)));
   var hands = new[] { new[] { (CardKind.OffensiveHorse, Suit.Spade), (CardKind.Dodge, Suit.Spade), (CardKind.Slash, Suit.Club), (CardKind.Peach, Suit.Heart) }, new[] { (CardKind.Dodge, Suit.Heart), (CardKind.Peach, Suit.Club), (CardKind.Slash, Suit.Diamond) } };
   for (var seat = 0; seat < 4; seat++) { var cards = hands[seat == 0 ? 0 : 1]; var ids = probe.V(seat).Hand.Select(c => c.Id).ToArray(); probe.Need(ids.Length == cards.Length, "The fixed native deal retains the exact sparse physical Hand count."); for (var i = 0; i < ids.Length; i++) stock[ids[i] - 1] = new(probe.registry.Cards.Values.Single(c => c.LegacyKind == cards[i].Item1).Id, cards[i].Item2, 7); }
   return Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(stock)));
  }
  private readonly ContentRegistry registry = registry;
  private static Scene Boot(ContentRegistry content)
  {
   var g = GameEngine.CreateStandard(new() { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord, ModeId = Mode, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, content);
   var s = new Scene(g, content); s.Accept(new StartGameCommand()); s.Play(); return s;
  }
 }
 private sealed class Fixture(IReadOnlyList<ContentDeckPhysicalCard> stock) : IGameContentPackage
 {
  public PackageManifest Manifest { get; } = new("fixture-mobile-he-qi", new(1, 0, 0));
  public void Register(IContentRegistryBuilder b)
  {
   foreach (var id in new[] { "classic:mashu", "boundary:yingzi", "ol:duanbing", "boundary:fenwei", "boundary:hongyan" }) b.AddSkill(Formal.Value.GetSkill(id));
   CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
   var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},{"id":"{{Hand}}0","revision":1,"modifiers":[{"id":"four-opening","query":"initialHandSize","operation":"add","value":4,"priority":0}]},{"id":"{{Hand}}1","revision":1,"modifiers":[{"id":"three-opening","query":"initialHandSize","operation":"add","value":3,"priority":0}]}]}""";
   var names = new Dictionary<string, object>(); foreach (var id in new[] { Quiet, Hand + "0", Hand + "1" }) names[id] = new { name = "固定原生输入", description = "真实原生发牌和阶段。" };
   var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
   foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
   for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer + seat, "固定同桌", "", Quiet, "wei", 8, [Hand + "1"]));
   b.AddDeck(new(Deck, "固定原生实体", 0, 2, []) { PhysicalCards = stock });
   b.AddMode(new(Mode, "移动贺齐实际身份桌", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck, GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer + 1, Peer + 2, Peer + 3]));
  }
 }
 private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
 {
  public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds ?? [], Quiet, Hand + "0", "boundary:hongyan"] } : d);
  public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d); public void AddCard(ContentCardDefinition d) => b.AddCard(d); public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d); public void AddMode(ContentModeDefinition d) => b.AddMode(d);
 }
}
