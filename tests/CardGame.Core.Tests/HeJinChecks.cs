using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HeJinChecks
{
    private const string Mouzhu = "ol:mouzhu", Yanhuo = "ol:yanhuo", Owner = "fixture:hj-owner", Driver = "fixture:hj-driver",
        Quiet = "fixture:hj-quiet", Watch = "fixture:hj-watch", Health = "fixture:hj-health", Target = "fixture:hj-target", Source = "fixture:hj-source", Response = "fixture:hj-response",
        Mode = "identity:classic-he-jin-fixture", InitialEmpty = "fixture:hj-initial-empty", DonorRange = "fixture:hj-donor-range";
    public static void DonorPrivateGiftActualComparisonAndPhaseQuotaCold()
    {
        {
            var (g, registry) = Start("human-donor");
            Need(P(g) is { PlayerSeat: 1, IsPrivate: true } && Enumerable.Range(0, 4).Where(s => s != 1).All(s => g.CreateSnapshot(s).PendingDecision is null),
                "Only the actual donor sees its real private hand gift; the AI issuer has no donor prompt.");
            var r = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HeJinMouzhu is not null).HeJinMouzhu!;
            Need(r.ActorSeat == 1 && V(g, r.Source.OwnerSeat).GeneralId == Owner && P(g)!.Choices.All(c => c.Cards.Count == 1 && V(g, 1).Hand.Any(h => h.Id == c.Cards[0])),
                "The frozen initial donor hand contains real entities and is owned by the human, not chosen by the skill source.");
            var initial = V(g, 1).HandCount; var view = State(g); var p = P(g)!;
            Need(!g.Submit(new AnswerPromptCommand(r.Source.OwnerSeat, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && State(g) == view,
                "The foreign AI issuer cannot answer the human's private gift or alter a rejected command.");
            g = Cold(g, registry); Answer(g, _ => true);
            Reach(g, () => Branch(g, "assault"));
            Need(P(g) is { PlayerSeat: 1, IsPrivate: true } && V(g, 1).HandCount == initial - 1 &&
                P(g)!.Choices.Select(c => c.Parameters["card-kind"]).Order().SequenceEqual(new[] { "Duel", "Slash" }) &&
                E<HeJinMouzhuGiftPaidEvent>(g).Length == 1,
                "After the real hand transfer and its children, the donor personally chooses a normal legal Slash or Duel.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("card-kind") == "Slash");
            Reach(g, () => E<HeJinMouzhuCompletedEvent>(g).Length == 1);
            Need(E<HeJinMouzhuAssaultIssuedEvent>(g) is [{ Origin.ActorSeat: 1, Origin.CardKind: CardKind.Slash }] &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 1 && e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Count == 0),
                "The donor's answer produces a genuine zero-entity native Slash and returns once through the AI source.");
            _ = Cold(g, registry);
        }
        foreach (var counts in new[] { (Owner: 1, Other: 3), (Owner: 0, Other: 4) })
        {
            var (g, registry) = Start(); Arrange(g, counts.Owner, counts.Other); UseMouzhu(g, 1); Play(g);
            Need(E<HeJinMouzhuGiftPaidEvent>(g).Length == 1 && E<HeJinMouzhuCompletedEvent>(g) is [{ AssaultIssued: false }] &&
                !E<HeJinMouzhuAssaultIssuedEvent>(g).Any() && V(g, 0).HandCount == counts.Owner + 1 && V(g, 1).HandCount == counts.Other - 1 &&
                !g.GetHumanLegalActions().Any(a => a.SkillId == Mouzhu),
                "Equal or greater donor hand count after the payment issues no assault and still consumes exactly one real Play use.");
            _ = Cold(g, registry);
        }
        {
            var (g, _) = Start(); Arrange(g, 6, 0); var before = State(g);
            Need(!g.GetHumanLegalActions().Where(a => a.SkillId == Mouzhu).Any(a => a.TargetSeats.Contains(1)) &&
                !g.Submit(new UseProgramSkillCommand(0, Mouzhu, "demand-hand-and-assault", [], [1], g.Revision, P(g)!.PromptId)).Accepted && State(g) == before,
                "A target unable to give its required hand card is excluded and a forged empty-hand demand is rejected without a quota debit.");
        }
    }
    public static void NativeSlashDuelChooserRangeAndResponsesCold()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.Duel })
        {
            var (g, registry) = Start("human-donor"); Answer(g, _ => true); Reach(g, () => Branch(g, "assault"));
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("card-kind") == kind.ToString());
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target || E<HeJinMouzhuCompletedEvent>(g).Length == 1);
            var origin = E<HeJinMouzhuAssaultIssuedEvent>(g).Single().Origin;
            Need(origin.ActorSeat == 1 && origin.TargetSeat == origin.Source.OwnerSeat && origin.CardKind == kind &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 1 && e.Action.EffectiveKind == kind && e.Action.PhysicalCards.Count == 0 &&
                    e.Action.ConversionChain.Count == 0 && e.Action.TargetSeats.SequenceEqual([origin.TargetSeat])),
                "Both personal donor branches create the exact genuine colorless native card action, with no physical payment or fabricated damage.");
            g = Cold(g, registry); Reach(g, () => E<HeJinMouzhuCompletedEvent>(g).Length == 1);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == origin.CardUseFrameId) == 1 &&
                E<HeJinMouzhuGiftPaidEvent>(g).Length == 1 && E<HeJinMouzhuCompletedEvent>(g) is [{ AssaultIssued: true }] &&
                (kind == CardKind.Duel ? E<DuelResponseEvent>(g).Any(e => e.ResolutionId == origin.CardUseFrameId) : E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == origin.TargetSeat)),
                "Actual native responses, damage and completion return cold to the already paid demand exactly once.");
            _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start("range"); UseMouzhu(g, 2);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target);
            var origin = E<HeJinMouzhuAssaultIssuedEvent>(g).Single().Origin;
            Need(origin.ActorSeat == 2 && origin.TargetSeat == 0 && origin.CardKind == CardKind.Duel,
                "A seat two donor outside current Slash range has only its actual legal native Duel branch; Mouzhu grants no distance bypass.");
            g = Cold(g, registry); Continue(g); Play(g); _ = Cold(g, registry);
        }
        foreach (var response in new[] { CardKind.Dodge, CardKind.Slash })
        {
            var (g, registry) = Start(response == CardKind.Dodge ? "physical-dodge" : "physical-slash");
            UseMouzhu(g, response == CardKind.Dodge ? 1 : 2); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target); Continue(g);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash });
            var id = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0]; g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([id]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Response);
            var use = E<HeJinMouzhuAssaultIssuedEvent>(g).Single().Origin;
            Need(g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.CardUseFrameId && f.HeJinMouzhu is not null) &&
                g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(f => f.Action.Type == CardActionType.Response &&
                    f.Candidates.Any(c => registry.GetSkill(c.SkillId).Program!.Triggers.Any(t => t.Id == c.TriggerId && t.Window == SkillProgramTriggerWindow.CardResponseAccepted))) &&
                g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == response && e.Action.PhysicalCards.Count == 1),
                "The real physical Dodge/Slash response payment reaches native ResponseAccepted inside the cardless assault, rather than being rejected as unrelated processing cards.");
            g = Cold(g, registry); Continue(g); Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.CardUseFrameId) == 1 && E<HeJinMouzhuCompletedEvent>(g) is [{ AssaultIssued: true }] &&
                g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0)) == 1,
                "Cold physical-response children normalize their own costs and return to the original native assault and paid Mouzhu once.");
            _ = Cold(g, registry);
        }
    }
    public static void PaidGiftChildrenRecompareAndSurviveSourceLossOrDeathCold()
    {
        foreach (var scenario in new[] { "gift-recompare", "gift-loss", "gift-death" })
        {
            var (g, registry) = Start(scenario); UseMouzhu(g, 1);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && Option(g, "continue"));
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HeJinMouzhu is not null);
            Need(root.HeJinMouzhu is { Stage: HeJinMouzhuStage.GiftChildren, GiftId: not null } && E<HeJinMouzhuGiftPaidEvent>(g).Length == 1 &&
                !E<HeJinMouzhuAssaultIssuedEvent>(g).Any(), "The actual card transfer is paid before its native CardsGained child and the live comparison.");
            g = Cold(g, registry); Continue(g);
            Reach(g, () => scenario == "gift-death" ? E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) : Option(g, "after-child"));
            Need(scenario == "gift-loss" ? V(g, 0).Skills!.All(s => s.ContentId != Mouzhu) : scenario != "gift-death" || !V(g, 0).IsAlive,
                "The genuine gain child can remove the conditional source or kill its owner after the one actual transfer.");
            g = Cold(g, registry); if (scenario != "gift-death") Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-child");
            Reach(g, () => E<HeJinMouzhuCompletedEvent>(g).Length == 1 || g.State.Winner != Winner.None);
            Need(E<HeJinMouzhuGiftPaidEvent>(g).Length == 1 &&
                (scenario == "gift-loss" ? E<HeJinMouzhuAssaultIssuedEvent>(g).Length == 1 && E<HeJinMouzhuCompletedEvent>(g) is [{ AssaultIssued: true }] :
                    E<HeJinMouzhuAssaultIssuedEvent>(g).Length == 0),
                "Post-child actual counts decide the assault; paid source loss still issues it, while death or actual equality prevents it without repaying the gift.");
            if (scenario == "gift-recompare") Need(V(g, 0).HandCount == 0 && V(g, 1).HandCount == 3, "The real gain child discards the receiver's hand before the actual comparison.");
            _ = Cold(g, registry);
        }
    }
    public static void DeathOwnedCardsOpaqueKillerDiscardAndEquipmentChildrenCold()
    {
        foreach (var scenario in new[] { "death-hand", "death-lion", "death-killer-death", "death-no-killer" })
        {
            var (g, registry) = Start(scenario); var killer = Enumerable.Range(1, 3).First(s => V(g, s).Role != Role.Lord);
            if (scenario != "death-no-killer")
            {
                var lightning = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Lightning);
                Accept(g, new PlayCardCommand(0, lightning.CardId!.Value, lightning.TargetSeats, g.Revision, P(g)!.PromptId, lightning.PlayedCardKind)); Play(g);
                Use(g, "equip", [0]); Play(g); Use(g, "equip", [killer]); Play(g); Use(g, "hurt", [killer]); Play(g);
                Arrange(g, 2, V(g, 1).HandCount);
                Need(V(g, 0).Equipment.Count == 1 && V(g, 0).Judgment.Count == 1 && V(g, 0).HandCount == 2, "Real setup contains two owned hand cards, one owned armor and one unowned judgment card.");
                Use(g, "near-death"); Play(g); Use(g, "fatal", [killer]); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Yanhuo);
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, () => Branch(g, "confirm"));
                var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HeJinYanhuo is not null); var r = frame.HeJinYanhuo!;
                Need(!V(g, 0).IsAlive && r.KillerSeat == killer && r.Limit == 3 && r.OriginalDying is not null && r.OriginalDamage is not null &&
                    P(g) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
                    P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand").All(c => c.Cards.Count == 0),
                    "The dead owner freezes X from predeath owned HE, excludes Judgment, preserves its original damage/dying/death return and sees only opaque killer hand slots.");
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == (scenario == "death-hand" ? "Hand" : "Equipment"));
                Need(!E<HeJinYanhuoDiscardPaidEvent>(g).Any(), "Private selection itself pays nothing and exposes no killer card before confirming the whole discard.");
                g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "confirm");
                if (scenario != "death-hand")
                {
                    Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
                    Need(E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == killer && e.Amount == 1) &&
                        g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == frame.Id && f.HeJinYanhuo is { Stage: HeJinYanhuoStage.DiscardChildren, Discarded: 1 }),
                        "Discarding the killer's actual SilverLion opens its native recovery and health child beneath the still suspended original dead victim.");
                    g = Cold(g, registry); Continue(g);
                }
                Reach(g, () => E<HeJinYanhuoCompletedEvent>(g).Length == 1);
                Need(E<HeJinYanhuoDiscardPaidEvent>(g).Length == 1 && E<HeJinYanhuoCompletedEvent>(g) is [{ Discarded: 1 }] &&
                    E<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 0) == 1 && (scenario != "death-killer-death" || !V(g, killer).IsAlive),
                    "An up-to-X choice may stop at one; its actual hand/armor payment and all native recovery or killer-death children return exactly once.");
                _ = Cold(g, registry);
            }
            else
            {
                Use(g, "self-loss"); Reach(g, () => E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0));
                Need(!E<HeJinYanhuoStartedEvent>(g).Any(), "Source-less HP loss has no killer and cannot invent a Yanhuo discard target."); _ = Cold(g, registry);
            }
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Branch(GameEngine g, string branch) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static bool Option(GameEngine g, string id) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real He Jin command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the real human can answer."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void UseMouzhu(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Mouzhu, "demand-hand-and-assault", [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, binding, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Arrange(GameEngine g, int owner, int other)
    {
        var ids = V(g, 0).Hand.Take(Math.Max(0, V(g, 0).HandCount - owner)).Select(c => c.Id).ToArray();
        if (ids.Length > 0) { Use(g, "trim", cards: ids); Play(g); }
        while (V(g, 1).HandCount > other)
        { Use(g, "trim-peer", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == "Hand"); Play(g); }
        Need(V(g, 0).HandCount == owner && V(g, 1).HandCount == other, "Real setup establishes exact small hand counts.");
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 220; step++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "He Jin ended before its required boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) Accept(g, new EndPlayPhaseCommand(p.PlayerSeat, g.Revision, p.PromptId));
            else if (p is { Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0);
            else if (Option(g, "continue")) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null) throw new InvalidOperationException("Unexpected real He Jin human prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing real He Jin boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g),
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, V(g, s).GeneralId, V(g, s).HandCount, V(g, s).Skills }),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Tail = g.Events.TakeLast(16).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(copy), "Cold He Jin preserves actual private chooser, original death/dying cursors, frozen payments and true native assault returns."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var human = scenario.StartsWith("human-donor", StringComparison.Ordinal) ? 1 : 0;
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = human, HumanRole = human == 1 || scenario.StartsWith("death", StringComparison.Ordinal) || scenario == "gift-death" ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(human, human == 0 ? Owner : "fixture:hj-peer-0", g.Revision, P(g)!.PromptId));
        if (human == 0) Play(g); else Reach(g, () => P(g) is { PlayerSeat: 1 } && Branch(g, "gift")); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-he-jin", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:he-jin");
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"large-hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"trim","minCards":1,"maxCards":20,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","sourceZones":["hand"],"usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                {"id":"fatal","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":20}]},
                {"id":"self-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
                {"id":"near-death","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":7}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"give","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Target}}}","revision":1,"triggers":[{"id":"true-assault-target","window":"cardUseTargetsFinalized","ownerRelation":"target","cardKinds":["slash","duel"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"target","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备", description = "真实移动及伤害" }, [Quiet] = new { name = "原生跳过出牌", description = "其他角色保持安静" },
                [Target] = new { name = "实际原生用牌目标", description = "杀或决斗真实目标窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var extras = new List<string> { Mouzhu, Yanhuo, Target }; if (scenario != "human-donor") extras.Add(Driver);
            if (scenario == "human-donor")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$$"""{"id":"{{{InitialEmpty}}}","revision":1,"triggers":[{"id":"real-empty-opening","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$$"""{"id":"{{{DonorRange}}}","revision":1,"modifiers":[{"id":"real-donor-range","query":"attackRange","operation":"set","value":3,"priority":0}]}"""));
                names[InitialEmpty] = new { name = "真实清空其他起始手牌", description = "原生开局弃置令真人成为唯一可交手牌者" };
                names[DonorRange] = new { name = "实际给牌者攻击范围", description = "真实范围三让本人可独立选杀或决斗" };
            }
            if (scenario.StartsWith("gift-", StringComparison.Ordinal))
            {
                extras.Add(Watch);
                var child = scenario == "gift-recompare" ? "{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]}" :
                    "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scenario == "gift-death" ? "20" : "1") + "}";
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$$"""{"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"actual-gift-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.he-jin.mouzhu.give"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{{{child}}},{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"after-child"}]}]}]}"""));
                names[Watch] = new { name = "真正交牌后子窗", description = "真实付款后再改变状态", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["after-child"] = "继续已付谋诛" } };
                if (scenario == "gift-loss")
                {
                    extras.Remove(Mouzhu); extras.Add(Source);
                    rules["skills"]!.AsArray().Add(JsonNode.Parse($$$"""{"id":"{{{Source}}}","revision":1,"hpSkillGrants":[{"id":"actual-nine","skillId":"{{{Mouzhu}}}","minimumHp":9}]}"""));
                    names[Source] = new { name = "实际体力来源", description = "主公9体力获得谋诛，真实失体力撤销" };
                }
            }
            if (scenario.StartsWith("death-", StringComparison.Ordinal))
            {
                var tail = scenario == "death-killer-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$$"""{"id":"{{{Health}}}","revision":1,"triggers":[{"id":"actual-lion-child","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery","options":[{"id":"continue"}]}{{{tail}}}]}]}"""));
                names[Health] = new { name = "原生狮子回复子窗", description = "真实装备离场回复和孩子死亡", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            if (scenario.StartsWith("physical-", StringComparison.Ordinal))
            {
                extras.Add(Response);
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$$"""{"id":"{{{Response}}}","revision":1,"triggers":[{"id":"actual-native-response","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","dodge"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"response","options":[{"id":"continue"}]}]}]}"""));
                names[Response] = new { name = "实际响应已支付", description = "真实实体响应孩子", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pickOwner = "fixture:hj-pick-owner";
            b.AddSkill(new(pickOwner, "固定何进", "公开身份选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => scenario.StartsWith("human-donor", StringComparison.Ordinal) && r == Role.Lord ? 100000000d : -100000000d) });
            b.AddGeneral(new(Owner, "何进本人", "ol-he-jin", pickOwner, "qun", 8, extras));
            var peers = Enumerable.Range(0, 3).Select(i => "fixture:hj-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            {
                var pick = "fixture:hj-pick-" + index;
                b.AddSkill(new(pick, "固定其他角色", "公开选将顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (3 - index)) });
                b.AddGeneral(new(peer, "真实其他", "supporter", pick, "wei", 8, scenario == "human-donor" ? index == 0 ? [Quiet, Target, DonorRange] : [Quiet, Target, InitialEmpty] :
                    scenario.StartsWith("death-", StringComparison.Ordinal) ? [Quiet, Target, Health] : [Quiet, Target]));
            }
            b.AddCard(new("fixture:hj-lion", "白银狮子", "装备牌", "真实装备离场回复", CardKind.SilverLion));
            b.AddCard(new("fixture:hj-ox", "木牛流马", "装备牌", "真实宝物离场", CardKind.WoodenOx));
            b.AddCard(new("fixture:hj-lightning", "闪电", "延时锦囊", "真实判定区牌", CardKind.Lightning));
            b.AddDeck(new("fixture:hj-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard(
                scenario.StartsWith("death-", StringComparison.Ordinal) ? (i % 2 == 0 ? "fixture:hj-lion" : "fixture:hj-lightning") : scenario == "physical-dodge" ? "standard:dodge" : "standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实何进命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:hj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
