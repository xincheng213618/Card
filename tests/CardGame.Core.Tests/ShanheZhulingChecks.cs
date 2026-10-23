using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheZhulingChecks
{
    private const string Jixian = StandardShanheS5ZhulingPackage.JixianId;
    private const string Driver = "fixture:shanhe-zhuling-driver", Gain = "fixture:shanhe-zhuling-gain";
    private const string Armor = "fixture:shanhe-zhuling-armor", Quiet = "fixture:shanhe-zhuling-quiet";
    private const string Redirect = "fixture:shanhe-zhuling-redirect", Target = "fixture:shanhe-zhuling-target";
    private const string Peer = "fixture:shanhe-zhuling-peer", Deck = "fixture:shanhe-zhuling-deck";

    public static void ThreeConditionsNativeSlashFrozenDrawAndOriginalTargetPenaltyCold()
    {
        foreach (var mode in new[] { "hit", "dodge", "redirect" })
        {
            var s = Scene.Start(mode); s.AtJixian(); var beforeHand = s.V().HandCount;
            Need(s.V(1).Equipment.Any(c => c.Kind == CardKind.BaguaFormation) && s.V(1).Hp == s.V(1).MaxHp,
                "The real target begins with an actually equipped armor and full HP.");
            s.Activate(); s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
            Need(s.P!.Choices.Any(c => c.Targets.SequenceEqual([1])) && !s.P.Choices.Any(c => c.Targets.Contains(2)),
                "The three condition filters preserve normal native Slash distance; the distance-two target is absent.");
            s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1])); s.AtGain();
            var frozen = s.E<ShanheS5JixianFrozenEvent>().Single();
            var use = s.E<ShanheS5JixianSlashIssuedEvent>().Single();
            Need(frozen is { ConditionMask: 7, X: 3, TargetSeat: 1 } && frozen.TargetSkillCount > frozen.OwnerSkillCount &&
                use.FrameId == frozen.FrameId && use.OriginalTargetSeat == 1 &&
                s.E<CardUseDeclaredEvent>().Single(e => e.ResolutionId == use.UseId) is { CardId: 0, CardKind: CardKind.Slash, SourceSeat: 0 } &&
                s.E<ShanheS5JixianDrawPaidEvent>().Single() is { Requested: 3, Actual: 3 } paid && paid.FrameId == frozen.FrameId && paid.UseId == use.UseId &&
                s.V().HandCount == beforeHand + 3 && s.V().Hp == 4 && !s.E<ShanheS5JixianResolvedEvent>().Any(),
                "Jixian freezes all three true conditions, issues one real zero-entity native Slash and pays exactly Draw3 before its gained-card child and penalty return.");
            var damages = s.E<DamageAppliedEvent>().Where(e => e.SourceCardUseResolutionId == use.UseId).ToArray();
            Need(mode switch
            {
                "hit" => damages is [{ TargetSeat: 1, Amount: 2 }] && s.V(1).Hp < s.V(1).MaxHp,
                "dodge" => damages.Length == 0 && s.G.CardMovements.Any(m => m.From == CardLocation.Hand(1) && m.Reason == CardMoveReasons.Respond),
                _ => damages is [{ TargetSeat: 2, Amount: 2 }] && s.V(1).Hp == s.V(1).MaxHp
            }, "The fixed native branch really hits the original target, pays a physical Dodge, or redirects and damages only a different target.");
            Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frozen.FrameId).InstructionIndex == 4,
                "The paid draw child still belongs to the original Jixian instruction.");
            s.Cold(); s.Continue(); s.Play();
            Need(s.E<ShanheS5JixianResolvedEvent>().Single() == new ShanheS5JixianResolvedEvent(frozen.FrameId, use.UseId, 1,
                    mode == "hit", mode != "hit") && s.V().Hp == (mode == "hit" ? 4 : 3) &&
                s.E<ShanheS5JixianDrawPaidEvent>().Length == 1 && s.V().HandCount == beforeHand + 3,
                "Actual damage to that original target alone waives the HP penalty; redirected damage elsewhere does not, and the returned draw child never recomputes X or pays twice.");
            s.Cold();
        }
    }

    public static void BaoliNativeAndCardlessSourceDamageExcludeSourceLessAndHpLossAndNpcUsesJixian()
    {
        VerifySourceModifierContract();
        var s = Scene.Start("hit"); s.AtJixian(); s.Skip(); s.Play();
        s.Use("hit", [1]);
        Need(s.E<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 1, Amount: 2, SourceCardUseResolutionId: null, SourceLess: false },
            "Baoli adds one to actual cardless damage whose real source owns the skill.");
        s.BeginSlash(3); s.Play();
        Need(s.E<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 3, Amount: 2, SourceCardUseResolutionId: not null },
            "The same broad source modifier adds one to an actual physical native Slash.");
        s.Use("source-less");
        Need(s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 1, SourceLess: true } && s.V().Hp == 3,
            "Actual source-less damage cannot borrow the victim's Baoli as a source.");
        var damageCount = s.E<DamageAppliedEvent>().Length; s.Use("lose");
        Need(s.V().Hp == 2 && s.E<DamageAppliedEvent>().Length == damageCount,
            "A real direct HP loss remains exactly one and creates no damage fact.");
        s.Cold();

        var npc = Scene.Start("hit", npc: true);
        npc.NpcUntil(() => npc.E<ShanheS5JixianResolvedEvent>().Length > 0);
        var frozen = npc.E<ShanheS5JixianFrozenEvent>().Single();
        Need(!npc.V().IsHuman && npc.E<ProgramBindingResolvedEvent>().Single(e => e.SkillId == Jixian) is { Activated: true, Completed: true } &&
            npc.E<ShanheS5JixianSlashIssuedEvent>().Single().FrameId == frozen.FrameId &&
            npc.E<ShanheS5JixianDrawPaidEvent>().Single().Requested == frozen.X,
            "The actual NPC autonomously activates Jixian, selects a legal target, issues its native Slash and settles its own frozen-X draw.");
        npc.Cold();
    }

    private static void VerifySourceModifierContract()
    {
        var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"fixture:baoli-contract","revision":1,"damageModifiers":[
                {"id":"source","cardKinds":[],"amount":1,"condition":"always","sourceScope":"damageSource"}]}]}
            """)!;
        var presentation = JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object>
            {
                ["fixture:baoli-contract"] = new { name = "来源加伤", description = "所有来源伤害" }
            }
        });
        foreach (var mutate in new Action<JsonObject>[]
        {
            modifier => modifier["cardKinds"] = JsonNode.Parse("""["slash"]"""),
            modifier => modifier["damageLayer"] = "cardDamage",
            modifier => modifier["capsToAmount"] = true
        })
        {
            var changed = rules.DeepClone();
            mutate(changed["skills"]![0]!["damageModifiers"]![0]!.AsObject());
            var rejected = false;
            try { SkillProgramCatalog.Load(changed.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Unconditional source damage cannot narrow its card kinds, masquerade as card damage, or cap received damage.");
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "The real Zhuling command was rejected."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.Single(predicate).Id, G.Revision)); }
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void AtJixian() => Until(() => P?.SkillPrompt?.SkillId == Jixian);
        public void AtGain() => Until(() => P?.SkillPrompt?.SkillId == Gain);
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string id, int[]? targets = null)
        { Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId)); Play(); }
        public void BeginSlash(int target)
        {
            var card = V().Hand.First(c => c.Kind == CardKind.Slash);
            var action = G.GetHumanLegalActions().Single(a => a.CardId == card.Id && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, card.Id, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind,
                action.TargetCardId) { ConversionSource = action.ConversionSource });
        }
        public void Until(Func<bool> done)
        {
            for (var n = 0; n < 140; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small Zhuling fixture must keep its target alive.");
                if (P?.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Zhuling boundary: " + JsonSerializer.Serialize(new { P.Kind, P.Prompt, Skill = P.SkillPrompt?.SkillId }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Zhuling continuation was not reached.");
        }
        public void NpcUntil(Func<bool> done)
        {
            for (var n = 0; n < 100; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "NPC Jixian did not activate: " + JsonSerializer.Serialize(E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Jixian)));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("NPC Jixian did not settle its native use.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves Jixian's original target, exact use identity, frozen X and already-paid native draw children.");
        }
        public static Scene Start(string mode, bool npc = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5ZhulingPackage(), new Fixture(mode));
            var setup = new ShanheBattleSetup(73, 1, "fixture-zhuling", StandardShanheS5ZhulingPackage.ZhulingId, 4, 0, 0, 0, 0,
                [Driver, Gain], [new(CardKind.Slash)], [],
                [new(Target, MaxHpBonus: 8) { StartingHand = mode == "dodge" ? [new(CardKind.Dodge)] : [] },
                 new(Peer, MaxHpBonus: 8) { StartingHand = [] }, new(Peer, MaxHpBonus: 8) { StartingHand = [] }], [], new string('a', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = npc ? -1 : 0, HumanTeamId = npc ? null : ShanheBattleSetup.PlayerTeamId,
                HumanRole = null, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = npc ? 1 : 8
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-zhuling", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[
                {"id":"hit","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"sourceRef":{"kind":"owner"}}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"paid-jixian-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["shanhe.s5.jixian.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Armor}}}","revision":1,"triggers":[{"id":"armor","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"armor"}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Redirect}}}","revision":1,"triggers":[{"id":"redirect","window":"slashTargetRedirecting","ownerRelation":"target","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"slashRedirectable"},{"op":"redirectCurrentAttack","target":"selectedTarget"}]}]}
            ]}
            """;
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实伤害驱动", description = "来源伤害、无来源与失去体力" },
                    [Gain] = new { name = "已付摸牌孩子", description = "急陷真实摸牌后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Armor] = new { name = "真实装备", description = "从牌堆使用唯一防具" },
                    [Quiet] = new { name = "静默", description = "跳过出牌" },
                    [Redirect] = new { name = "真实转移", description = "转移此原生杀" }
                }
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var passives = Enumerable.Range(0, 8).Select(i => "fixture:zhuling-passive-" + i).ToArray();
            foreach (var id in passives) b.AddSkill(new(id, "既有技能", "条件数量见证"));
            b.AddGeneral(new(Target, "三条件目标", "supporter", Armor, "qun", 4,
                new[] { Quiet }.Concat(passives).Concat(mode == "redirect" ? [Redirect] : Array.Empty<string>()).ToArray()));
            b.AddGeneral(new(Peer, "转移受害者", "supporter", Quiet, "qun", 4));
            b.AddDeck(new(Deck, "黑色判定固定牌堆", 0, 2, [])
            {
                PhysicalCards = new[] { new ContentDeckPhysicalCard("standard:bagua", Suit.Spade, 7) }
                    .Concat(Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7))).ToArray()
            });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "急陷原生流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5ZhulingPackage.ZhulingId, Target, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
