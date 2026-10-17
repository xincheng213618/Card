using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TangTangChecks
{
    private const string Owner = "fixture:tt-owner", Mode = "identity:classic-tang-tang", Driver = "fixture:tt-driver", Quiet = "fixture:tt-quiet", Watch = "fixture:tt-watch", Health = "fixture:tt-health", Conditional = "fixture:tt-conditional", Insert = "fixture:tt-insert", Claim = "fixture:tt-claim", Zexing = "ol:zexing", Zhiyi = "ol:zhiyi";
    private static string Scene = "";
    private static bool RejectZhiyi;

    public static void ZexingPrivateTakeGiftAndWholeNativeChildrenCold()
    {
        foreach (var scene in new[] { "take", "equip-show" })
        {
            var (g, r) = Start(scene); Stock(g);
            var shown = V(g, 0).Hand.First(c => c.Suit == Suit.Heart).Id;
            var pair = V(g, 0).Hand.Where(c => c.Suit == Suit.Spade).Take(2).Select(c => c.Id).ToArray(); Need(pair.Length == 2, "The fixed actual deck supplies the exact two intended foreign cards.");
            Use(g, "give-two", pair, [1]); Play(g);
            if (scene == "equip-show") { Native(g, a => a.Kind == LegalActionKind.Equip && a.CardId == shown); Play(g); }
            BeginZexing(g, shown, 1); Reach(g, () => Stage(g, TangTangStage.TakeCards));
            var peerHand = V(g, 1).Hand.Select(c => c.Id).ToArray(); var current = Own(g)!.TangTang!;
            Need(P(g)!.PlayerSeat == 0 && current.Viewed.SequenceEqual(peerHand) && peerHand.All(id => g.CreateSnapshot(0).PrivateRevealedCards!.Any(c => c.Id == id)) && Enumerable.Range(1, 3).All(s => !(g.CreateSnapshot(s).PrivateRevealedCards ?? []).Any(c => peerHand.Contains(c.Id))), "Only the issuer sees the actual foreign Hand during the real private take menu.");
            Need(Enumerable.Range(0, 4).All(s => (g.CreateSnapshot(s).PublicRevealedCards ?? []).Any(c => c.Id == shown)), "The exact shown Hand or Equipment entity is publicly revealed before the peer's decision.");
            var before = State(g); var prompt = P(g)!; Need(!g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && before == State(g), "A foreign viewer cannot answer the issuer's private selection or mutate the prepared view.");
            Frozen(current.Candidates); g = Cold(g, r); Pick(g, pair[0]); g = Cold(g, r); Pick(g, pair[1]); Branch(g, "take-pay"); Reach(g, () => At(g, Watch));
            var f = Own(g)!; Need(f.TangTang!.Stage == TangTangStage.TakeChildren && f.TangTang.Materials.Select(c => c.CardId).ToHashSet().SetEquals(pair) && !E<TangTangEntityMovedEvent>(g).Any(e => e.Stage == TangTangStage.GiftChildren), "Both original cards really arrive together before any original shown-card gift.");
            Frozen(f.TangTang.Materials); g = Cold(g, r); Continue(g); Play(g);
            var paid = E<TangTangEntityMovedEvent>(g).Where(e => e.FrameId == f.Id).ToArray();
            Need(paid.Count(e => e.Stage == TangTangStage.TakeChildren) == 2 && paid.Single(e => e.Stage == TangTangStage.GiftChildren) is { CardId: var original, To: var destination } && original == shown && destination == CardLocation.Hand(1) && pair.All(id => V(g, 0).Hand.Any(c => c.Id == id)) && V(g, 1).Hand.Any(c => c.Id == shown), "The peer obtains precisely the original shown entity after all real take children, with one take2 and one gift.");
            Need(E<ZexingShownEvent>(g).Single().From.Zone == (scene == "equip-show" ? CardZoneKind.Equipment : CardZoneKind.Hand) && E<TangTangCompletedEvent>(g).Count(e => e.FrameId == f.Id) == 1 && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zexing), "Native self Equipment and Hand shows share the correct once-per-actual-Play activation without double completion."); _ = Cold(g, r);
        }
    }
    public static void ZexingHpLossRepeatedPeersAndActualPlayScopesCold()
    {
        var (g, r) = Start("scopes"); var turn = g.State.TurnNumber; var hp1 = V(g, 1).Hp; var hp2 = V(g, 2).Hp;
        var shown = V(g, 0).Hand[0].Id; BeginZexing(g, shown, 1); Reach(g, () => Stage(g, TangTangStage.RepeatPeer));
        Need(V(g, 1).Hp == hp1 - 1 && P(g)!.Choices.All(c => !c.Targets.Contains(1)), "A genuine peer-only loss1 excludes the same peer from the ongoing actual-turn chain."); g = Cold(g, r); Target(g, 2); Reach(g, () => Stage(g, TangTangStage.RepeatPeer)); Branch(g, "decline"); Play(g);
        Need(V(g, 2).Hp == hp2 - 1 && E<ZexingShownEvent>(g).Length == 1 && E<ZexingHpLossIssuedEvent>(g).Length == 2 && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zexing), "Repeated loss branches retain one shown card and the first actual Play's one activation.");
        End(g); Play(g); Need(g.State.TurnNumber == turn && g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zexing), "The genuine inserted Play ends into a different normal Play in the same actual turn.");
        Use(g, "shown-card-chain", skill: Zexing); Branch(g, "show", card: V(g, 0).Hand[0].Id);
        Need(P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.SequenceEqual([3])), "A second actual Play gets a fresh activation but cannot revisit either earlier peer."); Target(g, 3); Reach(g, () => Stage(g, TangTangStage.RepeatPeer)); g = Cold(g, r); Branch(g, "decline"); Play(g);
        Need(E<TangTangStartedEvent>(g).Where(e => e.Op == SkillProgramEffectOp.ResolveZexingShownCardChain).Select(e => e.PlayToken).Distinct().Count() == 2 && E<ZexingPeerChosenEvent>(g).Select(e => e.Peer).Order().SequenceEqual([1, 2, 3]), "Actual Play tokens remain independent while the issuer's turn visitor set is exact and shared.");
        End(g); Reach(g, () => g.State.CurrentSeat == 0 && g.State.TurnNumber > turn && P(g)?.Kind == DecisionKind.PlayCard); Use(g, "shown-card-chain", skill: Zexing); Branch(g, "show", card: V(g, 0).Hand[0].Id);
        Need(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1])), "A true later actual turn releases the old visitor set rather than permanently banning the peer."); _ = Cold(g, r);

        (g, r) = Start("dying"); Use(g, "hurt-eleven", targets: [1]); Play(g); Need(V(g, 1).Hp == 1, "Real native HP loss sets up one remaining HP without direct state injection.");
        shown = V(g, 0).Hand[0].Id; BeginZexing(g, shown, 1); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); g = Cold(g, r);
        Answer(g, c => c.Cards.Count == 1 && !c.Cards.Contains(shown)); Reach(g, () => Stage(g, TangTangStage.RepeatPeer)); Branch(g, "decline"); Play(g);
        Need(V(g, 1).IsAlive && V(g, 1).Hp == 1 && E<ZexingHpLossIssuedEvent>(g).Length == 1 && E<ProgramSkillHpLostEvent>(g).Count(e => e.SkillId == Zexing && e.TargetSeat == 1) == 1 && E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 1 && e.Amount == 1), "The real HP1→dying→native Peach recovery returns to the original repetition choice once."); _ = Cold(g, r);
    }
    public static void ZhiyiWholeGainDirectionsColorsAndNativeTransferCold()
    {
        foreach (var scene in new[] { "red-empty", "red-old" })
        {
            var (g, r) = Start(scene); var keep = scene == "red-old" ? new[] { V(g, 0).Hand[0].Id } : [];
            Trim(g, keep); var before = E<NativeDrawInvocationRecordedEvent>(g).Length; RejectZhiyi = scene == "red-empty";
            Use(g, "draw-two");
            if (scene == "red-empty")
            { Play(g); RejectZhiyi = false; Need(V(g, 0).HandCount == 2 && E<ZhiyiOpportunityPaidEvent>(g).Length == 0 && E<NativeDrawInvocationRecordedEvent>(g).Skip(before).Any(e => e.OwnerSeat == 0 && e.ActualCount == 2), "Two true red draws in one native invocation cannot witness one another or create a hidden intermediate offer."); }
            else
            {
                ActivateZhiyi(g); Need(Own(g)!.TangTang!.Gains.Count == 2 && Own(g)!.TangTang!.Gains.Select(e => e.NativeDrawId).Distinct().Count() == 1 && Own(g)!.TangTang!.Gains.All(e => e.NativeDrawId > 0 && e.OtherHandCardId == keep[0]), "An old red card witnesses the entire real Draw2; both actual gains bind one native invocation, not just its first entity.");
                g = Cold(g, r); Branch(g, "benefit", target: 0); Play(g); Need(V(g, 0).HandCount == 4 && E<ZhiyiOpportunityPaidEvent>(g).Length == 1 && g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.tang-tang.zhiyi.draw") == 1, "The selected actual draw1 and its children complete once, without recursively paying a second direction.");
                Use(g, "give-one", [V(g, 0).Hand[0].Id], [1]); RejectZhiyi = true; Play(g); RejectZhiyi = false; Need(E<ZhiyiOpportunityPaidEvent>(g).Length == 1, "Foreign obtain shares the already spent actual-turn opportunity with the own-gain branch.");
            }
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("mixed"); Stock(g); var red = V(g, 0).Hand.First(c => c.Suit == Suit.Heart).Id; var club = V(g, 0).Hand.First(c => c.Suit == Suit.Club).Id;
            Use(g, "give-two", [red, club], [1]); Play(g); var shown = V(g, 0).Hand.First(c => c.Suit == Suit.Spade).Id; BeginZexing(g, shown, 1); Reach(g, () => Stage(g, TangTangStage.TakeCards)); Pick(g, red); Pick(g, club); Branch(g, "take-pay"); Reach(g, () => At(g, Watch)); Continue(g); ActivateZhiyi(g);
            var gain = Own(g)!.TangTang!; Need(gain.Gains.Count == 2 && gain.Gains.Select(x => x.Suit).ToHashSet().SetEquals([Suit.Heart, Suit.Club]), "Every color in a genuine atomic take2 is checked against other Hand cards; the whole batch offers only one shared benefit.");
            g = Cold(g, r); Branch(g, "benefit", target: 2); Play(g); Need(E<ZhiyiOpportunityPaidEvent>(g).Length == 1 && E<TangTangEntityMovedEvent>(g).Count(e => e.Stage == TangTangStage.GiftChildren) == 1, "The optional benefit's own movement children return before the original shown gift without duplicating the mixed batch."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("snatch"); Native(g, a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.SequenceEqual([1])); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectTargetCard });
            var sourceHand = V(g, 1).Hand.Select(c => c.Id).ToArray(); var stolen = sourceHand[0];
            Need(P(g)!.Choices.Count == sourceHand.Length && P(g)!.Choices.All(c => c.Cards.Count == 0), "A native Snatch keeps foreign Hand slots opaque before a real card is selected."); Answer(g, c => c.Parameters.GetValueOrDefault("action") == "target-card-slot" && c.Parameters.GetValueOrDefault("slot-index") == "0"); ActivateZhiyi(g); g = Cold(g, r); Branch(g, "benefit", target: 0); Play(g);
            Need(E<ZhiyiTransferProducerEvent>(g).Any(p => p.RecipientSeat == 0 && E<ZhiyiOwnedOriginEvent>(g).Any(o => o.Sequence == p.EntrySequence && o.CardId == stolen && o.OwnerSeat == 1)) && E<ZhiyiGainFrozenEvent>(g).Any(e => e.Gain.CardId == stolen && e.Gain.Foreign && e.Gain.OwnerSeat == 1 && e.Gain.RecipientSeat == 0 && e.Gain.EntrySequence < e.Gain.GainSequence), "True owned→Processing→Hand Snatch attribution retains the robbed owner's exact selected entity, origin and last-known color."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("claim"); RejectZhiyi = true; Native(g, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])); Play(g); RejectZhiyi = false;
            Need(E<ZhiyiOpportunityPaidEvent>(g).All(e => e.OwnerSeat != 0) && !E<ZhiyiGainFrozenEvent>(g).Any(e => e.Gain.Foreign && e.Gain.OwnerSeat == 0) && E<ProgramCurrentUsePhysicalCardsClaimedEvent>(g).Length > 0, "An actual use material claimed by its target is not a forged transfer of the original owner's cards."); _ = Cold(g, r);
        }
    }
    public static void ZexingPaidSourceLossDeathAndEquipmentRemovalCold()
    {
        foreach (var scene in new[] { "loss", "death", "stolen" })
        {
            var (g, r) = Start(scene); Stock(g); var shown = V(g, 0).Hand.First(c => c.Suit == Suit.Heart).Id; var pair = V(g, 0).Hand.Where(c => c.Suit == Suit.Spade).Take(2).Select(c => c.Id).ToArray();
            Use(g, "give-two", pair, [1]); Play(g); BeginZexing(g, shown, 1); Reach(g, () => Stage(g, TangTangStage.TakeCards)); foreach (var id in pair) Pick(g, id); Branch(g, "take-pay"); Reach(g, () => At(g, Watch)); var frame = Own(g)!.Id;
            g = Cold(g, r); Continue(g);
            if (scene == "stolen") { Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && P(g)!.Choices.Any(c => c.Cards.Contains(shown))); Answer(g, c => c.Cards.SequenceEqual([shown])); }
            if (scene == "death") Reach(g, () => E<TangTangCompletedEvent>(g).Any(e => e.FrameId == frame)); else Play(g);
            Need(E<TangTangEntityMovedEvent>(g).Count(e => e.FrameId == frame && e.Stage == TangTangStage.TakeChildren) == 2 && E<TangTangCompletedEvent>(g).Count(e => e.FrameId == frame) == 1, "An actually paid take drains native HP/skill/death children and closes the same owning chain once.");
            if (scene == "loss") Need(V(g, 0).IsAlive && !V(g, 0).Skills!.Any(s => s.Id == Zexing) && E<TangTangEntityMovedEvent>(g).Count(e => e.FrameId == frame && e.Stage == TangTangStage.GiftChildren && e.CardId == shown) == 1, "Actual conditional source loss after take2 preserves the original living issuer's shown-card gift.");
            else if (scene == "death") Need(!V(g, 0).IsAlive && g.State.Winner == Winner.None && !E<TangTangEntityMovedEvent>(g).Any(e => e.FrameId == frame && e.Stage == TangTangStage.GiftChildren), "A true non-Lord paid issuer death drains cleanup and grants no new cards or choices to the corpse.");
            else Need(!E<TangTangEntityMovedEvent>(g).Any(e => e.FrameId == frame && e.Stage == TangTangStage.GiftChildren) && g.CardMovements.Any(m => m.CardId == shown && m.To == CardLocation.DiscardPile), "A real nested child removes the original shown entity; no other card can replace its impossible later gift."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("equipment"); Stock(g); var ox = V(g, 0).Hand.First(c => c.Kind == CardKind.WoodenOx && c.Suit == Suit.Spade).Id; Native(g, a => a.Kind == LegalActionKind.Equip && a.CardId == ox); Play(g);
            var grain = V(g, 0).Hand.First(c => c.Kind != CardKind.SilverLion).Id; Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [1], g.Revision, P(g)!.PromptId)); Play(g);
            var lion = V(g, 0).Hand.First(c => c.Kind == CardKind.SilverLion && c.Suit == Suit.Spade).Id; Use(g, "place", [lion], [1]); Play(g); Use(g, "hurt-two", targets: [1]); Play(g); var hp = V(g, 1).Hp;
            Need(V(g, 1).WoodenOxGrain!.Any(c => c.Id == grain) && !(g.CreateSnapshot(0).Players[1].WoodenOxGrain ?? []).Any(c => c.Id == grain), "A real stored Ox grain transfers with its equipment and remains holder-private before take2.");
            var shown = V(g, 0).Hand.First(c => c.Suit == Suit.Heart).Id; BeginZexing(g, shown, 1); Reach(g, () => Stage(g, TangTangStage.TakeCards)); Pick(g, lion); Pick(g, ox); Branch(g, "take-pay"); Reach(g, () => At(g, Health));
            Need(V(g, 1).Hp == hp + 1 && V(g, 1).WoodenOxGrainCount == 0 && Own(g)!.TangTang!.Materials.Count == 2 && Own(g)!.TangTang!.Grain.SequenceEqual([grain]) && !E<TangTangEntityMovedEvent>(g).Any(e => e.Stage == TangTangStage.GiftChildren), "Real Silver Lion recovery and Ox grain spill are native children of the two primary equipment obtains before gift.");
            g = Cold(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision)); Play(g);
            Need(E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 1) == 1 && g.CardMovements.Any(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(1) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) && E<TangTangEntityMovedEvent>(g).Count(e => e.Stage == TangTangStage.TakeChildren) == 2 && E<TangTangEntityMovedEvent>(g).Single(e => e.Stage == TangTangStage.GiftChildren).CardId == shown, "Each primary equipment is obtained once; spilled grain is never a third primary, and the original gift follows all native children once."); _ = Cold(g, r);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame? Own(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.TangTang is not null);
    private static bool At(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill;
    private static bool Stage(GameEngine g, TangTangStage stage) => Own(g)?.TangTang?.Stage == stage && P(g)?.PlayerSeat == 0;
    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException("scene=" + Scene + " " + message); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Tang Tang command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "No real human option matched: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision)); }
    private static void Branch(GameEngine g, string branch, int? card = null, int? target = null) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == branch && (card is null || c.Cards.SequenceEqual([card.Value])) && (target is null || c.Targets.SequenceEqual([target.Value])));
    private static void Pick(GameEngine g, int id) => Branch(g, "pick", card: id);
    private static void Target(GameEngine g, int peer) => Branch(g, "peer", target: peer);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string activation, int[]? cards = null, int[]? targets = null, string skill = Driver) => Accept(g, new UseProgramSkillCommand(0, skill, activation, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void BeginZexing(GameEngine g, int shown, int peer) { Use(g, "shown-card-chain", skill: Zexing); Branch(g, "show", card: shown); Target(g, peer); }
    private static void ActivateZhiyi(GameEngine g) { Reach(g, () => At(g, Zhiyi) && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void Native(GameEngine g, Func<LegalAction, bool> predicate) { var a = g.GetHumanLegalActions().First(predicate); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
    private static void Stock(GameEngine g) { Use(g, "stock"); Play(g); }
    private static void Trim(GameEngine g, int[] keep) { var ids = V(g, 0).Hand.Where(c => !keep.Contains(c.Id)).Select(c => c.Id).ToArray(); if (ids.Length > 0) { Use(g, "trim", ids); Play(g); } }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 260; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Unexpected native completion at " + line + ": " + Diagnostic(g)); var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (At(g, Watch) || At(g, Health)) Continue(g);
            else if (At(g, Zhiyi) && RejectZhiyi) throw new InvalidOperationException("Unexpected false Zhiyi offer: " + Diagnostic(g));
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(g, c => c.Cards.Count == 0);
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else throw new InvalidOperationException("Unexpected native boundary at " + line + ": " + Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded native Tang Tang boundary at " + line + ": " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, g.State.Winner, Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(4).Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null, Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Kind = f.GetType().Name, Stage = (f as ProgramSkillFrame)?.TangTang?.Stage }), Facts = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay keeps actual phases, private frozen views, whole native invoices and once-only typed returns."); return copy; }
    private static void Frozen<T>(IReadOnlyList<T> list) { var refused = false; try { ((IList<T>)list)[0] = list[0]; } catch (NotSupportedException) { refused = true; } Need(refused, "Exposed nested original-entity lists must be immutable."); }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        Scene = scene; RejectZhiyi = false; var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-tang-tang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:tang-tang"); var defs = new List<JsonNode>();
            void Skill(string id, string body) => defs.Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")!);
            string A(string id, string effects, int cards = 0, int targets = 0, string more = "") => "{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + more + "\"effects\":[" + effects + "]}";
            var acts = new[] { A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), A("draw-two", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2}"), A("hurt-eleven", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":11}", targets: 1), A("hurt-two", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":2}", targets: 1),
                A("place", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}", 1, 1, "\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"),
                A("give-one", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gift\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"gift\",\"destination\":\"selectedTargetHand\"}", 1, 1, "\"sourceZones\":[\"hand\"],"),
                A("give-two", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gift\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"gift\",\"destination\":\"selectedTargetHand\"}", 2, 1, "\"sourceZones\":[\"hand\"],"),
                "{\"id\":\"trim\",\"minCards\":1,\"maxCards\":60,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"sourceZones\":[\"hand\"],\"usesPerTurn\":null,\"effects\":[{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"trim\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"trim\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}" };
            var views = scene == "dying" ? "[{\"id\":\"native-rescue\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"peach\",\"forPlay\":false,\"forResponse\":true,\"allowSameKind\":true}]" : scene == "snatch" ? "[{\"id\":\"native-snatch\",\"inputKinds\":[\"crossbow\"],\"inputSuits\":[],\"outputKind\":\"snatch\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true}]" : scene == "claim" ? "[{\"id\":\"native-slash\",\"inputKinds\":[\"crossbow\"],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}]" : "[]";
            Skill(Driver, "\"activations\":[" + string.Join(',', acts) + "],\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":50,\"priority\":0}],\"viewAs\":" + views);
            Skill(Quiet, "\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
            var extra = scene == "loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}" : scene == "death" ? ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : scene == "stolen" ? ",{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"amount\":1,\"zones\":[\"hand\"],\"resultBind\":\"stolen-shown\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"stolen-shown\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}" : "";
            Skill(Watch, "\"triggers\":[{\"id\":\"real-take-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.tang-tang.zexing.take\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paused\",\"options\":[{\"id\":\"continue\"}]}" + extra + "]}]");
            Skill(Health, "\"triggers\":[{\"id\":\"actual-lion-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"health\",\"options\":[{\"id\":\"continue\"}]}]}]");
            Skill(Conditional, "\"hpSkillGrants\":[{\"id\":\"healthy-zexing\",\"skillId\":\"ol:zexing\",\"minimumHp\":12}]");
            Skill(Insert, "\"triggers\":[{\"id\":\"real-inserted-play\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]");
            Skill(Claim, "\"triggers\":[{\"id\":\"native-target-claims-use\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"claimCurrentUsePhysicalCards\",\"target\":\"owner\"}]}]");
            var labels = defs.ToDictionary(n => n["id"]!.GetValue<string>(), n => new { name = n["id"]!.GetValue<string>(), description = "唐棠真实原生边界夹具", optionLabels = n["id"]!.GetValue<string>() is Watch or Health ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "唐棠真实夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:tt-owner-pick", "本人", "固定选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            var own = new List<string> { Driver, scene == "loss" ? Conditional : Zexing, Zhiyi };
            if (scene is "take" or "equip-show" or "mixed" or "loss" or "death" or "stolen") own.Add(Watch); if (scene == "scopes") own.Add(Insert); if (scene == "claim") own.Add(Claim);
            b.AddGeneral(new(Owner, "唐棠真实本人", "ol-tang-tang", "fixture:tt-owner-pick", "qun", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:tt-other-" + i).ToArray();
            foreach (var (id, i) in peers.Select((id, i) => (id, i))) { var pick = "fixture:tt-pick-" + i; b.AddSkill(new(pick, "真实其他角色", "固定顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (4 - i)) }); var skills = new List<string> { Quiet }; if (scene == "equipment") skills.Add(Health); if (scene == "snatch") skills.Add(Zhiyi); b.AddGeneral(new(id, "真实其他角色", "supporter", pick, "qun", 12, skills)); }
            foreach (var (kind, id) in new[] { (CardKind.Crossbow, "crossbow"), (CardKind.SilverLion, "lion"), (CardKind.WoodenOx, "ox") }) b.AddCard(new("fixture:tt-" + id, id, "原生实体", "真实牌面", kind));
            var kinds = scene == "equipment" ? new[] { "crossbow", "lion", "ox" } : ["crossbow"];
            var suits = scene is "red-empty" or "red-old" ? new[] { Suit.Heart } : scene is "scopes" or "dying" or "snatch" or "claim" ? [Suit.Spade] : scene == "mixed" ? [Suit.Spade, Suit.Heart, Suit.Club] : [Suit.Spade, Suit.Heart];
            b.AddDeck(new("fixture:tt-deck", "唐棠固定原生牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard("fixture:tt-" + kinds[i % kinds.Length], suits[(i / kinds.Length) % suits.Length], i % 13 + 1)).ToArray() });
            var roles = new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }; if (scene == "death") { roles[nameof(Role.Rebel)] = 2; roles[nameof(Role.Renegade)] = 1; }
            b.AddMode(new(Mode, "唐棠真实原生边界", 4, 4, roles, "fixture:tt-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
