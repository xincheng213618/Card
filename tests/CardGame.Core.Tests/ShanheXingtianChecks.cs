using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheXingtianChecks
{
    private const string Axe = StandardShanheXingtianPackage.SkillId, OldAxe = "special:xingtian-axe-effect",
        Actor = "fixture:shanhe-axe-actor", Target = "fixture:shanhe-axe-target", Reactor = "fixture:shanhe-axe-reactor",
        Peer = "fixture:shanhe-axe-peer", Driver = "fixture:shanhe-axe-driver", Quiet = "fixture:shanhe-axe-quiet",
        Equip = "fixture:shanhe-axe-equip", EquipmentDodge = "fixture:shanhe-axe-equipment-dodge",
        React = "fixture:shanhe-axe-other-source", Watch = "fixture:shanhe-axe-paid-watch", Deck = "fixture:shanhe-axe-deck";

    public static void FixedHandRealEquipWeaponCostAndAllResponsesSealOtherSourceArmorUntilActualTurnEndCold()
    {
        var s = Scene.Start(); s.Play();
        var weapon = s.V(0).Hand.Single(c => c.Kind == CardKind.ShanheXingtianAxe).Id;
        var equipmentMaterial = s.V(1).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Need(s.V(0).Equipment.Count == 0 && EquipmentCatalog.Get(CardKind.ShanheXingtianAxe).WeaponAttackRange == 4 &&
            s.G.CardMovements.Any(m => m.CardId == weapon && m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)),
            "The actual S5 fixed axe is an extra hand entity with range four and must be equipped through a real card Use.");

        // Establish actual response opportunities while all three materials are still retained.
        s.Card(CardKind.Slash, [1]); s.DodgePrompt();
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([equipmentMaterial]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == EquipmentDodge) &&
            s.P.Choices.Any(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic") &&
            s.P.Choices.Any(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null),
            "The real recipient has a legal equipped-material Dodge, a zero-entity Weijing Dodge and a physical hand Dodge before the new seal.");
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic"); s.Play();
        // That real zero-entity response spends round one's quota. Cross the
        // native round boundary so every proven route is affordable for the seal.
        s.End(); s.Play();
        var sealRound = s.E<RoundStartedEvent>().Last().RoundNumber;
        s.Need(sealRound > 1 && s.V(1).Equipment.Any(c => c.Id == equipmentMaterial),
            "The next real round restores the zero-entity quota while preserving the target's equipped response material.");
        s.Card(CardKind.ShanheXingtianAxe, []); s.Play();
        s.Need(s.V(0).Equipment.Any(c => c.Id == weapon) && s.V(0).Skills!.Any(k => k.Id == Axe),
            "The original positive entity equips and supplies the equipment-owned S5 program.");
        s.Card(CardKind.IronChain, [1]); s.Offer(Axe); s.Activate();
        var handMaterial = s.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id;
        s.Pay([weapon, handMaterial]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Axe);
        var invoice = s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == parent.Id && e.ActualCount == 2);
        s.Need(parent.CardSetBindings.Single(b => b.Name == "axe-cost").CardIds.ToHashSet().SetEquals([weapon, handMaterial]) &&
            parent.CardSetBindings.Single(b => b.Name == "axe-cost").SourceLocations.Select(l => l.Zone).Order()
                .SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) &&
            s.G.CardMovements.Count(m => (m.CardId == weapon || m.CardId == handMaterial) && m.To == CardLocation.DiscardPile) == 2 &&
            s.V(0).Equipment.All(c => c.Id != weapon) && s.V(0).Skills!.All(k => k.Id != Axe) &&
            !s.E<CardTargetRestrictionGrantedEvent>().Any(e => e.Restriction.Source.SkillId == Axe) &&
            s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(),
            "Both real HE materials have been discarded once on one native invoice; the weapon grant is gone while its original paid movement child precedes the seal.");
        s.Cold(); s.Continue(); s.Play();
        var seals = s.E<CardTargetRestrictionGrantedEvent>().Where(e => e.Restriction.Source.SkillId == Axe).Select(e => e.Restriction).ToArray();
        s.Need(seals.Length == 2 && seals.All(e => e.ParentFrameId == parent.Id && e.TargetSeat == 1 && e.TurnSeat == 0) &&
            seals.Select(e => e.Restriction).ToHashSet().SetEquals([SkillProgramCardTargetRestriction.CannotUseOrPlayForTurn,
                SkillProgramCardTargetRestriction.ArmorIneffectiveForTurn]) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.InvocationId == invoice.InvocationId) == 1 &&
            s.Forbidden(1, CardKind.Slash, CardActionType.Use) && s.Forbidden(1, CardKind.Dodge, CardActionType.Response),
            "Returning from the original paid child applies both target-wide restrictions once despite having discarded the source weapon.");

        var hp = s.V(1).Hp; var responses = s.E<CardRespondedEvent>().Length;
        s.Card(CardKind.Slash, [1]); s.Play();
        s.Need(s.V(1).Hp == hp - 1 && s.E<CardRespondedEvent>().Length == responses &&
            s.V(1).Equipment.Any(c => c.Id == equipmentMaterial) && s.V(1).Hand.Any(c => c.Kind == CardKind.Dodge) &&
            !s.E<RoundOnceCardlessBasicQuotaPaidEvent>().Any(e => e.Receipt.Source.OwnerSeat == 1 && e.Receipt.RoundNumber == sealRound),
            "The real incoming Slash bypasses every previously proven response route, retaining hand/equipment materials and the unpaid zero-entity quota.");
        hp = s.V(1).Hp; s.OtherSourceDamage();
        s.Need(s.V(1).Hp == hp - 3 && s.E<DamageAppliedEvent>().Last() is { SourceSeat: 2, TargetSeat: 1, Amount: 3 },
            "Silver Lion is ineffective for another actual damage source too, rather than only the former weapon holder's cards.");
        s.Cold(); var turn = s.G.State.TurnNumber; s.End();
        s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber > turn && e.ActorSeat == 1));
        s.Need(!s.Forbidden(1, CardKind.Slash, CardActionType.Use) && !s.Forbidden(1, CardKind.Dodge, CardActionType.Response) &&
            s.E<TurnCardUseEffectsExpiredEvent>().Any(e => e.TurnNumber == turn && e.TurnSeat == 0 &&
                seals.All(seal => e.GrantSequences.Contains(seal.GrantSequence))),
            "Both issues expire at the original actual-turn boundary, before the target's own turn and without waiting for the departed weapon holder's next turn.");
        s.Play(); hp = s.V(1).Hp; s.OtherSourceDamage();
        s.Need(s.V(1).Hp == hp - 1 && s.E<DamageAppliedEvent>().Last() is { SourceSeat: 2, TargetSeat: 1, Amount: 1 },
            "After expiry the same real foreign-source three damage is capped by the retained Silver Lion again.");
        s.Card(CardKind.Slash, [1]); s.DodgePrompt();
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([equipmentMaterial])) &&
            s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic"),
            "The real equipped and zero-entity response opportunities return after the actual-turn expiry.");
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic"); s.Play();
    }

    public static void ExistingSpecialAxeStillOnlyBlocksHandAndHolderArmorBypass()
    {
        var s = Scene.Start(old: true); s.Play(); s.Card(CardKind.XingtianAxe, []); s.Play();
        s.Card(CardKind.IronChain, [1]); s.Offer(OldAxe); s.Activate();
        var hand = s.V(0).Hand.Where(c => c.Kind == CardKind.Peach).Take(2).Select(c => c.Id).ToArray();
        s.Pay(hand); s.Play();
        s.Need(!s.E<CardTargetRestrictionGrantedEvent>().Any(e => e.Restriction.Restriction == SkillProgramCardTargetRestriction.CannotUseOrPlayForTurn) &&
            s.E<HandCardColorRestrictionGrantedEvent>().Count(e => e.Restriction.AffectedSeat == 1) == 2 &&
            s.E<DirectedTurnCardPolicyGrantedEvent>().Any(e => e.Policy.ActorSeat == 0 && e.Policy.TargetSeat == 1 &&
                e.Policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreArmor)),
            "The preexisting special axe retains its narrower hand-only ban and directed holder-to-target armor policy.");
        var hp = s.V(1).Hp; s.OtherSourceDamage();
        s.Need(s.V(1).Hp == hp - 1 && s.E<DamageAppliedEvent>().Last() is { SourceSeat: 2, TargetSeat: 1, Amount: 1 },
            "The old axe leaves armor effective against another actual source.");
        var equipment = s.V(1).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Card(CardKind.Slash, [1]); s.DodgePrompt();
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([equipment]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == EquipmentDodge) &&
            s.P.Choices.Any(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic") &&
            s.P.Choices.All(c => !c.Cards.Any(id => s.V(1).Hand.Any(card => card.Id == id))),
            "The old hand-only seal still permits the genuine equipment conversion and zero-entity native Dodge while excluding the retained hand Dodge.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic"); s.Play();
        s.Need(s.E<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e => e.Receipt.Source.OwnerSeat == 1) == 1 &&
            s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 1 && e.Action.EffectiveKind == CardKind.Dodge && e.Action.PhysicalCards.Count == 0),
            "The old axe also leaves the actual zero-material Dodge Use and its once-per-round native payment intact.");
        s.Card(CardKind.Slash, [1]); s.DodgePrompt();
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([equipment])) &&
            !s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "round-once-cardless-basic"),
            "After the genuine round quota is spent, the original hand ban still retains its equipped-material native response.");
        // The ordinary physical-response human API belongs to seat zero. Let
        // this actual NPC execute its published equipment response natively.
        s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Play();
        s.Need(s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge && e.CardId == equipment) &&
            s.G.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Equipment(1) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The allowed original equipment response really pays its equipped entity through native processing and cancels the Slash.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Forbidden(int seat, CardKind kind, CardActionType action) => (bool)typeof(GameEngine)
            .GetMethod("IsCardUseForbidden", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(G, [seat, kind, action, false, null])!;
        public void Accept(GameCommand command)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected real axe command."); }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, G.Revision)); }
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void DodgePrompt() => Reach(() => P is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        public void Offer(string id) => Reach(() => P?.SkillPrompt?.SkillId == id && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Card(CardKind kind, int[] targets)
        {
            var ids = V(0).Hand.Where(c => c.Kind == kind).Select(c => c.Id).ToHashSet();
            var a = G.GetHumanLegalActions().First(a => a.CardId is { } id && ids.Contains(id) && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets) && a.Kind != LegalActionKind.Recast);
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Pay(IReadOnlyList<int> ids)
        {
            foreach (var id in ids)
            {
                Reach(() => P?.Choices.Any(c => c.Cards.SequenceEqual([id]) ||
                    c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([1])) == true);
                if (P!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"))
                {
                    Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([1]));
                    Reach(() => P?.Choices.Any(c => c.Cards.SequenceEqual([id])) == true);
                }
                Answer(c => c.Cards.SequenceEqual([id]));
            }
        }
        public void OtherSourceDamage()
        {
            Accept(new UseProgramSkillCommand(0, Driver, "poke", [], [2], G.Revision, P!.PromptId));
            Reach(() => P?.SkillPrompt?.SkillId == React && P.Choices.Any(c => c.Targets.SequenceEqual([1])));
            Answer(c => c.Targets.SequenceEqual([1])); Play();
        }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId is Axe or OldAxe && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                    Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected axe boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded actual axe flow was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold retains the original paid two-material discard, exact weapon loss, native response and actual-turn issues without repeating payment."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 4).Select(V), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool old = false)
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new StandardShanheXingtianPackage(), new Fixture()]);
            var setup = new ShanheBattleSetup(31, 1, "fixture-shanhe-axe", Actor, 8, 0, 0, 0, 3, [],
                [new(old ? CardKind.XingtianAxe : CardKind.ShanheXingtianAxe, Suit.Spade, 2), new(CardKind.IronChain, Suit.Club, 12),
                 new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Slash, Suit.Club, 8), new(CardKind.Slash, Suit.Heart, 9),
                 new(CardKind.Peach, Suit.Heart, 2), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4)], [],
                [new(Target) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 2)] }, new(Reactor) { StartingHand = [] },
                 new(Peer) { StartingHand = [] }], [], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-axe", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"poke","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{{Equip}}}","revision":1,"triggers":[{"id":"equip","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"first"},{"op":"useRandomDeckEquipment","target":"owner","targetRef":{"kind":"owner"},"resultBind":"second"}]}]},
              {"id":"{{{EquipmentDodge}}}","revision":1,"viewAs":[{"id":"equipped-dodge","inputKinds":["crossbow"],"inputSuits":[],"sourceZones":["equipment"],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
              {"id":"{{{React}}}","revision":1,"triggers":[{"id":"other-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":3}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.{{{Axe}}}.MoveBoundCards"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new[] { Quiet, Driver, Equip, EquipmentDodge, React }.ToDictionary(id => id,
                id => (object)new { name = id, description = "真实原生装备、响应与第三方伤害" });
            names[Watch] = new { name = "原生已付弃牌孩子", description = "原武器已实际弃置后继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Actor, "实际武器拥有者", "supporter", Driver, "wei", 8, [Watch]));
            b.AddGeneral(new(Target, "保留三种闪的目标", "supporter", Quiet, "shu", 12, [Equip, EquipmentDodge, "ol:weijing"]));
            b.AddGeneral(new(Reactor, "另一真实伤害来源", "supporter", Quiet, "wu", 12, [React]));
            b.AddGeneral(new(Peer, "保持战局", "supporter", Quiet, "qun", 12));
            b.AddDeck(new(Deck, "仅两件可用装备的固定库存", 0, 0, [])
            { PhysicalCards = new[] { new ContentDeckPhysicalCard("classic:silver-lion", Suit.Club, 1), new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 2) }
                .Concat(Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7))).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "S5刑天原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Actor, Target, Reactor, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
