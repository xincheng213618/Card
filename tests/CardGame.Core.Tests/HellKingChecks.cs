using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HellKingChecks
{
    private const string Driver = "fixture:hell-king-driver", Quiet = "fixture:hell-king-quiet", Mode = "fixture:hell-king-mode";

    public static void NativePreparationDrawRandomAssaultAndRepeatedDamage()
    {
        {
            var (g, registry) = Start(["hell:modao", "hell:wangsheng"], [], card: "standard:peach");
            var roll = E<HellKingChoiceRecordedEvent>(g).Single(e => e.ResultBind == "assault");
            var kind = roll.OptionId == "nanman" ? CardKind.BarbarianAssault : CardKind.ArrowBarrage;
            var declaration = E<CardUseDeclaredEvent>(g).Single(e => e.SourceSeat == 0 && e.CardId == 0 && e.CardKind == kind);
            Require(g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("DrawHellPreparation", StringComparison.Ordinal)) == 2 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == declaration.ResolutionId) == 1 &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.Amount == 1) == 3,
                "Modao performs two native preparation draws; Wangsheng rolls once, issues one real zero-entity assault and resolves all three actual targets once.");
            Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:fanshi"]); var boss = Peer(g);
            for (var i = 0; i < 3; i++) { Use(g, "one-damage", boss); Play(g); Cold(g, registry); }
            var counted = E<HellFanshiDamageCountedEvent>(g).Where(e => e.OwnerSeat == boss).ToArray();
            var choices = E<HellKingChoiceRecordedEvent>(g).Where(e => e.OwnerSeat == boss && e.ResultBind == "victim").ToArray();
            Require(counted.Length == 3 && counted.Select(e => e.DamageFrameId).Distinct().Count() == 3 && counted.Select(e => e.ActualTurn).Distinct().Count() == 1 &&
                choices.Length == 3 && choices[0].OptionId == "none" && choices.Skip(1).All(e => e.OptionId == "eligible" && e.TargetSeat != boss) &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == boss && e.Amount == 1) == 2,
                "Fanshi counts distinct native damage instances in the same actual turn, skips only the first and creates two attributed native random damage children.");
            End(g); Play(g); var before = E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == boss);
            Use(g, "damage", boss); Play(g);
            Require(E<HellFanshiDamageCountedEvent>(g).Last().ActualTurn != counted[0].ActualTurn && E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == boss) == before,
                "A later actual turn resets Fanshi; a two-point first damage still counts as one occurrence."); Cold(g, registry);
        }
    }

    public static void ZhiwangUsesRealGainBatchesAndExcludesDrawPhases()
    {
        var (g, registry) = Start([], ["hell:zhiwang"]); var boss = Peer(g);
        Require(!E<HellRandomCardDiscardedEvent>(g).Any(), "Normal draw-phase hand gains do not trigger Zhiwang.");
        var hand = V(g, 0).HandCount; var moves = g.CardMovements.Count;
        Use(g, "draw-two"); Play(g);
        var receipt = E<HellRandomCardDiscardedEvent>(g).Single();
        Require(receipt.OwnerSeat == boss && receipt.CardOwnerSeat == 0 && V(g, 0).HandCount == hand + 1 &&
            g.CardMovements.Skip(moves).Count(m => m.To == CardLocation.Hand(0)) == 2 &&
            g.CardMovements.Skip(moves).Count(m => m.CardId == receipt.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
            E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e => e.InitiatorSeat == boss && e.SubjectSeat == 0 && e.ActualCount == 1),
            "One real two-card hand-gain batch outside Draw causes one true attributed random hand discard, rather than one discard per gained card."); Cold(g, registry);
        var recipient = Enumerable.Range(1, 3).First(s => s != boss); var recipientHand = V(g, recipient).HandCount;
        Use(g, "give", recipient); Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        var card = V(g, 0).Hand[0].Id; Answer(g, c => c.Cards.SequenceEqual([card])); Play(g);
        Require(V(g, recipient).HandCount == recipientHand && E<HellRandomCardDiscardedEvent>(g).Count() == 2 && E<HellRandomCardDiscardedEvent>(g).Last().CardOwnerSeat == recipient,
            "A genuine hand transfer qualifies its actual gainer as a separate batch, including another character's turn."); Cold(g, registry);
        var count = E<HellRandomCardDiscardedEvent>(g).Count(); End(g); Play(g);
        Require(E<HellRandomCardDiscardedEvent>(g).Count() == count, "The following native normal draw phases remain excluded after the outside-draw gain receipts."); Cold(g, registry);
    }

    public static void BitterWinePeachPoisonAndRecoverySourceStayNative()
    {
        {
            var (g, registry) = Start(["hell:kujiu"], []); End(g);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "hell:kujiu");
            var actor = P(g)!.ValidTargetSeats.Single();
            Require(actor != 0 && P(g)!.Choices.Count == 1, "Bitter Wine's true preparation context exposes exactly the other current actor as its mandatory target.");
            Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([actor]));
            Until(g, () => E<CardUseFinishedEvent>(g).Any(e => e.CardKind == CardKind.Alcohol));
            var wine = E<CardUseDeclaredEvent>(g).Single(e => e.CardKind == CardKind.Alcohol);
            Require(wine.CardId == 0 && wine.SourceSeat != 0 && E<ProgramSkillHpLostEvent>(g).Single(e => e.SkillId == "hell:kujiu") is { Amount: 1 } loss &&
                loss.TargetSeat == wine.SourceSeat && E<AlcoholAppliedEvent>(g).Single().SourceSeat == wine.SourceSeat &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == wine.ResolutionId) == 1,
                "Bitter Wine first resolves actual HP loss, then the affected preparation actor performs one zero-entity native Alcohol Use and typed completion."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:fudu"], card: "standard:peach"); var boss = Peer(g);
            Use(g, "lose-one", 0); Play(g); var before = g.CreateSnapshot(0).Players.ToDictionary(p => p.Seat, p => p.Hp);
            Physical(g, CardKind.Peach, 0); Play(g);
            var victim = E<HellKingChoiceRecordedEvent>(g).Single(e => e.OwnerSeat == boss && e.ResultBind == "victim").TargetSeat!.Value;
            Require(victim != boss && victim != 0 && V(g, victim).Hp == before[victim] - 1 && V(g, 0).Hp == before[0] + 1 &&
                E<ProgramSkillHpLostEvent>(g).Single(e => e.SkillId == "hell:fudu") is { Amount: 1 } loss && loss.TargetSeat == victim,
                "A real paid Peach triggers poison on a random distinct third participant; the user and poison owner are excluded and the Peach still recovers normally."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:enyuan"]); var boss = Peer(g);
            Use(g, "damage", boss); Play(g); var hand = V(g, 0).HandCount;
            Use(g, "recover", boss); Play(g);
            Require(V(g, 0).HandCount == hand + 2 && g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value.Contains("RewardHellRecoverySource", StringComparison.Ordinal)) == 2,
                "A real two-point recovery gives its actual other source two separate native one-card draws; Enyuan's heart-card damage branch remains the existing native request."); Cold(g, registry);
        }
    }

    public static void DeathEffectsRecalculateExtremeHpAndPreserveNativeSubtrees()
    {
        {
            var (g, registry) = Start(["classic:wuhun"], ["hell:huoxing"]); var boss = Peer(g);
            Use(g, "peer-kill-owner", boss);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "classic:wuhun" && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([boss])));
            Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([boss]));
            Until(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == "hell:huoxing" && e.Activated && e.Completed));
            Require(!V(g, 0).IsAlive && !V(g, boss).IsAlive &&
                E<ProgramSkillCauseDeathDeclaredEvent>(g).Single() is { SkillId: "classic:wuhun", SourceSeat: 0 } direct && direct.TargetSeat == boss &&
                E<JudgmentResolvedEvent>(g).Single(e => e.Reason == "skill.wuhun.death") is { CardKind: CardKind.Slash } &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == boss && e.Amount == 1 && e.Nature == DamageNature.Fire && e.TargetSeat != 0 && e.TargetSeat != boss) == 2,
                $"A true attributed lethal hit invokes Wuhun's real nightmare target and non-Peach judgment, directly kills the king, then completes both Huoxing fire-damage children while retaining the original death tree. Boss={boss}; alive={V(g, 0).IsAlive}/{V(g, boss).IsAlive}; direct={JsonSerializer.Serialize(E<ProgramSkillCauseDeathDeclaredEvent>(g))}; damage={JsonSerializer.Serialize(E<DamageAppliedEvent>(g))}."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:pingdeng"]); var boss = Peer(g);
            Use(g, "kill", boss); Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "hell-king-extreme") == true); Cold(g, registry);
            Until(g, () => E<HellKingChoiceRecordedEvent>(g).Any(e => e.OwnerSeat == boss && e.ResultBind == "second-victim"));
            Play(g);
            var first = E<HellKingChoiceRecordedEvent>(g).Single(e => e.OwnerSeat == boss && e.ResultBind == "first-victim");
            var second = E<HellKingChoiceRecordedEvent>(g).Single(e => e.OwnerSeat == boss && e.ResultBind == "second-victim");
            var damage = E<DamageAppliedEvent>(g).Where(e => e.SourceSeat == boss).ToArray();
            Require(!V(g, boss).IsAlive && first.TargetSeat != second.TargetSeat && damage.Length == 2 && damage[0].TargetSeat == first.TargetSeat && damage[0].Amount == 2 &&
                damage[1].TargetSeat == second.TargetSeat && damage[1].Amount == 1 && damage.All(e => e.Nature is DamageNature.Fire or DamageNature.Thunder),
                "Pingdeng keeps the true dead skill source, completes the first native damage subtree, then recalculates the highest living HP before its independent second random element."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:wangxiang"], equipAtStart: true); var boss = Peer(g);
            var originals = g.CreateSnapshot(0).Players.Where(p => p.Seat != boss).SelectMany(p => p.Equipment.Select(c => (Seat: p.Seat, c.Id))).ToArray();
            Require(originals.Length == 3, "Every surviving fixture participant has a real original equipped card.");
            Use(g, "kill", boss); Play(g);
            Require(originals.All(item => g.CardMovements.Any(m => m.CardId == item.Id && m.From == CardLocation.Equipment(item.Seat) && m.To == CardLocation.DiscardPile)) &&
                g.CreateSnapshot(0).Players.Where(p => p.Seat != boss).All(p => p.Equipment.Count == 0),
                "Wangxiang's death discards every other participant's actual equipment through native movements."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:bingfeng"]); var boss = Peer(g);
            Use(g, "kill", boss); Play(g);
            Require(V(g, 0).IsFaceDown && E<CharacterStateChangedEvent>(g).Any(e => e.Change.TargetSeat == 0 && e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver),
                "Icebound identifies the real killer from the death context and turns that originally face-up character over natively."); Cold(g, registry);
        }
    }

    public static void VirtualArmorNativeSlashAndReincarnationSetting()
    {
        {
            var (g, registry) = Start([], ["hell:tiemian"], suit: Suit.Spade); var boss = Peer(g); var hp = V(g, boss).Hp;
            var card = Physical(g, CardKind.Slash, boss); Play(g);
            Require(V(g, boss).Hp == hp && E<ArmorEffectAppliedEvent>(g).Any(e => e.ArmorCard == CardKind.RenwangShield) &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "Unarmored Tiemian supplies a genuine Renwang barrier against a real paid black Slash, with exactly one material discard."); Cold(g, registry);
        }
        {
            var (g, registry) = Start(["hell:zhadao"], ["hell:tiemian"], suit: Suit.Spade); var boss = Peer(g); var hp = V(g, boss).Hp;
            Physical(g, CardKind.Slash, boss); Play(g);
            Require(V(g, boss).Hp == hp - 1, "Zhadao's real Slash bypasses the native virtual shield, as Qinggang does."); Cold(g, registry);
        }
        {
            var (g, registry) = Start([], ["hell:remen"]); var boss = Peer(g); var hp = V(g, boss).Hp;
            Physical(g, CardKind.Slash, boss); Play(g); Require(V(g, boss).Hp == hp, "Unarmored Remen makes a real ordinary Slash ineffective.");
            Use(g, "fire", boss); Play(g); Require(V(g, boss).Hp == hp - 2, "Remen does not acquire Tengjia's additional fire vulnerability."); Cold(g, registry);
        }
        {
            var (g, registry) = Start(["hell:lunhui"], []); Use(g, "lose-ten", 0); Play(g); End(g);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == "hell:lunhui"); Cold(g, registry);
            Answer(g, c => c.Targets.Count == 1); Play(g);
            var swap = E<HellCurrentHpExchangedEvent>(g).Single();
            Require(swap.OwnerBefore == 2 && swap.OtherBefore == 12 && swap.OwnerAfter == 12 && swap.OtherAfter == 2 &&
                V(g, 0).Hp == 12 && V(g, swap.OtherSeat).Hp == 2 && !E<RecoveryAppliedEvent>(g).Any(e => e.Amount == 10) &&
                !E<ProgramSkillHpLostEvent>(g).Any(e => e.SkillId == "hell:lunhui"),
                "Reincarnation exchanges current HP through Setting facts without pretending the exchange is a recovery or HP loss."); Cold(g, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(string[] ownerSkills, string[] peerSkills, string card = "standard:slash", Suit suit = Suit.Heart, bool equipAtStart = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardGhostActivityPackage(), new StandardHellActivityPackage(),
            new Fixture(ownerSkills, peerSkills, card, suit, equipAtStart));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:blue", ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:hell-king-actor", g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private static void Use(GameEngine g, string id, int? target = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    private static int Physical(GameEngine g, CardKind kind, int target)
    {
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null &&
            (a.TargetSeats.SequenceEqual([target]) || kind == CardKind.Peach && target == 0 && a.TargetSeats.Count == 0));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)); return action.CardId.Value;
    }
    private static int Peer(GameEngine g) => g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:hell-king-peer-1").Seat;
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Until(GameEngine g, Func<bool> condition)
    {
        for (var i = 0; i < 250; i++)
        {
            if (condition()) return;
            if (P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (P(g) is { PlayerSeat: 0 } card && card.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "hand-repayment")) Answer(g, c => c.Parameters.GetValueOrDefault("repayment") == "give");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Hell king fixture missed the actual boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(restored.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements), "All private views, random invoices and native material movements reproduce from accepted commands.");
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Hell command rejected."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(string[] ownerSkills, string[] peerSkills, string card, Suit suit, bool equipAtStart) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("hell-king-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var equipment = equipAtStart ? ",{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"owner\"},\"resultBind\":\"initial-equipment\"}" : "";
            var growth = "{\"id\":\"native-health\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"changeMaximumHp\",\"target\":\"owner\",\"amount\":8},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":8}" + equipment + "]}";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"cardPolicies":[{"id":"fixture-distance","kind":"ignoreUseDistance","cardKinds":["slash","fireSlash","thunderSlash"]}],"triggers":[{{{growth}}}],"activations":[
                {"id":"one-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2,"nature":"fire"}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"peer-kill-owner","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":20}]},
                {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"lose-ten","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":10}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]},
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                {"id":"give","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true}]}
              ]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{{{growth}}},{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "Small actual Hell king rule fixture." }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Small actual Hell king rule fixture.") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new("fixture:hell-king-actor", "Hell king actor", "", Driver, "qun", BaseHp: 12, AdditionalSkillIds: ownerSkills));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:hell-king-peer-{i}", $"Hell king peer {i}", "", Quiet, "qun", BaseHp: 12, AdditionalSkillIds: i == 1 ? peerSkills : []));
            b.AddDeck(new("fixture:hell-king-deck", "Real uniform materials", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(i < 160 ? card : "standard:crossbow", suit, 7)).ToArray() });
            b.AddMode(new(Mode, "Actual Hell king fixture", 4, 4, new Dictionary<string, int>(), "fixture:hell-king-deck", 4,
                ["fixture:hell-king-actor", "fixture:hell-king-peer-1", "fixture:hell-king-peer-2", "fixture:hell-king-peer-3"], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:blue"] = 2, ["team:red"] = 2 }));
        }
    }
}
