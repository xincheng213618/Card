using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class OlMoZhangFeiChecks
{
    private const string Hero = "ol:mo-zhang-fei", Driver = "fixture:mzf-driver", Probe = "fixture:mzf-probe",
        Boost = "fixture:mzf-boost", Enemy = "fixture:mzf-enemy", Other = "fixture:mzf-other", Ally = "fixture:mzf-ally",
        Deck = "fixture:mzf-deck";
    private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Spade) => new(kind, suit, 7);

    public static void MandatoryIdentityMixedNativeTargetsAndCounterspellCold()
    {
        var s = Scene.Start(false);
        s.Need(s.E<MoZhangFeiColorArrivedEvent>().Length == 0, "Initial assigned HP does not consume a first color arrival.");
        var crossbow = s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Use(s.Action(a => a.CardId == crossbow)); s.Play();
        var dodge = s.V().Hand.First(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Club);
        var mixed = s.Action(a => a.CardId == dodge.Id && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1, 3]));
        s.Need(!s.Actions().Any(a => a.CardId == dodge.Id && a.PlayedCardKind == CardKind.Dodge), "Own-turn Dodge has mandatory Slash identity.");
        s.Use(mixed);
        s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single();
        s.Need(use.CardKind == CardKind.Slash && use.TargetSeats.SequenceEqual([1, 3]) &&
            s.E<NativeSlashTargetDuelStartedEvent>().Any(e => e.UseId == use.Id && e.TargetSeat == 3) &&
            s.E<DuelResponseEvent>().Any(e => e.ResolutionId == use.Id && e.UsedSlash),
            "The same-color enemy really answers Duel on the original paid mixed Slash after the different-color extra target.");
        s.Cold();
        var responseDodge = s.V().Hand.First(c => c.Kind == CardKind.Dodge);
        s.Need(s.P!.Choices.Any(c => c.Cards.Contains(responseDodge.Id)), "Mandatory own-turn Dodge identity also supplies the genuine native Duel Slash response.");
        s.Answer(c => c.Cards.Contains(responseDodge.Id)); s.Play();
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardKind == CardKind.Slash) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == dodge.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1) &&
            s.E<MoZhangFeiDrawReturnedEvent>().Any(e => e.Outcome.RequestedCount == 1),
            "One paid Use drains its different-color extra Slash target, original color draw and native Duel slice without another declaration or material payment.");
        s.Drive("recover", 3); s.Play();
        var blackSlash = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        var slash = s.Action(a => a.CardId == blackSlash && a.TargetSeats.SequenceEqual([3]));
        var damageBeforeCounterspell = s.E<DamageAppliedEvent>().Length;
        s.Use(slash); s.Reach(() => s.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }); s.Cold();
        var second = s.G.ResolutionStack.OfType<CardUseFrame>().Single().Id;
        s.Answer(c => c.Cards.Any(id => s.V().Hand.Any(card => card.Id == id && card.Kind == CardKind.Nullification)));
        s.Play();
        s.Need(s.E<NativeSlashTargetDuelCanceledEvent>().Count(e => e.UseId == second) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == second) == 1 &&
            s.E<DamageAppliedEvent>().Length == damageBeforeCounterspell,
            "A real Nullification cancels only the current Duel slice and returns the original paid native Use exactly once.");

        var spear = Scene.Start(false, spear: true);
        var weapon = spear.V().Hand.Single(c => c.Kind == CardKind.ZhangbaSerpentSpear).Id;
        spear.Use(spear.Action(a => a.CardId == weapon)); spear.Play();
        var pair = spear.V().Hand.Where(c => c.Kind is CardKind.Crossbow or CardKind.Nullification).Select(c => c.Id).ToArray();
        spear.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [1, 3], spear.G.Revision, spear.P!.PromptId));
        spear.Reach(() => spear.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 }); spear.Cold(); spear.Play();
        var grouped = spear.E<CardActionAcceptedEvent>().Single(e => e.Action.Type == CardActionType.Use && e.Action.EffectiveKind == CardKind.Slash).Action;
        var groupedUse = spear.E<NativeSlashTargetDuelStartedEvent>().Single().UseId;
        spear.Need(grouped.TargetSeats.SequenceEqual([1, 3]) && grouped.PhysicalCards.Select(c => c.CardId).SequenceEqual(pair) &&
            grouped.EffectiveSuit is null or Suit.None && grouped.EffectiveIsRed == false &&
            pair.All(id => spear.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1) &&
            spear.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == groupedUse) == 1 &&
            spear.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == groupedUse && e.CardKind == CardKind.Slash) == 1 &&
            spear.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1) &&
            spear.E<DuelResponseEvent>().Any(e => e.ResolutionId == groupedUse && e.ResponderSeat == 3 && e.UsedSlash) &&
            !spear.Actions().Any(a => a.Kind is LegalActionKind.Slash or LegalActionKind.UseEquipmentEffect),
            "A real Spade/Club black Zhangba pair pays both entities once, retains its color without a single suit, settles mixed Slash/Duel on one Use and consumes the native Slash quota.");
    }

    public static void NativeThreeDamageModificationColorsAndIssuedGreenCold()
    {
        var s = Scene.Start(true);
        var slash = s.Action(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        s.Use(slash); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 3) &&
            s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 1) && s.V().Hp == 5 &&
            s.E<MoZhangFeiChenshiEnteredEvent>().Length == 1 && s.E<OlMoRumoEnteredEvent>().Length == 1 &&
            s.V().Skills!.Any(skill => skill.Id == MoZhangFeiRuntime.ModifiedZhuohun) &&
            !s.V().Skills!.Any(skill => skill.Id == MoZhangFeiRuntime.Zhuohun),
            "One actual three-point Slash/Duel occurrence recovers, modifies Zhuohun and enters the existing native Rumo exactly once.");
        s.Drive("lose-three", 1); s.Play();
        s.Need(s.E<MoZhangFeiColorEffectIssuedEvent>().Any(e => e.TargetSeat == 1 && e.Color == MoZhangFeiColor.Yellow) &&
            s.E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Any(e => e.Suppression.TargetSeat == 1) &&
            s.E<MoZhangFeiDrawReturnedEvent>().Any(e => e.Outcome.RequestedCount > 0) && s.V().HandCount >= s.V().MaxHp,
            "Modified yellow first fills the owner's hand, then issues the native current-turn nonlocked suppression.");
        s.Drive("recover", 1); s.Play();
        var grant = s.E<MoZhangFeiColorEffectIssuedEvent>().Single(e => e.TargetSeat == 1 && e.Color == MoZhangFeiColor.Green);
        s.Cold(); s.Drive("draw-two", 1); s.Play();
        var replaced = s.E<MoZhangFeiGreenReplacementEvent>().Single();
        s.Need(replaced.GrantFrameId == grant.FrameId && replaced.Requested == 2 && replaced.Count == 2 &&
            s.E<MoZhangFeiGreenMaterialEvent>().Where(e => e.FrameId == replaced.FrameId).All(e =>
                e.From == CardLocation.DrawPile && e.To == CardLocation.Hand(1) && e.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
            !s.G.CardMovements.Any(m => m.Sequence > replaced.Before && m.Sequence <= replaced.After && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program." + Driver + ".Draw"),
            "The restored issued green entitlement obtains real DrawPile Slashes instead of drawing, on the exact original request.");
        s.Drive("lose-three", 3); s.Play();
        s.Need(!s.V(3).IsAlive && s.E<MoZhangFeiColorEffectIssuedEvent>().Any(e => e.TargetSeat == 3 && e.Color == MoZhangFeiColor.Red),
            "A new red arrival with no dying character uses native HP loss to remove the remaining HP and drain genuine dying/death.");
        s.Drive("lose-three", 0); s.Play();
        s.Drive("grant-probe"); s.Play();
        s.Need(s.E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Any(e => e.Suppression.TargetSeat == 0) &&
            s.E<SkillsAcquiredEvent>().Any(e => e.PlayerSeat == 0 && e.SkillIds.Contains(Probe)) && !s.Actions().Any(a => a.ProgramSkillId == Probe) &&
            s.Actions().Any(a => a.ProgramSkillId == Driver),
            "Yellow includes its own owner and a future acquired nonlocked grant, while locked fixture/native skills remain qualified.");
        s.Drive("lose-zhuohun"); s.Play(); s.Cold();
        s.Drive("draw-two", 1); s.Play();
        s.Need(s.E<MoZhangFeiGreenReplacementEvent>().Count(e => e.GrantFrameId == grant.FrameId) == 2 &&
            s.E<MoZhangFeiChenshiEnteredEvent>().Length == 1 && !s.V().Skills!.Any(skill => skill.Id == MoZhangFeiRuntime.ModifiedZhuohun),
            "Losing the original modified skill does not revoke an already issued green entitlement or repeat entry/payment after cold restoration.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mo-zhang-fei", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-mo-sun-quan." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var shared = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            b.AddSkill(new("ol:mo-rumo", "入魔", "共享入魔") { Program = shared.Programs["ol:mo-rumo"], ProgramPresentation = shared.Presentations["ol:mo-rumo"], Tags = SkillTag.Locked });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"lose-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":3}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]},
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
                {"id":"grant-probe","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Probe}}"]}]},
                {"id":"lose-zhuohun","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:mo-zhuohun-modified"],"sourceBind":"standard:none"}]}]},
                {"id":"{{Probe}}","revision":1,"activations":[{"id":"probe","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"{{Boost}}","revision":1,"damageModifiers":[{"id":"real-three-native","cardKinds":["slash","duel"],"amount":2,"condition":"always","sourceScope":"ownerUsed"}]}]}
                """;
            var labels = new[] { Driver, Probe, Boost }.ToDictionary(id => id, id => (object)new { name = id, description = "固定真实原生驱动" });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Probe ? SkillTag.None : SkillTag.Locked });
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Nullification, CardKind.Crossbow, CardKind.ZhangbaSerpentSpear })
            { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mzf-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            foreach (var peer in new[] { Enemy, Other, Ally }) b.AddGeneral(new(peer, peer, "", "standard:none", "wei", 8));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40)
                .Select(i => new ContentDeckPhysicalCard("fixture:mzf-card-" + (i % 2 == 0 ? CardKind.Slash : CardKind.Dodge), Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "魔张飞真实小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Enemy, Other, Ally], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(GameEngine engine, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = engine;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public IReadOnlyList<LegalAction> Actions() => G.GetHumanLegalActions();
        public LegalAction Action(Func<LegalAction, bool> predicate) => Actions().FirstOrDefault(predicate) ??
            throw new InvalidOperationException("Missing native action: " + JsonSerializer.Serialize(new { Hand = V().Hand, Actions = Actions(), P }));
        public void Use(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Drive(string activation, params int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> goal, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (goal()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal at " + line);
                if (P is not { PlayerSeat: 0 }) { Accept(new AdvanceOneStepCommand(G.Revision)); continue; }
                if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not "bagua");
                else throw new InvalidOperationException("Unexpected native prompt at " + line + ": " + JsonSerializer.Serialize(new
                { P.Kind, P.PlayerSeat, Hp = Enumerable.Range(0, 4).Select(i => V(i).Hp), Hands = Enumerable.Range(0, 4).Select(i => V(i).Hand),
                    Issued = E<NativeSlashTargetDuelIssuedEvent>(), Started = E<NativeSlashTargetDuelStartedEvent>(),
                    Responses = E<DuelResponseEvent>(), Damage = E<DamageAppliedEvent>(),
                    Uses = E<CardActionAcceptedEvent>().Select(e => e.Action) }));
            }
            throw new InvalidOperationException("Bounded native flow not reached at " + line + ": " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var movements = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(Enumerable.Range(0, 4).All(i => views[i] == SnapshotJson.Serialize(G.CreateSnapshot(i))) &&
                movements == JsonSerializer.Serialize(G.CardMovements), "The original native prompt, material payments and issued color entitlement survive cold restoration.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Message); }
        public void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, G.State.Winner, P,
                Hp = Enumerable.Range(0, 4).Select(i => V(i).Hp), Frames = G.ResolutionStack.Select(f => f.GetType().Name) }));
        }
        public static Scene Start(bool boosted, bool spear = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var hand = spear ? new[] { C(CardKind.ZhangbaSerpentSpear), C(CardKind.Crossbow), C(CardKind.Nullification, Suit.Club), C(CardKind.Slash, Suit.Heart) } :
                boosted ? new[] { C(CardKind.Slash) } : new[] { C(CardKind.Crossbow), C(CardKind.Dodge, Suit.Club), C(CardKind.Dodge, Suit.Heart), C(CardKind.Slash), C(CardKind.Nullification) };
            var setup = new ShanheBattleSetup(83, 1, "mzf-native", Hero, boosted ? 4 : 5, 0, 0, 0, 0, boosted ? [Driver, Boost] : [Driver], hand, [],
                [new(Enemy, Hp: 8) { StartingHand = [] }, new(Other, Hp: boosted ? 4 : 3) { StartingHand = boosted ? [] : [C(CardKind.Slash)] }],
                [new(Ally, Hp: boosted ? 8 : 2) { StartingHand = [] }], new string('f', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
