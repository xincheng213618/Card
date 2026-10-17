using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuanduCardChecks
{
    internal const string Mode = "fixture:guandu-cards", Driver = "fixture:guandu-card-driver", Quiet = "fixture:guandu-card-quiet", Watch = "fixture:guandu-card-watch";
    private const string Jiaozhao = "boundary:jiaozhao-round-current";

    public static void IntoxicationKeepsEnemyTurnQuotaAndEveryRealDamageBonus()
    {
        var (g, r) = Start(CardKind.Intoxication);
        AuditAi(g, LegalActionKind.Intoxication);
        var enemy = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat is { } target && V(g, target).TeamId != V(g, 0).TeamId).TargetSeat!.Value;
        var before = V(g, enemy).Hp;
        var wine = Physical(g, CardKind.Intoxication, [enemy]);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.Intoxication));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Intoxication);
        Need(use.Action!.PhysicalCards is [{ CardId: var paid }] && paid == wine && use.TargetSeats.SequenceEqual([enemy]) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == wine).Location == CardLocation.Processing,
            "Intoxication is a genuine targeted basic use with its paid entity retained through the actual committed-use observer.");
        Cold(g, r); Play(g);
        Need(g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Intoxication).All(a => a.TargetSeat != enemy) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Intoxication) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.BindingId == "fixed-alcohol") &&
            !V(g, 0).HasAlcoholEffect && E<GuanduIntoxicationAppliedEvent>(g).Count(e => e.UseFrameId == use.Id) == 1,
            "Only this enemy is spent for the actual turn; another enemy and fixed ordinary Wine remain legal and no next-Slash Wine state is created.");
        Use(g, "damage", enemy); Play(g); var afterSkill = V(g, enemy).Hp;
        Physical(g, CardKind.Slash, [enemy]); Play(g);
        Need(afterSkill == before - 2 && V(g, enemy).Hp == before - 4 &&
            E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == enemy && e.Amount == 2) == 2 &&
            g.CardMovements.Count(m => m.CardId == wine && m.To == CardLocation.DiscardPile) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1,
            "The same turn's real skill damage and later real Slash each gain one, without consuming the mark or paying the basic card twice.");
        Cold(g, r);
    }

    public static void StoreGrainUsesThreeTargetsSeparateCounterspellsAndRepeatedNativeDraws()
    {
        {
            var (g, r) = Start(CardKind.StoreGrain);
            AuditAi(g, LegalActionKind.StoreGrain);
            var card = Physical(g, CardKind.StoreGrain, [0, 1, 2]);
            Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.GuanduCardEffect is not null) && g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any());
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.StoreGrain);
            Need(use.TargetSeats.SequenceEqual([0, 1, 2]) && use.GuanduCardEffect is { Stage: GuanduCardEffectStage.Drawing } &&
                use.NativeDraw is { RequestedCount: 1 } && g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing,
                "Three chosen targets use real sequential counterspell and native draw requests, retaining the one paid card through its first draw child.");
            Cold(g, r); Play(g);
            Need(E<GuanduCardDrawAppliedEvent>(g).Where(e => e.UseFrameId == use.Id).Select(e => e.RecipientSeat).SequenceEqual([0, 1, 2]) &&
                E<NullificationResolvedEvent>(g).Count(e => e.EffectCardKind == CardKind.StoreGrain) == 3 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "Every selected role gains exactly one actual card through three distinct target windows and the original material finishes once."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.StoreGrain, counter: true);
            var card = Physical(g, CardKind.StoreGrain, [1, 2]);
            Until(g, () => P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }); Cold(g, r);
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "nullification"); Play(g);
            var useId = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == card && e.CardKind == CardKind.StoreGrain).ResolutionId;
            Need(E<NullificationResolvedEvent>(g).Count(e => e.EffectCardKind == CardKind.StoreGrain && e.EffectNullified) == 1 &&
                E<GuanduCardDrawAppliedEvent>(g).Where(e => e.UseFrameId == useId).Select(e => e.RecipientSeat).SequenceEqual([2]),
                "One genuine converted counterspell cancels only the first target, preserving the second target's normal draw and shared physical parent."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.StoreGrain, repeat: true);
            for (var i = 0; i < 9; i++) { Physical(g, CardKind.StoreGrain, [0]); Play(g); }
            var card = Physical(g, CardKind.StoreGrain, [1, 2]);
            Until(g, () => g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.StoreGrain && f.GuanduCardEffect is not null));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.StoreGrain); Cold(g, r); Play(g);
            var gains = E<GuanduCardDrawAppliedEvent>(g).Where(e => e.UseFrameId == use.Id).ToArray();
            Need(gains.Length == 4 && gains.GroupBy(e => e.RecipientSeat).All(group => group.Count() == 2 && group.Select(e => e.Occurrence).SequenceEqual([1, 2])) &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "The real tenth-card event repeats both native target effects with distinct occurrences, without declaring or paying the original card again."); Cold(g, r);
        }
    }

    public static void ReinforcementsAndUnitedEffortKeepNativeRecoveryChainsAndRecast()
    {
        {
            var (g, r) = Start(CardKind.Reinforcements);
            Use(g, "hurt", 0); Play(g); Use(g, "hurt", 1); Play(g); Use(g, "hurt", 4); Play(g);
            Need(g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Reinforcements).All(a => !a.TargetSeats.Contains(0)) &&
                g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Reinforcements && a.TargetSeats.SequenceEqual([1, 4])),
                "Reinforcements excludes self and full-health roles but allows both a wounded ally and enemy in one genuine two-target use.");
            AuditAi(g, LegalActionKind.Reinforcements); var hp = new[] { V(g, 1).Hp, V(g, 4).Hp };
            var card = Physical(g, CardKind.Reinforcements, [1, 4]);
            Until(g, () => g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.Continuation == PostEventContinuation.GuanduCardEffect));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Reinforcements);
            Need(use.GuanduCardEffect is { Stage: GuanduCardEffectStage.RecoveryChildren, RecoveryAmount: 1 } &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing,
                "The real recovery observer remains under the exact target receipt before either the next target or material retirement.");
            Cold(g, r); Play(g);
            Need(V(g, 1).Hp == hp[0] + 1 && V(g, 4).Hp == hp[1] + 1 &&
                E<GuanduCardRecoveryIssuedEvent>(g).Count(e => e.UseFrameId == use.Id) == 2 &&
                E<NullificationResolvedEvent>(g).Count(e => e.EffectCardKind == CardKind.Reinforcements) == 2 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "Both legal wounded other roles recover one through distinct real target windows; health and costs replay once."); Cold(g, r);
        }
        {
            var (g, r) = Start(CardKind.UnitedEffort);
            foreach (var seat in new[] { 0, 1, 4 }) { Use(g, "chain", seat); Play(g); }
            AuditAi(g, LegalActionKind.UnitedEffort);
            var choices = g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.UnitedEffort).Select(a => a.TargetSeats).ToArray();
            Need(choices.All(targets => targets.SequenceEqual([0, 1, 2, 3]) || targets.SequenceEqual([4, 5, 6, 7])),
                "United Effort publishes only an entire selected army, never an arbitrary subset or a mixture of armies.");
            var card = Physical(g, CardKind.UnitedEffort, [0, 1, 2, 3]);
            Until(g, () => g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any() && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.GuanduCardEffect is not null));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.UnitedEffort); Cold(g, r); Play(g);
            var draws = E<GuanduCardDrawAppliedEvent>(g).Where(e => e.UseFrameId == use.Id).ToArray();
            Need(draws.Where(e => e.ActualDrawCount > 0).Select(e => e.RecipientSeat).SequenceEqual([0, 1]) && V(g, 0).IsChained && V(g, 1).IsChained &&
                draws.Where(e => e.RecipientSeat is 2 or 3).All(e => e.RequestedCount == 0),
                "The allied branch draws only for already chained roles, leaves their chain state unchanged, and gives healthy unchained allies no cards.");
            Physical(g, CardKind.UnitedEffort, [4, 5, 6, 7]); Play(g);
            Need(Enumerable.Range(4, 4).All(seat => V(g, seat).IsChained) && E<NullificationResolvedEvent>(g).Count(e => e.EffectCardKind == CardKind.UnitedEffort) == 8,
                "The enemy branch sets all real targets chained rather than toggling the already chained enemy, with one counterspell per affected role.");
            var recast = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Recast);
            var used = E<CardUseDeclaredEvent>(g).Count(); var hand = V(g, 0).HandCount;
            Accept(g, new RecastCardCommand(0, recast.CardId!.Value, g.Revision, P(g)!.PromptId));
            Until(g, () => g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any()); Cold(g, r); Play(g);
            Need(V(g, 0).HandCount == hand && E<CardUseDeclaredEvent>(g).Count() == used &&
                E<CardRecastEvent>(g).Any(e => e.CardId == recast.CardId && e.CardKind == CardKind.UnitedEffort && e.DrawCount == 1) &&
                g.CardMovements.Count(m => m.CardId == recast.CardId && m.To == CardLocation.DiscardPile) == 1,
                "United Effort recasts through a real paid recast and native draw child, without becoming a card use or opening any counterspell window."); Cold(g, r);
        }
    }

    public static void CompleteBasicFamiliesProjectIntoxicationWithOneAndZeroMaterials()
    {
        foreach (var upgrades in new[] { 0, 2 })
        {
            var (g, r) = Start(CardKind.Slash, conversions: true);
            for (var i = 0; i < upgrades; i++) { Use(g, "upgrade"); Play(g); }
            var original = r.GetSkill(Jiaozhao).Program!; var hash = original.GameplayHash;
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Intoxication && a.ConversionSource?.SkillId == Jiaozhao);
            Need(g.GetCardConversionDefinition(action.ConversionSource!) is { OutputKind: CardKind.Intoxication, ForResponse: false } &&
                original.ViewAs.All(v => v.OutputKind != CardKind.Intoxication) && (upgrades == 2 ? action.CardId == 0 : action.CardId > 0),
                "Only the immutable match projection of a complete basic family exposes Intoxication, preserving its exact one/zero material arity and forbidding rescue response.");
            var hand = V(g, 0).HandCount; Act(g, action);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.Intoxication));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Intoxication);
            Need(use.Action!.PhysicalCards.Count == (upgrades == 2 ? 0 : 1), "The actual accepted basic use retains the original conversion's genuine material count.");
            Cold(g, r); Play(g);
            Need(V(g, 0).HandCount == hand - (upgrades == 2 ? 0 : 1) && g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Jiaozhao) &&
                r.GetSkill(Jiaozhao).Program!.GameplayHash == hash && !g.CardMovements.Any(m => m.CardId == 0),
                "Native Intoxication consumes the real shared round allowance and exact entities while leaving original content unchanged.");
            Use(g, "hurt", 0); Play(g);
            var peach = g.GetHumanLegalActions().First(a => a.ConversionSource?.BindingId == "fixed-peach");
            Need(peach.PlayedCardKind == CardKind.Peach && g.GetCardConversionDefinition(peach.ConversionSource!)!.OutputKind == CardKind.Peach,
                "A fixed named Peach retains its ordinary native healing name in Guandu, without acquiring the new enemy-target basic.");
            Act(g, peach); Play(g); Cold(g, r);
        }
    }

    internal static (GameEngine, ContentRegistry) Start(CardKind kind, bool counter = false, bool repeat = false, bool conversions = false,
        GuanduBattleEvent? battleEvent = null, bool reachPlay = true, int deckSize = 240, bool deathWatch = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(kind, counter, repeat, conversions, battleEvent, deckSize, deathWatch));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:cao", HumanGuanduRole = GuanduSeatRole.Commander, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 64 }, r);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        var general = P(g)!.Choices.First().ContentIds.Single();
        Accept(g, new SelectGeneralCommand(0, general, g.Revision, P(g)!.PromptId)); if (reachPlay) Play(g); return (g, r);
    }
    internal static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    internal static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 8).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    internal static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    internal static bool IsPlay(GameEngine g) => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    internal static void Play(GameEngine g) => Until(g, () => IsPlay(g));
    internal static void Use(GameEngine g, string id, int? target = null) => Accept(g, new UseProgramSkillCommand(0,
        Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    internal static void Act(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats,
        g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    internal static int Physical(GameEngine g, CardKind kind, IReadOnlyList<int> targets)
    {
        var a = g.GetHumanLegalActions().First(a => a.CardId > 0 && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets) &&
            (a.PlayedCardKind ?? V(g, 0).Hand.SingleOrDefault(c => c.Id == a.CardId)?.Kind) == kind);
        Act(g, a); return a.CardId!.Value;
    }
    internal static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    internal static void Until(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 240; i++)
        {
            if (done()) return;
            if (P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "decline" or "take-damage"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "decline" or "take-damage");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Guandu card fixture did not reach its actual boundary: " + P(g)?.Kind + "/" + P(g)?.Prompt);
    }
    internal static void Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(Enumerable.Range(0, 8).All(seat => JsonSerializer.Serialize(g.CreateSnapshot(seat)) == JsonSerializer.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Every player's private view and the physical movement ledger survive cold command replay at the real child boundary.");
    }
    private static void AuditAi(GameEngine g, LegalActionKind kind)
    {
        var actions = g.GetHumanLegalActions().Where(a => a.Kind == kind || a.Kind == LegalActionKind.EndPlay).ToArray();
        var (chosen, thought) = new SimpleAiBrain(0, 7).ChoosePlay(g.CreateSnapshot(0), actions, 1);
        Need(actions.Contains(chosen) && thought.Candidates.All(c => double.IsFinite(c.Score) && !string.IsNullOrWhiteSpace(c.Reason)),
            "Every published Guandu action has a finite public-information AI score without assuming a single or hidden target.");
    }
    internal static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(c); Need(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    internal static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(CardKind kind, bool counter, bool repeat, bool conversions, GuanduBattleEvent? battleEvent, int deckSize, bool deathWatch) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-guandu-cards", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.guandu-events.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.guandu-events.presentation.json")!);
            var events = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in events.Programs) b.AddSkill(new(id, id, "Native Guandu event fixture.")
                { Program = program, ProgramPresentation = events.Presentations[id] });
            var json = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"initialHandSize","operation":"add","value":14,"priority":0,"condition":{"kind":"always"}},
                {"id":"extra-slash","query":"slashLimit","operation":"add","value":1,"priority":0,"condition":{"kind":"always"}}],
                "viewAs":[{"id":"tier-ref","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"conversionStateId":"boundary-jiaozhao-round-tier"},
                  {"id":"fixed-peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"extendedUse":true},
                  {"id":"fixed-alcohol","inputKinds":[],"inputSuits":[],"outputKind":"alcohol","forPlay":true,"forResponse":false,"useOnly":true}
                  ,{"id":"fixed-fire","inputKinds":["slash"],"inputSuits":[],"outputKind":"fireSlash","forPlay":true,"forResponse":false}
                  ,{"id":"slash-response","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":false,"forResponse":true}
                  {{{(counter ? ",{\"id\":\"counter\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"nullification\",\"forPlay\":false,\"forResponse\":true}" : "")}}}],
                "activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                  {"id":"nick","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                  {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                  {"id":"grow-five","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":5}]},
                  {"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"real-weapon"}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                  {"id":"skip-draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:guandu-skipdraw"]}]},
                  {"id":"jianji","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"drawAndOfferJianjiExactCard","target":"selectedTarget"}]},
                  {"id":"jianji-again","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"drawAndOfferJianjiExactCard","target":"selectedTarget"}]},
                  {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                  {"id":"upgrade","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"upgradeConversionTier","target":"owner","stateId":"boundary-jiaozhao-round-tier"}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[
                {"id":"draw","window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
                {"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
                {"id":"basic","window":"cardUseCommitted","ownerRelation":"actor","cardCategories":["basic"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":true,
                "effects":[{"op":"draw","target":"owner","amount":1},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:guandu-skipdraw","revision":1,"triggers":[{"id":"skip","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]},
              {"id":"fixture:guandu-death-watch","revision":1,"triggers":[{"id":"death","window":"characterDied","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
            ]}
            """;
            var ids = new[] { Driver, Watch, Quiet, "fixture:guandu-skipdraw", "fixture:guandu-death-watch" };
            if (kind is CardKind.BorrowedSword or CardKind.IronChain || battleEvent == GuanduBattleEvent.HoldTheLine)
            {
                var mutable = JsonNode.Parse(json)!;
                if (kind == CardKind.IronChain)
                    mutable["skills"]!.AsArray()[1]!["triggers"]!.AsArray()[2]!["cardCategories"] = JsonNode.Parse("""["trick"]""");
                else mutable["skills"]!.AsArray()[0]!["viewAs"]!.AsArray().Add(JsonNode.Parse("""
                    {"id":"dodge-response","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}
                    """));
                json = mutable.ToJsonString();
            }
            var own = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Small real eight-seat Guandu card fixture." }) }));
            foreach (var (id, program) in own.Programs) b.AddSkill(new(id, id, "Native Guandu card fixture.") { Program = program, ProgramPresentation = own.Presentations[id] });
            foreach (var c in new[] { CardKind.Intoxication, CardKind.StoreGrain, CardKind.Reinforcements, CardKind.UnitedEffort, CardKind.IronChain })
                b.AddCard(new("fixture:guandu-card-" + c, CardCatalog.Get(c).DisplayName, CardCatalog.Get(c).CategoryName, "Native Guandu card.", c));
            var pools = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var army in new[] { "cao", "yuan" })
            {
                var generals = Enumerable.Range(0, 9).Select(i => $"fixture:guandu-{army}-{i}").ToArray();
                foreach (var id in generals) b.AddGeneral(new(id, id, "", army == "cao" && id.EndsWith("-0", StringComparison.Ordinal) ? Driver : Quiet,
                    army == "cao" ? "wei" : "qun", 8, new[] { Watch }.Concat(army == "cao" && id.EndsWith("-0", StringComparison.Ordinal) ? [Quiet] : Array.Empty<string>())
                        .Concat(kind == CardKind.BorrowedSword && !(army == "cao" && id.EndsWith("-0", StringComparison.Ordinal)) ? ["sp:guan-yu-wusheng"] : Array.Empty<string>())
                        .Concat(deathWatch && army == "cao" && id.EndsWith("-0", StringComparison.Ordinal) ? ["fixture:guandu-death-watch"] : Array.Empty<string>())
                        .Concat(conversions ? [Jiaozhao] : Array.Empty<string>()).ToArray()));
                pools["team:" + army] = generals;
            }
            const string deck = "fixture:guandu-card-deck";
            b.AddDeck(new(deck, deck, 4, 2, []) { PhysicalCards = Enumerable.Range(0, deckSize).Select(i => new ContentDeckPhysicalCard(
                kind == CardKind.Duel ? "standard:duel" : kind == CardKind.BorrowedSword ? i < 40 ? "standard:crossbow" : "classic:borrowed-sword" :
                kind == CardKind.Slash || kind == CardKind.Intoxication && i % 2 == 0 ? "standard:slash" : "fixture:guandu-card-" + kind,
                Suit.Heart, i % 13 + 1)).ToArray() });
            var layout = new[] { "team:cao", "team:cao", "team:cao", "team:cao", "team:yuan", "team:yuan", "team:yuan", "team:yuan" };
            b.AddMode(new(Mode, Mode, 8, 8, new Dictionary<string, int>(), deck, 6, pools.Values.SelectMany(p => p).ToArray(), ContentModeKind.Team,
                new Dictionary<string, int> { ["team:cao"] = 4, ["team:yuan"] = 4 })
            { GuanduActivity = new(pools, new Dictionary<string, string> { ["team:cao"] = pools["team:cao"][0], ["team:yuan"] = pools["team:yuan"][0] },
                [battleEvent ?? (repeat ? GuanduBattleEvent.TenWinsTenLosses : GuanduBattleEvent.Underdog)], Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)layout).ToArray(), PlayerTier: 1) });
        }
    }
}
