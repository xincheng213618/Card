using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5MoushiChecks
{
    private const string Moushi = StandardShanheS5MoushiPackage.MoushiId;
    private const string Driver = "fixture:shanhe-moushi-driver", GiftHold = "fixture:shanhe-moushi-gift";
    private const string DrawHold = "fixture:shanhe-moushi-draw", Recipient = "fixture:shanhe-moushi-recipient";
    private const string Actor = "fixture:shanhe-moushi-actor", Peer = "fixture:shanhe-moushi-peer", Deck = "fixture:shanhe-moushi-deck";
    private const string GiftReason = "activity.guandu.guandu:moushi.gift", DrawReason = "activity.guandu.guandu:moushi.draw";

    public static void MoushiActualNextPlayKeepsPaidGiftAndNativeDrawChildrenCold()
    {
        var s = Scene.Start(); s.Play();
        var card = s.V(1).Hand.Single().Id;
        s.EndPlay(); s.Until(() => s.P is { PlayerSeat: 1 } && s.P.SkillPrompt?.SkillId == Moushi && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "gift"));
        // The trusted driver answers only the NPC's actually published private choice;
        // no recipient hand information or NPC prompt is exposed in the human UI.
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([card]) && c.Targets.SequenceEqual([0]));
        s.Until(() => s.P?.SkillPrompt?.SkillId == GiftHold);
        Need(s.V(1).HandCount == 0 && s.V().Hand.Any(c => c.Id == card) && s.E<GuanduMoushiGiftedEvent>().Length == 0 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Moushi).GuanduGeneral is { Stage: "moushi-arm" },
            "The exact physical gift is paid before the real recipient's gain child, while the original Moushi frame still owns its unarmed continuation.");
        s.Cold(); s.Continue(); s.Play();
        var gift = s.E<GuanduMoushiGiftedEvent>().Single();
        Need(gift is { OwnerSeat: 1, RecipientSeat: 0 } && gift.CardId == card &&
            s.G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == GiftReason) == 1,
            "The resumed gain child arms the paid gift exactly once outside the Guandu activity.");
        Need(s.E<GuanduMoushiDrawnEvent>().Length == 0 && s.E<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 0, Amount: 1 },
            "A real damage source that has not reached its next actual turn Play cannot trigger the newly gifted relation.");

        var phase = s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last(e => e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play).Phase;
        Need(s.G.State.CurrentSeat == 0 && s.E<TurnStartedEvent>().First(e => e.ActorSeat == 0 && e.TurnNumber > gift.CreatedTurn).TurnNumber == phase.TurnNumber,
            "The real gifted role now owns its exact next actual turn and Play phase, rather than a fabricated damage context.");
        s.Damage(2); s.DrawChild();
        var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Moushi);
        var paid = s.E<GuanduGeneralDrawEvent>().SingleOrDefault(e => e.FrameId == producer.Id);
        Need(s.E<GuanduMoushiDrawnEvent>().Single() is { VictimSeat: 2 } &&
            producer.OwnerSeat == 1 && producer.NativeDraw is null && producer.GuanduGeneral is
                { Stage: "finish", Draw: { RecipientSeat: 1, RequestedCount: 1, Replacement: null, ActualDrawIds: [var drawnId] } outcome } &&
            paid is { RecipientSeat: 1, Requested: 1, Actual: 1, Replaced: false } &&
            paid.Before == outcome.SequenceBefore && paid.After == outcome.SequenceAfter &&
            s.E<GuanduGeneralDrawEvent>().Length == 1 && s.G.CardMovements.Count(m => m.CardId == drawnId &&
                m.Sequence > paid.Before && m.Sequence <= paid.After && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(1) && m.Reason.Value == DrawReason) == 1,
            "The exact original Moushi producer owns its one completed native draw receipt and original DrawPile entity while the real paid gain child suspends its finish stage. " + s.Diagnostic());
        s.Cold(); s.Play(); s.Damage(2); s.Play();
        Need(s.E<GuanduMoushiDrawnEvent>().Length == 1,
            "A second actual damage to the same victim in that Play does not duplicate the source's reward.");
        s.Damage(3); s.DrawChild(); s.Cold(); s.Play();
        Need(s.E<GuanduMoushiDrawnEvent>().Where(e => e.ProducerFrameId == gift.FrameId).Select(e => e.VictimSeat).SequenceEqual([2, 3]) &&
            s.E<GuanduMoushiDrawnEvent>().All(e => e.ActualPlayToken == phase.Token) &&
            s.E<GuanduGeneralDrawEvent>().Count(e => e.RecipientSeat == 1 && e.Requested == 1 && e.Actual == 1) == 2 &&
            s.E<GuanduMoushiGiftedEvent>().Length == 1 && s.G.CardMovements.Count(m => m.CardId == card && m.Reason.Value == GiftReason) == 1,
            "Different real victims each reward once in the one eligible Play; both original native draws and the cross-seat gift remain once-paid after cold recovery.");
        s.Cold();
    }

    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "Rejected native Shanhe Moushi command."); }
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void DrawChild() => Until(() => P?.SkillPrompt?.SkillId == DrawHold);
        public void Damage(int target) => Accept(new UseProgramSkillCommand(0, Driver, "damage", [], [target], G.Revision, P!.PromptId));
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Until(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The compact real-turn Moushi fixture must keep its combatants alive.");
                if (P is { PlayerSeat: 0 } p)
                {
                    var skip = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                    if (skip is not null) { Answer(c => c.Id == skip.Id); continue; }
                    throw new InvalidOperationException("Unexpected Shanhe Moushi boundary: " + JsonSerializer.Serialize(p));
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded actual-turn Moushi continuation was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, G.PlayerCount).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            Current = new { G.State.TurnNumber, G.State.CurrentSeat, Prompt = P?.SkillPrompt?.SkillId, PromptSeat = P?.PlayerSeat },
            Programs = G.ResolutionStack.OfType<ProgramSkillFrame>().TakeLast(6).Select(f => new
                { f.Id, f.SkillId, f.OwnerSeat, f.InstructionIndex, f.GuanduGeneral, f.NativeDraw, f.ParentFrameId }),
            Frames = G.ResolutionStack.TakeLast(10).Select(f => new { f.Id, f.Kind }),
            Gifts = E<GuanduMoushiGiftedEvent>().TakeLast(2), Rewards = E<GuanduMoushiDrawnEvent>().TakeLast(3),
            NativeReturns = E<GuanduGeneralDrawEvent>().TakeLast(3), Attempts = E<NativeDrawAttemptStartedEvent>().TakeLast(3),
            Decisions = E<NativeDrawAttemptDecidedEvent>().TakeLast(3),
            Moves = G.CardMovements.Where(m => m.Reason.Value is GiftReason or DrawReason).TakeLast(6)
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the exact Moushi gift, actual next Play, per-victim usage and paid gain/draw children.");
        }
        public static Scene Start()
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new StandardShanheS5MoushiPackage(), new Fixture()]);
            var setup = new ShanheBattleSetup(67, 1, "fixture-s5-moushi", Actor, 8, 0, 0, 0, 0,
                [GiftHold], [], [],
                [new(Recipient) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7)] }, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [], new string('e', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-moushi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","sourceRef":{"kind":"owner"},"amount":1}]}],
                "triggers":[{"id":"real-before-next-turn-damage","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":false,"effects":[
                  {"op":"damage","target":"owner","sourceRef":{"kind":"owner"},"amount":1}]}]},
              {"id":"{{{GiftHold}}}","revision":1,"triggers":[{"id":"paid-cross-seat-gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{GiftReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift","options":[{"id":"continue"}]}]}]},
              {"id":"{{{DrawHold}}}","revision":1,"triggers":[{"id":"paid-native-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实伤害驱动", description = "合法owner伤害与真实外国回合结束窗口" },
                    [GiftHold] = new { name = "真实赠牌孩子", description = "暂停原实体已移交后的原生获得窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [DrawHold] = new { name = "真实摸牌孩子", description = "暂停原生摸牌已付后的获得窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Actor, "实际收牌者", "supporter", Driver, "qun", 8));
            b.AddGeneral(new(Recipient, "实际谋识来源", "ol-xun-chen", Moushi, "qun", 8, [DrawHold]));
            b.AddGeneral(new(Peer, "实际受伤者", "supporter", "standard:none", "qun", 8));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋识跨模式真实回合", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Actor, Recipient, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
