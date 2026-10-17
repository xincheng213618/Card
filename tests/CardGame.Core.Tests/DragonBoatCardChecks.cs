using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DragonBoatCardChecks
{
    private const string Mode = "fixture:dragon-card-mode", Driver = "fixture:dragon-card-driver", Watch = "fixture:dragon-card-watch";
    private const string Jiaozhao = "boundary:jiaozhao-round-current";

    public static void AlliedBasicsKeepNativeRecoveryAndOneSharedWineAllowance()
    {
        {
            var (g, registry) = Start(CardKind.Zong);
            Use(g, "hurt", 4); Play(g);
            var hp = V(g, 4).Hp;
            Need(g.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Zong).All(a => a.TargetSeat == 4),
                "Zong must publish only a living wounded ally, not a healthy self or another team's character.");
            AuditAi(g, CardKind.Zong);
            var card = Physical(g, CardKind.Zong, [4]);
            Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Zong);
            Need(use.Action!.EffectiveKind == CardKind.Zong && use.TargetSeats.SequenceEqual([4]) &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing,
                "Zong preserves its actual named basic use and paid material through the native recovery child.");
            Cold(g, registry); Play(g);
            Need(V(g, 4).Hp == hp + 1 && E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 4 && e.Amount == 1) &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "A selected teammate genuinely recovers one HP and the physical Zong is retired once.");
            AuditAi(g, CardKind.Zong); Cold(g, registry);
        }
        {
            var (g, registry) = Start(CardKind.RealgarWine, slash: true);
            AuditAi(g, CardKind.RealgarWine);
            Need(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.BindingId == "fixed-alcohol"),
                "The explicit ordinary Wine conversion is available before either name spends the one shared allowance.");
            var wine = Physical(g, CardKind.RealgarWine, []); Play(g);
            var wineUse = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == wine && e.CardKind == CardKind.RealgarWine);
            Need(V(g, 0).HasAlcoholEffect && !g.GetHumanLegalActions().Any(a => a.Kind is LegalActionKind.RealgarWine or LegalActionKind.Alcohol) &&
                E<JianjiOrdinaryAlcoholPaidEvent>(g).Count(e => e.NativeUseId == wineUse.ResolutionId && e.ActorSeat == 0 && e.CountsQuota) == 1,
                "Realgar Wine spends the normal once-per-turn Wine allowance at commitment.");
            var before = V(g, 1).Hp; Physical(g, CardKind.Slash, [1]); Play(g);
            Need(V(g, 1).Hp == before - 2 && !V(g, 0).HasAlcoholEffect &&
                !g.GetHumanLegalActions().Any(a => a.Kind is LegalActionKind.RealgarWine or LegalActionKind.Alcohol) &&
                E<DragonBoatTreasureChangedEvent>(g).Last(e => e.TeamId == "team:wei").Current == 2 &&
                g.CardMovements.Count(m => m.CardId == wine && m.To == CardLocation.DiscardPile) == 1,
                "The next native Slash consumes the real Wine damage bonus, awards two real treasures and does not reopen the shared allowance.");
            AuditAi(g, CardKind.RealgarWine); Cold(g, registry);
        }
    }

    public static void AlliedAndGlobalDrawsUseExactRecipientsRequestsAndCounterspellWindows()
    {
        foreach (var kind in new[] { CardKind.SameBoat, CardKind.StriveUpstream })
        {
            var (g, registry) = Start(kind);
            AuditAi(g, kind);
            var card = Physical(g, kind, kind == CardKind.SameBoat ? [4] : Enumerable.Range(0, 8).ToArray());
            Until(g, () => g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any() && P(g)?.SkillPrompt?.SkillId == Watch);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == kind);
            Need(use.DragonBoatCardEffect is { Stage: DragonBoatCardEffectStage.Drawing } && use.NativeDraw is not null &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing &&
                !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id),
                "A real optional NativeDrawStarting child retains its exact recipient, requested amount, paid material and card target cursor.");
            Cold(g, registry); Play(g);
            var draws = E<DragonBoatCardDrawAppliedEvent>(g).Where(e => e.UseFrameId == use.Id).ToArray();
            Need(kind == CardKind.SameBoat ? draws.Length == 2 && draws.Select(d => d.RecipientSeat).SequenceEqual([4, 0]) &&
                    draws.All(d => d.RequestedCount == 2 && d.ActualDrawCount == 2) :
                draws.Length == 8 && draws.Select(d => d.RecipientSeat).SequenceEqual(Enumerable.Range(0, 8)) &&
                    draws.All(d => d.RequestedCount == (d.RecipientSeat == 0 ? 0 : 1) && d.ActualDrawCount == d.RequestedCount),
                "Same Boat draws twice for both tied-leading teammates; Strive Upstream allows its user to draw zero and gives every other actual target one.");
            Need(E<NullificationResolvedEvent>(g).Count(e => e.EffectCardKind == kind) == (kind == CardKind.SameBoat ? 1 : 8) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1,
                "Same Boat has one counterspell window covering both recipients; Strive Upstream has one per actual target and neither repeats its paid cost.");
            AuditAi(g, kind); Cold(g, registry);
        }
    }

    public static void AgainstCurrentFreezesPostDamageTiesThenDiscardsBeforeDrawing()
    {
        foreach (var prevent in new[] { false, true })
        {
            var (g, registry) = Start(CardKind.AgainstCurrent, prevent: prevent);
            AuditAi(g, CardKind.AgainstCurrent);
            var hp = V(g, 1).Hp; var card = Physical(g, CardKind.AgainstCurrent, [1]);
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "dragon-boat-discard") == true);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.AgainstCurrent);
            var rank = E<DragonBoatAgainstCurrentRankingEvent>(g).Single(e => e.UseFrameId == use.Id);
            Need(V(g, 1).Hp == hp - (prevent ? 0 : 1) && rank.TeamTreasures == (prevent ? 0 : 1) && rank.Most && rank.Least == prevent &&
                use.DragonBoatCardEffect is { Stage: DragonBoatCardEffectStage.ChoosingDiscard } &&
                use.CardAttack is { Active: false, EffectiveCardKind: CardKind.AgainstCurrent } &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.Processing &&
                !E<DragonBoatCardDrawAppliedEvent>(g).Any(e => e.UseFrameId == use.Id),
                "Against Current waits for native damage or prevention and its actual treasure event before fixing both rank conditions and requiring the leading team's discard.");
            Cold(g, registry); var discarded = P(g)!.Choices.First().Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([discarded]));
            if (prevent)
            {
                Until(g, () => g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any() && P(g)?.SkillPrompt?.SkillId == Watch);
                Need(g.CardMovements.Any(m => m.CardId == discarded && m.To == CardLocation.DiscardPile) &&
                    !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id),
                    "When all teams tie, the actual mandatory discard completes before the later native one-card draw request.");
                Cold(g, registry);
            }
            Play(g);
            Need(E<DragonBoatCardDrawAppliedEvent>(g).Where(e => e.UseFrameId == use.Id).Sum(e => e.ActualDrawCount) == (prevent ? 1 : 0) &&
                g.CardMovements.Count(m => m.CardId == discarded && m.To == CardLocation.DiscardPile) == 1 &&
                g.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1,
                "The native damage's paid parent resumes once after discard/draw children, without repeating damage, self-discard or the original trick payment.");
            AuditAi(g, CardKind.AgainstCurrent); Cold(g, registry);
        }
    }

    public static void FullBasicFamiliesKeepMaterialArityWhileNamedPeachStaysPeach()
    {
        foreach (var tier in new[] { 0, 2 })
        {
            var (g, registry) = Start(CardKind.Slash, conversions: true);
            Use(g, "hurt", 4); Play(g);
            for (var i = 0; i < tier; i++) { Use(g, "upgrade"); Play(g); }
            var original = registry.GetSkill(Jiaozhao).Program!; var hash = original.GameplayHash;
            var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Jiaozhao && a.PlayedCardKind == CardKind.Zong && a.TargetSeat == 4);
            var hand = V(g, 0).HandCount; var hp = V(g, 4).Hp;
            Need(action.CardId == (tier == 2 ? 0 : action.CardId) && (tier == 2 || action.CardId > 0) &&
                g.GetCardConversionDefinition(action.ConversionSource!) is { OutputKind: CardKind.Zong, ForResponse: false } &&
                original.ViewAs.All(rule => rule.OutputKind is not (CardKind.Zong or CardKind.RealgarWine)),
                "Only the match's complete basic-name family projects Zong with genuine one/zero arity; the immutable registry and rescue directions stay unchanged.");
            PlayAction(g, action); Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Zong);
            Need(use.Action!.PhysicalCards.Count == (tier == 2 ? 0 : 1) && use.Action.ConversionChain.Single().SkillId == Jiaozhao,
                "The exact source tuple and original material count are accepted by the native basic-card use.");
            Cold(g, registry); Play(g);
            Need(V(g, 4).Hp == hp + 1 && V(g, 0).HandCount == hand - (tier == 2 ? 0 : 1) &&
                g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Jiaozhao) &&
                !g.CardMovements.Any(m => m.CardId == 0) && registry.GetSkill(Jiaozhao).Program!.GameplayHash == hash,
                "The real projected Zong heals its teammate, pays each entity once or pays none at zero tier, and consumes the genuine shared round quota.");
            Use(g, "hurt", 0); Play(g);
            var named = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.ConversionSource.BindingId == "fixed-peach");
            Need(named.PlayedCardKind == CardKind.Peach && g.GetCardConversionDefinition(named.ConversionSource!)!.OutputKind == CardKind.Peach,
                "An explicit fixed virtual Peach keeps its original name and ordinary native recovery semantics in the Dragon Boat mode.");
            PlayAction(g, named); Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch); Cold(g, registry); Play(g); Cold(g, registry);
        }
        {
            var (g, registry) = Start(CardKind.Slash, conversions: true);
            Use(g, "upgrade"); Play(g); Use(g, "upgrade"); Play(g);
            var hp = V(g, 1).Hp;
            var zero = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Jiaozhao && a.CardId == 0 &&
                a.PlayedCardKind == CardKind.AgainstCurrent && a.TargetSeat == 1);
            PlayAction(g, zero);
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "dragon-boat-discard") == true);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.AgainstCurrent);
            Need(use.CardId == 0 && use.Action!.PhysicalCards.Count == 0 && V(g, 1).Hp == hp - 1 &&
                use.DragonBoatCardEffect is { Stage: DragonBoatCardEffectStage.ChoosingDiscard } &&
                use.CardAttack is { Active: false, EffectiveCardKind: CardKind.AgainstCurrent } &&
                E<DragonBoatAgainstCurrentRankingEvent>(g).Any(e => e.UseFrameId == use.Id && e.TeamTreasures == 1),
                "The genuine zero-material ordinary-trick source executes Against Current's native damage and ranking tail without inventing a physical card zero.");
            Cold(g, registry); Answer(g, _ => true); Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && !g.CardMovements.Any(m => m.CardId == 0) &&
                g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Jiaozhao),
                "The zero native attack completes its actual parent once after its paid self-discard and retains the real shared round allowance.");
            Cold(g, registry);
        }
    }

    private static (GameEngine, ContentRegistry) Start(CardKind kind, bool slash = false, bool prevent = false, bool conversions = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(kind, slash, prevent, conversions));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:wei", ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        var selected = P(g)!.Choices.First(c => c.ContentIds.Single().StartsWith("fixture:dragon-card-wei-", StringComparison.Ordinal)).ContentIds.Single();
        Accept(g, new SelectGeneralCommand(0, selected, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 8).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool IsPlay(GameEngine g) => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static void Play(GameEngine g) => Until(g, () => IsPlay(g));
    private static void Use(GameEngine g, string id, int? target = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P(g)!.PromptId));
    private static int Physical(GameEngine g, CardKind kind, IReadOnlyList<int> targets)
    {
        var action = g.GetHumanLegalActions().First(a => a.CardId > 0 && a.ConversionSource is null &&
            (a.PlayedCardKind ?? V(g, 0).Hand.SingleOrDefault(c => c.Id == a.CardId)?.Kind) == kind && a.TargetSeats.SequenceEqual(targets));
        PlayAction(g, action); return action.CardId!.Value;
    }
    private static void PlayAction(GameEngine g, LegalAction action) => Accept(g, new PlayCardCommand(0, action.CardId!.Value,
        action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Until(GameEngine g, Func<bool> condition)
    {
        for (var i = 0; i < 220; i++)
        {
            if (condition()) return;
            if (P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip" ||
                c.Parameters.GetValueOrDefault("response") is "pass" or "decline"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("response") is "pass" or "decline");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Dragon Boat card fixture did not reach its actual boundary: " + P(g)?.Kind + "/" + P(g)?.Prompt);
    }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 8).All(seat => JsonSerializer.Serialize(g.CreateSnapshot(seat)) == JsonSerializer.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Every viewer and native material ledger must survive cold command replay at the actual pending child.");
    }
    private static void AuditAi(GameEngine g, CardKind kind)
    {
        var actions = g.GetHumanLegalActions().Where(a => a.Kind is LegalActionKind.EndPlay ||
            (a.PlayedCardKind ?? V(g, 0).Hand.SingleOrDefault(c => c.Id == a.CardId)?.Kind) == kind).ToArray();
        if (actions.Length == 0) return;
        var (chosen, thought) = new SimpleAiBrain(0, 7).ChoosePlay(g.CreateSnapshot(0), actions, 1);
        Need(actions.Contains(chosen) && thought.Candidates.All(c => double.IsFinite(c.Score) && !string.IsNullOrWhiteSpace(c.Reason)),
            "Each published new-card action must have a finite public-information AI score without inventing self targets or reading foreign hands.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected actual command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(CardKind kind, bool slash, bool prevent, bool conversions) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-dragon-boat-cards", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.dragonboat-activity.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.dragonboat-activity.presentation.json")!);
            var activity = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var id in new[] { "dragonboat:aiye", "dragonboat:niepan" })
                b.AddSkill(new(id, id, "Actual native Dragon Boat permanent mode rule.") { Program = activity.Programs[id], ProgramPresentation = activity.Presentations[id] });
            var json = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hand","query":"initialHandSize","operation":"add","value":14,"priority":0,"condition":{"kind":"always"}}],
                "viewAs":[{"id":"tier-ref","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"conversionStateId":"boundary-jiaozhao-round-tier"},
                  {"id":"fixed-peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"extendedUse":true},
                  {"id":"fixed-alcohol","inputKinds":[],"inputSuits":[],"outputKind":"alcohol","forPlay":true,"forResponse":false,"useOnly":true}],
                "activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,
                  "effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"upgrade","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                    "effects":[{"op":"upgradeConversionTier","target":"owner","stateId":"boundary-jiaozhao-round-tier"}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[
                {"id":"request","window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
                {"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
                {"id":"basic-use","window":"cardUseCommitted","ownerRelation":"actor","cardCategories":["basic"],"optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"fixture:dragon-card-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":true,
                "effects":[{"op":"draw","target":"owner","amount":1},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:dragon-card-prevent","revision":1,"triggers":[{"id":"prevent","window":"beforeDamageApplied","subject":"damageTarget","optional":false,
                "effects":[{"op":"preventCurrentDamage","target":"owner"}]}]}
            ]}
            """;
            var ids = new[] { Driver, Watch, "fixture:dragon-card-quiet", "fixture:dragon-card-prevent" };
            var own = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Small native eight-seat Dragon Boat card fixture." }) }));
            foreach (var (id, program) in own.Programs) b.AddSkill(new(id, id, "Native card fixture.") { Program = program, ProgramPresentation = own.Presentations[id] });
            foreach (var card in new[] { CardKind.Zong, CardKind.RealgarWine, CardKind.SameBoat, CardKind.StriveUpstream, CardKind.AgainstCurrent })
                b.AddCard(new("fixture:dragon-card-" + card, CardCatalog.Get(card).DisplayName, CardCatalog.Get(card).CategoryName, "Native Dragon Boat card.", card));
            var pools = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var faction in new[] { "wei", "shu", "wu", "qun" })
            {
                var generals = Enumerable.Range(0, 12).Select(i => $"fixture:dragon-card-{faction}-{i}").ToArray();
                foreach (var id in generals) b.AddGeneral(new(id, id, "", faction == "wei" ? Driver : "fixture:dragon-card-quiet", faction, 4,
                    new[] { Watch }.Concat(faction == "wei" ? ["fixture:dragon-card-quiet"] : Array.Empty<string>())
                        .Concat(prevent ? ["fixture:dragon-card-prevent"] : Array.Empty<string>()).Concat(conversions ? [Jiaozhao] : Array.Empty<string>()).ToArray()));
                pools["team:" + faction] = generals;
            }
            foreach (var id in new[] { "su-fei", "huang-quan", "tang-zi", "liu-qi" }) b.AddGeneral(new("dragonboat:" + id, id, "", "fixture:dragon-card-quiet", "wei", 4,
                new[] { Watch }.Concat(prevent ? ["fixture:dragon-card-prevent"] : Array.Empty<string>()).ToArray()));
            pools["team:wei"] = pools["team:wei"].Concat(["dragonboat:huang-quan", "dragonboat:tang-zi"]).ToArray();
            pools["team:shu"] = pools["team:shu"].Concat(["dragonboat:huang-quan", "dragonboat:liu-qi"]).ToArray();
            pools["team:wu"] = pools["team:wu"].Concat(["dragonboat:su-fei", "dragonboat:tang-zi"]).ToArray();
            pools["team:qun"] = pools["team:qun"].Concat(["dragonboat:su-fei", "dragonboat:liu-qi"]).ToArray();
            const string deck = "fixture:dragon-card-deck";
            b.AddDeck(new(deck, deck, 4, 2, []) { PhysicalCards = Enumerable.Range(0, 240).Select(i => new ContentDeckPhysicalCard(
                slash && i % 2 == 0 || kind == CardKind.Slash ? "standard:slash" : "fixture:dragon-card-" + kind,
                Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, Mode, 8, 8, new Dictionary<string, int>(), deck, 6, pools.Values.SelectMany(p => p).Distinct().ToArray(),
                ContentModeKind.Team, pools.Keys.ToDictionary(team => team, _ => 2, StringComparer.Ordinal))
            { DragonBoatActivity = new(pools, PlayerTier: 1) });
        }
    }
}
