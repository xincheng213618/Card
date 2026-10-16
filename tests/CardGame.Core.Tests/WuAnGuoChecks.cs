using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WuAnGuoChecks
{
    private const string Skill = "ol:liyong", Owner = "fixture:wag-owner", Driver = "fixture:wag-driver", Quiet = "fixture:wag-quiet", Response = "fixture:wag-response",
        Watch = "fixture:wag-watch", Health = "fixture:wag-health", Before = "fixture:wag-before", YangWindow = "fixture:wag-yang-window", Conditional = "fixture:wag-source", Mode = "identity:classic-wu-an-guo-fixture";
    public static void FourSuitsRealYangAndYinNativeResponsesCold()
    {
        var (g, r) = Start(); Use(g, "draw"); Play(g);
        Need(V(g).Hand.Select(c => c.Suit).Distinct().Count() == 4, "The one fixed seed and genuine native draw supply all four real suits without altering any frame or hand.");
        foreach (var suit in new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond })
        {
            var action = YangActions(g).First(a => V(g).Hand.Any(c => c.Id == a.CardId && c.Suit == suit)); var paid = action.CardId!.Value;
            PlayCard(g, action); Reach(g, () => P(g)?.SkillPrompt?.SkillId == YangWindow);
            Need(Yin(g) && E<WuAnGuoLiyongYangConsumedEvent>(g).Last().Suit == suit && g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.CardId == paid && u.CardKind == CardKind.Duel && u.Action?.EffectiveSuit == suit),
                "Yang pays one real entity as a native Duel and changes state before actual target/response children.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "The physical Yang material enters and leaves native Processing once, including cold response completion.");
            var before = V(g).HandCount; YinUse(g, 2); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
            var gain = E<WuAnGuoLiyongGainPaidEvent>(g).Last(); var origin = E<WuAnGuoLiyongDuelIssuedEvent>(g).Last().Origin;
            Need(!Yin(g) && gain.CardId is not null && V(g).HandCount == before + 1 &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 2 && e.Action.EffectiveKind == CardKind.Duel && e.Action.PhysicalCards.Count == 0 && e.Action.EffectiveSuit == Suit.None && e.Action.TargetSeats.SequenceEqual([0])) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "Yin first obtains an actual matching deck entity privately, changes state and then creates the selected actor's genuine colorless native Duel.");
            var slash = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0]; g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([slash]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response);
            var response = E<CardActionAcceptedEvent>(g).Last(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash).Action;
            Need(!E<WuAnGuoActualSuitUsedEvent>(g).Any(e => e.ActionId == response.ActionId) && !E<WuAnGuoActualSuitUsedEvent>(g).Any(e => e.ActorSeat == 2 && e.ActionId ==
                E<CardActionAcceptedEvent>(g).Last(a => a.Action.ActorSeat == 2 && a.Action.EffectiveKind == CardKind.Duel).Action.ActionId),
                "A played Slash during Duel and the cardless incoming Duel add no used suit; they remain real native actions.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == origin.CardUseFrameId) == 1 && E<WuAnGuoLiyongCompletedEvent>(g).Count(e => e.FrameId == origin.ProgramFrameId && e.DuelIssued) == 1,
                "The actual physical Slash response and the native Duel return once to the original paid Yin frame.");
        }
        Need(E<WuAnGuoLiyongYangConsumedEvent>(g).Length == 4 && E<WuAnGuoLiyongCompletedEvent>(g).Length == 4 && !YangActions(g).Any(),
            "Four distinct actual suits permit four alternating Yang uses in one Play, then every previously used suit is excluded without an artificial once-per-phase limit.");
        var forbidden = V(g).Hand.First(); var state = State(g);
        Need(!g.Submit(new PlayCardCommand(0, forbidden.Id, [1], g.Revision, P(g)!.PromptId, CardKind.Duel)
            { ConversionSource = E<WuAnGuoLiyongYangConsumedEvent>(g).First().Source }).Accepted && State(g) == state,
            "A forged repeat-suit Yang cannot pay a card, flip conversion state or change the accepted command journal."); _ = Cold(g, r);
    }
    public static void TurnBoundaryEmptySuitSetDeckMissAndEffectiveEquipmentCold()
    {
        {
            var (g, r) = Start("boundary"); PlayCard(g, YangActions(g).First()); Play(g); Need(Yin(g), "A real Yang leaves the conversion on Yin.");
            var turn = g.State.TurnNumber; End(g); Reach(g, () => g.State.TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Need(Yin(g) && !E<WuAnGuoActualSuitUsedEvent>(g).Any(e => e.ActualTurn == g.State.TurnNumber && e.ActorSeat == 0), "The actual new turn clears used-suit eligibility while the original source's conversion side persists.");
            g = Cold(g, r); YinUse(g, 2); Play(g);
            Need(E<WuAnGuoLiyongYinStartedEvent>(g).Last().UsedSuitMask == 0 && E<WuAnGuoLiyongGainPaidEvent>(g).Last().CardId is null && E<WuAnGuoLiyongCompletedEvent>(g) is [{ DuelIssued: true }] && !Yin(g) && YangActions(g).Any(),
                "An empty current-turn suit set obtains nothing and still performs Yin's genuine incoming Duel; the old turn's suit cannot leak into the new Yang eligibility."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("deck-miss"); PlayCard(g, YangActions(g).First()); Play(g); Use(g, "draw"); Play(g);
            Need(!g.CreateCardZoneDiagnostics().Any(z => z.Location == CardLocation.DrawPile), "The small real deck and its one discarded material are truly exhausted by native draws.");
            var reshuffles = g.CardMovements.Count(m => m.Reason == CardMoveReasons.Reshuffle); g = Cold(g, r); YinUse(g, 2); Play(g);
            Need(E<WuAnGuoLiyongYinStartedEvent>(g).Last().UsedSuitMask != 0 && E<WuAnGuoLiyongGainPaidEvent>(g).Last() is { CardId: null } gain && gain.Before == gain.After &&
                E<WuAnGuoLiyongCompletedEvent>(g) is [{ DuelIssued: true }] && g.CardMovements.Count(m => m.Reason == CardMoveReasons.Reshuffle) == reshuffles,
                "Searching a genuinely empty matching deck neither invents an entity nor reshuffles; the remaining native Duel and conversion still happen."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("effective"); PlayCard(g, YangActions(g).First()); Play(g);
            Need(E<WuAnGuoLiyongYangConsumedEvent>(g).Single().Suit == Suit.Heart && E<WuAnGuoActualSuitUsedEvent>(g).Any(e => e.ActorSeat == 0 && e.Suit == Suit.Heart) &&
                !E<WuAnGuoActualSuitUsedEvent>(g).Any(e => e.ActorSeat == 0 && e.Suit == Suit.Spade), "Native Hongyan changes a real printed Spade payment into the actually used Heart suit.");
            YinUse(g, 2); Play(g); Need(E<WuAnGuoLiyongGainPaidEvent>(g).Single().CardId is null,
                "Deck cards retain their actual deck suit: printed Spades are not matching Hearts merely because the receiver has Hongyan."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("lion"); var lion = V(g).Hand.First(c => c.Kind == CardKind.SilverLion).Id; Use(g, "place", [0], [lion]); Play(g); Use(g, "lose"); Play(g); var hp = V(g).Hp;
            var action = YangActions(g).First(a => a.CardId == lion); PlayCard(g, action); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
            Need(V(g).Hp == hp + 1 && g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1,
                "Yang accepts an actual equipped entity and inherits native SilverLion removal recovery before its HP child.");
            g = Cold(g, r); Continue(g); Play(g); Need(E<WuAnGuoLiyongYangConsumedEvent>(g).Single().UseFrameId > 0 && V(g).Equipment.Count == 0,
                "Cold native equipment-payment recovery returns to the same genuine Yang Duel and spends its equipped material once."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("regrant"); PlayCard(g, YangActions(g).First()); Play(g); var old = V(g).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!.Single(); Need(old.Value, "The old actual source generation is Yin.");
            Use(g, "lose"); Play(g); Need(V(g).Skills!.All(s => s.ContentId != Skill), "Actual HP loss removes the conditional source."); Use(g, "grant"); Play(g);
            var current = V(g).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!.Single();
            Need(current.SkillInstanceId != old.SkillInstanceId && !current.Value, "A genuinely different native acquired source starts at Yang rather than borrowing the lost conditional instance's Yin state.");
            g = Cold(g, r); Use(g, "recover"); Play(g); var restored = V(g).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!;
            Need(restored.Any(s => s.SkillInstanceId == old.SkillInstanceId && s.Value) && restored.Any(s => s.SkillInstanceId == current.SkillInstanceId && !s.Value),
                "Restoring the original conditional source preserves its existing Yin while the separate new source keeps its own initial Yang."); _ = Cold(g, r);
        }
    }
    public static void ActualUsedDodgeCounterspellAndPlayedSlashSeparationCold()
    {
        {
            var (g, r) = Start("dodge"); Use(g, "incoming", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0") == true);
            Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var card = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0]; g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([card])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response);
            var action = E<CardActionAcceptedEvent>(g).Last(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Dodge).Action;
            Need(E<WuAnGuoActualSuitUsedEvent>(g).Any(e => e.ActionId == action.ActionId && e.Suit == Suit.Spade) && !Yin(g),
                "A real Dodge defending against a native Slash is an actual use, so its effective suit is recorded before ResponseAccepted without changing an unrelated conversion side.");
            g = Cold(g, r); Continue(g); Play(g); Need(!YangActions(g).Any(), "The actual used Dodge suit excludes every same-suit Yang material in this real Play."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("counterspell"); PlayCard(g, YangActions(g).First());
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); var card = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([card])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response);
            var action = E<CardActionAcceptedEvent>(g).Last(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Nullification).Action;
            Need(E<WuAnGuoActualSuitUsedEvent>(g).Count(e => e.ActionId == action.ActionId && e.Suit == Suit.Heart) == 1 && Yin(g),
                "A genuine physical counterspell response is a use and records its actual suit once; it does not borrow the Yang binding or flip its state again.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(E<CardActionAcceptedEvent>(g).Where(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Nullification)
                .All(e => E<WuAnGuoActualSuitUsedEvent>(g).Count(f => f.ActionId == e.Action.ActionId) == 1), "The actual native counterspell chain preserves each exact accepted response-use identity across cold completion."); _ = Cold(g, r);
        }
    }
    public static void PaidYinGainChildrenLossDeathAndFrozenActorCold()
    {
        foreach (var scenario in new[] { "gain-child", "paid-loss", "source-death", "actor-death" })
        {
            var (g, r) = Start(scenario); if (scenario == "gain-child") { Use(g, "lose"); Play(g); }
            PlayCard(g, YangActions(g).First()); Play(g); var actor = Enumerable.Range(1, 3).First(s => V(g, s).Role != Role.Lord);
            YinUse(g, actor); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.WuAnGuoLiyong is not null); var receipt = frame.WuAnGuoLiyong!;
            Need(receipt is { Stage: WuAnGuoLiyongStage.GainChildren, ObtainedCardId: not null } && receipt.InitialActorSeat == actor &&
                E<WuAnGuoLiyongGainPaidEvent>(g).Count(e => e.FrameId == frame.Id) == 1 && !E<WuAnGuoLiyongDuelIssuedEvent>(g).Any(),
                "Yin freezes its selected actor and one already paid matching entity before the actual private CardsGained child; no Duel is issued early.");
            Need(Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].Hand.Count == 0), "The random obtained hand entity remains private throughout its real movement child.");
            g = Cold(g, r); Continue(g);
            if (scenario == "gain-child")
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Before); g = Cold(g, r); Continue(g);
            }
            if (scenario == "actor-death")
            {
                Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true); Answer(g, c => c.Targets.SequenceEqual([actor]));
            }
            Reach(g, () => Option(g, "after-child") || !V(g).IsAlive);
            if (scenario == "paid-loss") Need(V(g).Skills!.All(s => s.ContentId != Skill), "The real paid movement child removes the exact conditional source before Duel issuance.");
            if (scenario == "actor-death") Need(!V(g, actor).IsAlive, "The actual selected actor dies in the paid movement subtree.");
            g = Cold(g, r); if (Option(g, "after-child")) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-child");
            Reach(g, () => E<WuAnGuoLiyongCompletedEvent>(g).Any(e => e.FrameId == frame.Id));
            Need(E<WuAnGuoLiyongGainPaidEvent>(g).Count(e => e.FrameId == frame.Id) == 1 && E<WuAnGuoLiyongCompletedEvent>(g).Single(e => e.FrameId == frame.Id).DuelIssued == (scenario is "gain-child" or "paid-loss") &&
                E<WuAnGuoLiyongDuelIssuedEvent>(g).Count(e => e.Origin.ProgramFrameId == frame.Id) == (scenario is "gain-child" or "paid-loss" ? 1 : 0),
                "Paid source loss keeps the same genuine native Duel; owner or frozen-actor death stops issuance without selecting a replacement, repaying the gain or changing the already spent side.");
            if (scenario == "source-death") Need(!V(g).IsAlive && g.State.Winner == Winner.None, "The native damage/dying subtree really kills the non-Lord source and returns to its paid receipt."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Yin(GameEngine g) => V(g).SkillRuntimeStates!.Single(s => s.SkillId == Skill).BooleanStates!.Single().Value;
    private static IEnumerable<LegalAction> YangActions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ConversionSource is { SkillId: Skill, BindingId: "yang-unused-suit" } && a.PlayedCardKind == CardKind.Duel);
    private static bool Option(GameEngine g, string id) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
    private static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Wu An Guo command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the actual human answers a prompt."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Decline(GameEngine g) => Answer(g, c => c.Cards.Count == 0);
    private static void PlayCard(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources ?? [] });
    private static void YinUse(GameEngine g, int actor) => Accept(g, new UseProgramSkillCommand(0, Skill, "yin-obtain-and-incoming-duel", [], [actor], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 320; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Wu An Guo ended before its actual boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification }) Decline(g);
            else if (p is { Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (Option(g, "continue")) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected Wu An Guo prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Wu An Guo actual boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Select(c => c.Parameters) } : null,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Frames = g.ResolutionStack.Select(f => new { f.Id, Kind = f.GetType().Name }), Tail = g.Events.TakeLast(8).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(copy), "Cold replay preserves every filtered private view, effective-suit use fact, source-generation side and paid native gain/Duel cursor."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scenario == "source-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu-an-guo", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:wu-an-guo"); if (scenario == "effective") CharacterFixtureContent.Register(b, "classic:xiao-qiao");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":100,"priority":0}],"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]},
              {"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:liyong"]}]},
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]}]},
             {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Response}}}","revision":1,"triggers":[{"id":"native-response","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","dodge","nullification"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"response","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备", description = "仅实际原生指令" }, [Quiet] = new { name = "安静他人回合", description = "实际跳出牌" },
                [Response] = new { name = "实际响应子窗", description = "保留原生已付实体响应", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            void Add(string id, string body, string[]? options = null) { rules["skills"]!.AsArray().Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")); names[id] = new { name = id, description = "真实原生子窗", optionLabels = (options ?? []).ToDictionary(x => x, x => x) }; }
            var skills = new List<string> { Driver, Response };
            if (scenario == "plain") { Add(YangWindow, "\"triggers\":[{\"id\":\"native-yang-targets\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"duel\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"targets\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(YangWindow); }
            if (scenario is "paid-loss" or "regrant") { Add(Conditional, "\"hpSkillGrants\":[{\"id\":\"real-full-source\",\"skillId\":\"ol:liyong\",\"minimumHp\":13}]"); skills.Add(Conditional); } else skills.Add(Skill);
            if (scenario == "effective") skills.Add("classic:hongyan");
            if (scenario is "gain-child" or "paid-loss" or "source-death" or "actor-death")
            {
                var child = scenario == "gain-child" ? "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" :
                    scenario == "paid-loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scenario == "source-death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" :
                    "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":20}";
                Add(Watch, "\"triggers\":[{\"id\":\"native-gain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.wu-an-guo.liyong.obtain\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]}," + child + ",{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"after\",\"options\":[{\"id\":\"after-child\"}]}]}]", ["continue", "after-child"]); skills.Add(Watch);
            }
            if (scenario is "gain-child" or "lion")
            {
                Add(Health, "\"triggers\":[{\"id\":\"native-health\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"health\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Health);
                if (scenario == "gain-child") { Add(Before, "\"triggers\":[{\"id\":\"native-damage\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"damage\",\"options\":[{\"id\":\"continue\"}]}]}]", ["continue"]); skills.Add(Before); }
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:wag-pick-owner", "本人选将", "真实公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "武安国本人", "ol-wu-an-guo", "fixture:wag-pick-owner", "qun", scenario == "source-death" ? 13 : 12, skills));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:wag-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            { var pick = "fixture:wag-pick-" + index; b.AddSkill(new(pick, "其他角色次序", "真实公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - index)) }); b.AddGeneral(new(peer, "真实其他角色", "supporter", pick, "wei", 12, [Quiet])); }
            b.AddCard(new("fixture:wag-lion", "白银狮子", "装备牌", "实际离场回复", CardKind.SilverLion));
            var dodge = scenario is "dodge" or "regrant" or "gain-child" or "paid-loss" or "source-death" or "actor-death";
            b.AddDeck(new("fixture:wag-deck", "固定实体小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, scenario is "boundary" or "deck-miss" ? 24 : 96).Select(i => new ContentDeckPhysicalCard(
                scenario == "lion" ? "fixture:wag-lion" : scenario == "counterspell" ? "standard:nullification" : dodge ? "standard:dodge" : "standard:slash",
                scenario == "plain" ? new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4] : scenario == "counterspell" ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "武安国真实原生命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:wag-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
