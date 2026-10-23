using System.Text.Json;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Content.Standard;

internal static class ShanheS5MaSiblingsChecks
{
    private const string Tieqi = StandardShanheS5MaSiblingsPackage.TieqiId;
    private const string Peer = "fixture:s5-ma-peer", Target = "fixture:s5-ma-target";
    private const string Quiet = "fixture:s5-ma-quiet", Armor = "fixture:s5-ma-armor", Watch = "fixture:s5-ma-watch";
    private const string Deck = "fixture:s5-ma-deck";
    private const string CostReason = "skill-program.shanhe-tieqi.discard";

    public static void ColorAllCostJudgmentSuppressionAndNativeDodgeCold()
    {
        var paid = Scene.Start("paid"); paid.Play();
        Need(paid.G.GetCombatDistance(0, 2) == 1 && paid.G.GetCombatDistance(1, 3) == 1 && paid.G.GetCombatDistance(3, 1) == 2,
            "Both actual sibling bodies set their final outgoing distance to one despite the target's extra incoming distance; the reverse direction remains ordinary.");
        Need(paid.V(2).Equipment is [{ Kind: CardKind.SilverLion, Suit: Suit.Diamond }], "The matching equipment must be truly installed before paying its removal cost.");
        paid.Slash(); paid.Activate(); paid.ColorChoice();
        var owner = paid.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Tieqi);
        var offered = paid.P!.Choices.Single(c => c.Cards.Count > 0);
        var actual = paid.V(2).Hand.Concat(paid.V(2).Equipment).Where(c => c.Suit is Suit.Heart or Suit.Diamond).Select(c => c.Id).ToArray();
        Need(owner.SlashSuitDiscard is { Stage: ProgramSlashSuitDiscardStage.Choosing, FinalSuit: Suit.Heart } &&
            offered.Cards.Count == 3 && offered.Cards.Order().SequenceEqual(actual.Order()) &&
            paid.E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Single().Suppression is { TargetSeat: 2, TurnOwnerSeat: 0 } suppression &&
            suppression.ParentFrameId == owner.Id && !paid.E<DamageAppliedEvent>().Any(),
            "The actual final judgment has already suppressed the original Slash target; its one full-color option includes both Heart and Diamond hand/equipment entities, rather than one matching suit.");
        paid.Cold(); paid.Answer(c => c.Cards.Count == 3); paid.Until(() => paid.P?.SkillPrompt?.SkillId == Watch);
        var cost = paid.E<ShanheS5TieqiPaidEvent>().Single();
        Need(cost.FrameId == owner.Id && cost.CardUseFrameId == owner.SlashSuitDiscard!.CardUseFrameId && cost.TargetSeat == 2 && cost.Count == 3 &&
            paid.G.CardMovements.Count(m => m.Reason.Value == CostReason) == 3 &&
            paid.G.CardMovements.Where(m => m.Reason.Value == CostReason).Select(m => m.From.Zone).Order()
                .SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) &&
            paid.V(2).Hp == 4 && paid.V(2).Equipment.Count == 0 &&
            paid.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == owner.Id) is
                { ShanheS5Tieqi.CardIds.Count: 3, SlashSuitDiscard.Stage: ProgramSlashSuitDiscardStage.PaymentMovement, PendingMovementContinuation: not null },
            "All three original matching entities pay one real HE discard; Silver Lion's actual removal recovers before its native child and leaves the once-paid proof on the original frame.");
        paid.Cold(); paid.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); paid.Dodge(); paid.Play();
        Need(paid.E<ShanheS5TieqiPaidEvent>().Length == 1 && paid.G.CardMovements.Count(m => m.Reason.Value == CostReason) == 3 &&
            paid.E<ProgramSlashSuitDiscardResolvedEvent>().Single() is { PaymentCompleted: true, TargetSeat: 2 } result && result.ProgramFrameId == owner.Id &&
            paid.E<CardRespondedEvent>().Single() is { ResponderSeat: 2, EffectiveCardKind: CardKind.Dodge } &&
            !paid.E<DamageAppliedEvent>().Any() && paid.V(2).Hp == 4,
            "Returning from the paid child's cold restore neither repeats payment nor strips the surviving black Dodge; that real Dodge now cancels the native Slash.");
        paid.Cold(); paid.End();
        paid.Until(() => paid.E<CurrentTurnNonLockedSkillSuppressionsExpiredEvent>().Any(e => e.TurnNumber == 1 && e.TurnOwnerSeat == 0));
        Need(paid.E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Length == 1,
            "The same actual-turn suppression expires at the ordinary turn boundary after the Slash and its children.");

        var declined = Scene.Start("declined"); declined.Play(); declined.Slash(); declined.Activate(); declined.ColorChoice(); declined.Cold();
        declined.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "shanhe-tieqi-skip");
        declined.Dodge(); declined.Play();
        Need(!declined.E<ShanheS5TieqiPaidEvent>().Any() && !declined.G.CardMovements.Any(m => m.Reason.Value == CostReason) &&
            declined.E<ProgramSlashSuitDiscardResolvedEvent>().Single() is { PaymentCompleted: false } &&
            declined.E<CardRespondedEvent>().Single() is { EffectiveCardKind: CardKind.Dodge } &&
            declined.E<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 2, Amount: 1 } && declined.V(2).Hp == 2,
            "Declining the entire cost preserves those entities; a true Dodge still cannot cancel that original Slash and the normal damage path runs.");
        declined.Cold();

        var zero = Scene.Start("zero"); zero.Play(); zero.Slash(); zero.Activate(); zero.Dodge(); zero.Cold(); zero.Play();
        Need(!zero.E<ShanheS5TieqiPaidEvent>().Any() && !zero.G.CardMovements.Any(m => m.Reason.Value == CostReason) &&
            zero.E<ProgramSlashSuitDiscardResolvedEvent>().Single() is { PaymentCompleted: false } &&
            zero.E<CardRespondedEvent>().Single() is { EffectiveCardKind: CardKind.Dodge } &&
            zero.E<DamageAppliedEvent>().Single() is { Amount: 1, TargetSeat: 2 } && zero.V(2).Hp == 2,
            "With no cards of the judgment's color, no fictitious zero-card payment clears the restriction: the actual Dodge is unable to cancel and real damage occurs.");
        zero.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.ToString() ?? "The native S5 sibling command was rejected.");
        }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, G.Revision));
        }
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded real sibling battle must remain live until its intended boundary.");
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected S5 sibling prompt: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The native sibling boundary was not reached: " + JsonSerializer.Serialize(P));
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Slash()
        {
            var action = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, [2], G.Revision, P!.PromptId, action.PlayedCardKind));
            Until(() => P?.SkillPrompt?.SkillId == Tieqi && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        }
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void ColorChoice() => Until(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "shanhe-tieqi-color") == true);
        public void Dodge()
        {
            Until(() => P is { PlayerSeat: 2, Kind: DecisionKind.RespondDodge });
            Need(V(2).Hand.Any(card => card.Kind == CardKind.Dodge && card.Suit == Suit.Spade),
                "The actual AI responder still owns its real black Dodge at the native response boundary.");
            Cold();
            Accept(new AdvanceOneStepCommand(G.Revision));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(G.CreateSnapshot(s))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Every viewer, original native Slash/judgment, private whole-color choice, paid cohort and child restores identically.");
        }
        public static Scene Start(string scenario)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5MaSiblingsPackage(), new Fixture(scenario));
            var hand = scenario == "zero" ? new[] { new ShanheStartingCard(CardKind.Dodge, Suit.Spade, 2) } :
                new[] { new ShanheStartingCard(CardKind.Slash, Suit.Heart, 7), new ShanheStartingCard(CardKind.Slash, Suit.Diamond, 8), new ShanheStartingCard(CardKind.Dodge, Suit.Spade, 2) };
            var setup = new ShanheBattleSetup(42, 1, "fixture-ma-siblings", StandardShanheS5MaSiblingsPackage.MachaoId,
                4, 0, 0, 0, 0, [], [new(CardKind.Slash, Suit.Spade, 7)], [],
                [new(Target, Hp: 3) { StartingHand = hand }, new(Peer) { StartingHand = [] }],
                [new(StandardShanheS5MaSiblingsPackage.MayunluId) { StartingHand = [] }], new string('e', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5
            }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-ma-siblings", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Quiet}}}","revision":1,"modifiers":[{"id":"far","query":"incomingDistance","operation":"add","value":2,"priority":0}],"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Armor}}}","revision":1,"triggers":[{"id":"armor","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"armor"}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"removal-recovery","window":"afterHpRecovered","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>
            {
                [Quiet] = new { name = "静默远距同桌", description = "真实距离修正及跳出牌" },
                [Armor] = new { name = "原生穿戴", description = "使用牌堆唯一装备" },
                [Watch] = new { name = "离装回复孩子", description = "真正回复后继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Watch || id == Quiet ? SkillTag.Locked : SkillTag.None });
            builder.AddGeneral(new(Peer, "真实其他敌军", "supporter", Quiet, "wei", 4));
            builder.AddGeneral(new(Target, "真实受杀目标", "supporter", Quiet, "wei", 4, scenario == "zero" ? [Watch] : [Watch, Armor]));
            builder.AddDeck(new(Deck, "空随机起手与固定红判", 0, 0, [])
            {
                PhysicalCards = new[] { new ContentDeckPhysicalCard("classic:silver-lion", Suit.Diamond, 7) }
                    .Concat(Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7))).ToArray()
            });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "完整兄妹原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5MaSiblingsPackage.MachaoId, StandardShanheS5MaSiblingsPackage.MayunluId, Peer, Target],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
