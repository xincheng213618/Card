using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuLingBenefitChecks
{
    private const string Skill = "ol:zhanyi", Driver = "fixture:zhanyi-benefit-driver", Hp = "fixture:zhanyi-benefit-hp";
    private const string Extra = "fixture:zhanyi-benefit-extra", Rescue = "fixture:zhanyi-benefit-rescue", Clock = "fixture:zhanyi-benefit-clock";
    private const string Mode = "identity:classic-zhanyi-benefit", Owner = "fixture:zhanyi-benefit-owner";

    public static void SharedRecoveryAndRuleExpirationKeepLegacyDefaults()
    {
        var store = new TurnCardUseEffectStore();
        var source = new CardUseEffectSource(Skill, "launch", 0, "owner-instance");
        var kinds = new List<CardKind> { CardKind.Peach, CardKind.Alcohol };
        var old = store.GrantRuleModifier(1, 0, 10, 0, source, SkillRuleQuery.CardUseDistanceLimit, SkillRuleOperation.Unlimited, 0, [CardKind.Slash]);
        var distance = store.GrantRuleModifier(1, 0, 11, 0, source, SkillRuleQuery.CardUseDistanceLimit, SkillRuleOperation.Unlimited, 0,
            [CardKind.Slash], expiration: SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        var current = store.GrantRecoveryModifier(1, 0, 10, 0, source, kinds, 1);
        var next = store.GrantRecoveryModifier(1, 0, 11, 0, source, kinds, 1, SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        var oldHand = store.GrantHandLimitKindExemption(1, 0, 10, 0, source, [CardKind.DrawTwo]);
        var hand = store.GrantHandLimitKindExemption(1, 0, 11, 0, source, [CardKind.DrawTwo], SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        kinds.Clear();
        Require(store.GrantRecoveryModifier(1, 0, 11, 0, source, [CardKind.Alcohol, CardKind.Peach], 1,
                SkillProgramDamageModifierExpiration.NextOwnerTurnStart) == next && next.CardKinds.Count == 2,
            "Recovery grants defensively freeze card kinds and have an idempotent frame/instruction key.");
        Require(!JsonSerializer.Serialize(old).Contains("Expiration", StringComparison.Ordinal) && !JsonSerializer.Serialize(oldHand).Contains("Expiration", StringComparison.Ordinal) &&
            JsonSerializer.Serialize(distance).Contains("Expiration", StringComparison.Ordinal),
            "Old unconfigured turn-rule payloads omit the additive default property.");
        Require(store.GetRecoveryModifiers(1, 0, 1, CardKind.Peach).Count == 0 &&
            store.GetRecoveryModifiers(1, 0, 0, CardKind.Slash).Count == 0,
            "The actual user, rather than card provider or damage source, owns recovery bonuses, and Slash cannot recover.");
        Require(store.ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(0, 1).Count == 0,
            "The granting actual turn cannot expire its own next-turn benefits.");
        Require(store.ExpireTurn(1, 0).Order().SequenceEqual(new[] { old.GrantSequence, current.GrantSequence, oldHand.GrantSequence }.Order()) &&
            store.GetRuleModifiers(2, 1, 0, SkillRuleQuery.CardUseDistanceLimit, CardKind.Slash).SequenceEqual([distance]) &&
            store.GetRecoveryModifiers(2, 1, 0, CardKind.Peach).Single().GrantSequence == next.GrantSequence &&
            store.GetHandLimitExemptCardKinds(2, 1, 0).SetEquals([CardKind.DrawTwo]),
            "Default grants expire at the current turn end; explicit next-owner grants stay active during another player's turn.");
        Require(store.ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(1, 2).Count == 0 &&
            store.ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(0, 3).Count == 3 && store.HandLimitKindExemptions.Count == 0,
            "Only the source owner's next actual turn expires next-owner benefits.");
        store.GrantRecoveryModifier(3, 0, 12, 0, source, [CardKind.Peach], 1, SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        Require(store.ExpireRecoveryAndRuleModifiersOnDeath(0).Count == 1, "Dead owners cannot retain pending recovery benefits.");
        store.AssertInvariants();
        var root = JsonNode.Parse(Read("ol-zhu-ling", "rules"))!;
        var launch = root["skills"]![0]!["triggers"]![0]!["effects"]![0]!.AsObject();
        launch.Remove("recoveryBonus"); launch.Remove("expires"); launch.Remove("expireBooleanStatesAtOwnerTurnStart");
        Require(SkillProgramCatalog.Load(root.ToJsonString(), Read("ol-zhu-ling", "presentation")).Programs[Skill]
            .Triggers[0].Effects[0].CardUseBenefitPolicy is null, "Old launch configuration must retain its original behavior with no inferred opt-in.");
    }

    public static void PhysicalConvertedAndZeroPeachesFreezeRecoveryAcrossColdChildren()
    {
        foreach (var kind in new[] { "physical", "converted", "zero", "physical-extra", "zero-extra" })
        {
            var extra = kind.EndsWith("extra", StringComparison.Ordinal);
            var (game, registry) = Create(replacement: true, extra: extra);
            var ownerHp = game.State.Players[0].Hp;
            var physicalId = 0;
            if (kind.StartsWith("zero", StringComparison.Ordinal))
            {
                Use(game, "virtual"); Reach(game, p => p.Choices.Any(c => c.Parameters.ContainsKey("basic-option")));
                game = Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("basic-option") == "Peach:0");
            }
            else
            {
                var action = game.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Peach &&
                    (kind == "converted" ? a.ConversionSource?.SkillId == Driver : a.ConversionSource is null));
                Require(action is not null, "Missing " + kind + " Peach: " + JsonSerializer.Serialize(new
                { Player = game.CreateSnapshot(0).Players[0], Actions = game.GetHumanLegalActions() }));
                physicalId = action!.CardId!.Value; Play(game, action);
            }
            if (extra)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == Extra && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
                Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "designated-extra-target"));
                game = Cold(game, registry); Answer(game, c => c.Targets.SequenceEqual([2]));
            }
            Reach(game, p => p.Kind == DecisionKind.RecoveryReplacement);
            var recovery = game.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
            var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == recovery.ParentFrameId);
            Require(recovery.Attempt is { Amount: 2, SourceSeat: 0, TargetSeat: 0 } &&
                use.IssuedRecoveryModifiers is [var frozen] && frozen.Source.SkillId == Skill && frozen.Source.OwnerSeat == 0 &&
                use.RecoveryModifierApplications is [ { BaseAmount: 1, ModifiedAmount: 2, TargetSeat: 0 } ],
                "Physical, converted and zero-entity Peaches freeze the real user's +1 before the recovery-replacement child.");
            Frozen(use.IssuedRecoveryModifiers!); Frozen(use.RecoveryModifierApplications!);
            Frozen(Facts<CardRecoveryModifierGrantedEvent>(game).Single().Modifier.CardKinds);
            var before = State(game); var prompt = P(game)!;
            Require(!game.Submit(new AnswerPromptCommand((prompt.PlayerSeat + 1) % 4, prompt.PromptId, prompt.Choices[0].Id, game.Revision)).Accepted && State(game) == before,
                "A foreign actor cannot answer or alter the frozen recovery child.");
            game = Cold(game, registry);
            var redirect = kind == "converted";
            Answer(game, c => c.Parameters.GetValueOrDefault("recovery-action") == (redirect ? "redirect" : "keep"));
            if (!redirect)
            {
                Reach(game, p => p.SkillPrompt?.SkillId == Hp);
                game = Cold(game, registry);
                Require(game.State.Players[0].Hp == ownerHp + 2, "The HP child sees the final +2 recovery before resuming the exact use.");
                Continue(game);
            }
            Settle(game); game = Cold(game, registry);
            Require(game.State.Players[0].Hp == ownerHp + (redirect ? 0 : 2) &&
                Facts<ProgramCardRecoveryModifiedEvent>(game).Count(e => e.ResolutionId == use.Id && e.TargetSeat == 0) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id) == 1,
                "Cold child return neither reapplies the +1 nor repeats the completed real use.");
            if (redirect) Require(Facts<RecoveryReplacementChosenEvent>(game).Single() is { ReplacedAmount: 2, RecoveryAmount: 1 },
                "Redirect replaces the frozen amount and does not recursively boost its separate one-HP recovery.");
            if (extra) Require(Facts<RecoveryAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) == 1 &&
                Facts<ProgramCardRecoveryModifiedEvent>(game).Count(e => e.ResolutionId == use.Id && e.TargetSeat == 2) == 1,
                "The real added Peach target receives its own +2 exactly once using the original use's frozen source.");
            Require(physicalId == 0 ? !game.CardMovements.Any(m => m.CardId == 0) :
                game.CardMovements.Count(m => m.CardId == physicalId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(m => m.CardId == physicalId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Zero cards never manufacture an entity; physical and converted payment entities move exactly once.");
        }
    }
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static void Use(GameEngine game, string activation, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, P(game)!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, game.Revision)); }
    private static void Settle(GameEngine game) => Reach(game, IsPlay);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> reached)
    {
        for (var step = 0; step < 220; step++)
        {
            var p = P(game); if (p is not null && reached(p)) return;
            if (p?.SkillPrompt?.SkillId == Clock) Continue(game);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Zhanyi benefit boundary missing: " + JsonSerializer.Serialize(new
        { Kind = P(game)?.Kind, Skill = P(game)?.SkillPrompt?.SkillId, game.State.TurnNumber, game.State.CurrentSeat,
            Hp = game.State.Players.Select(p => new { p.Seat, p.Hp, p.IsAlive }).ToArray(),
            Choices = P(game)?.Choices.Select(c => c.Parameters).ToArray(),
            RecentEvents = game.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() }));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real benefit fixture command."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    { if (values is not IList<T> list || values.Count == 0) return; try { list[0] = values[0]; } catch (NotSupportedException) { return; } throw new InvalidOperationException("Recovery nested collections must be frozen."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Require(State(game) == State(restored), "Every private view, frozen recovery frame, event, movement and accepted command must cold-restore identically."); return restored; }
    private static string Read(string name, string suffix)
    { var a = typeof(StandardContentPackage).Assembly; using var reader = new StreamReader(a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith(name + "." + suffix + ".json", StringComparison.Ordinal)))!); return reader.ReadToEnd(); }

    private static (GameEngine, ContentRegistry) Create(bool replacement = false, bool extra = false, string? rescue = null, bool clock = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(replacement, extra, rescue, clock));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = replacement ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == Skill); Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zhanyi-launch"));
        var category = clock ? "Equipment" : "Trick";
        Require(P(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("category") == category), "The fixed small deck supplies a genuine category payment.");
        Answer(game, c => c.Parameters.GetValueOrDefault("category") == category); Settle(game); return (game, registry);
    }

    private sealed class Fixture(bool replacement, bool extra, string? rescue, bool clock) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhanyi-benefits", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var zhanyi = SkillProgramCatalog.Load(Read("ol-zhu-ling", "rules"), Read("ol-zhu-ling", "presentation"));
            builder.AddSkill(new(Skill, "战意", "正式战意") { Program = zhanyi.Programs[Skill], ProgramPresentation = zhanyi.Presentations[Skill] });
            string Act(string id, string effects, int targets = 0, int cards = 0) => $$"""
                {"id":"{{id}}","minCards":{{cards}},"maxCards":{{cards}},"minTargets":{{targets}},"maxTargets":{{targets}},"targetKind":"anyLiving","usesPerTurn":null,"effects":{{effects}}}
                """;
            var acts = new[] {
                Act("virtual", """[{"op":"offerVirtualBasicCard","target":"owner"}]"""),
                Act("trim", """[{"op":"loseHp","target":"owner","amount":1}]"""),
                Act("extra", """[{"op":"pendExtraTurn","target":"owner"}]"""),
                Act("flip", """[{"op":"turnOver","target":"owner"}]"""),
                Act("hurt-other", """[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true},{"op":"loseHp","target":"selectedTarget","amount":2}]""", 1),
                Act("store", """[{"op":"captureSelectedCards","target":"owner","resultBind":"wine"},{"op":"moveBoundCards","target":"owner","sourceBind":"wine","destination":"ownerPersistentZone","destinationZone":"chunlao","awaitMovementTriggers":true}]""", cards: 1)
            };
            var definitions = new List<string> {
                $$"""{"id":"{{Driver}}","revision":1,"viewAs":[{"id":"peach","inputKinds":["slash"],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"useOnly":true}],"activations":[{{string.Join(',', acts)}}]}""",
                $$"""{"id":"{{Hp}}","revision":1,"triggers":[{"id":"after-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp","options":[{"id":"continue"}]}]}]}""",
                """{"id":"fixture:zhanyi-benefit-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"keep-hand","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]}"""
            };
            if (replacement) definitions.Add("""{"id":"fixture:zhanyi-benefit-lord","revision":1,"cardPolicies":[{"id":"replacement","kind":"redirectOwnTurnFactionRecovery","factionId":"wei","ownerRole":"lord","value":1,"providerDrawCount":1}]}""");
            if (extra) definitions.Add($$"""{"id":"{{Extra}}","revision":1,"triggers":[{"id":"extra","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["basic","instantTrick"],"singleActionInstance":true,"optional":true,"effects":[{"op":"addOneDistanceFreeCurrentUseTarget","target":"owner"}]}]}""");
            if (rescue == "virtual") definitions.Add($$"""{"id":"{{Rescue}}","revision":1,"triggers":[{"id":"wine","window":"selfDyingResponse","subject":"owner","optional":false,"effects":[{"op":"useVirtualDyingAlcohol","target":"owner"}]},{"id":"finished-wine","window":"cardUseCompleted","ownerRelation":"observer","cardKinds":["alcohol"],"singleActionInstance":true,"optional":false,"condition":{"kind":"cardActionActualTurnUseOrdinalIs","value":1},"effects":[{"op":"chooseOption","target":"owner","resultBind":"wine-finished","options":[{"id":"continue"}]}]}]}""");
            if (rescue == "bound") definitions.Add($$$$"""{"id":"{{{{Rescue}}}}","revision":1,"triggers":[{"id":"wine","window":"dyingResponse","subject":"owner","optional":true,"condition":{"kind":"compare","left":{"kind":"currentOwnedZoneCount","zone":"chunlao"},"operator":"greaterThan","right":{"kind":"integerConstant","value":0}},"effects":[{"op":"selectSourceCard","target":"owner","cardSource":"owner","zones":["chunlao"],"resultBind":"wine"},{"op":"useBoundCardAsDyingAlcohol","target":"owner","sourceBind":"wine"}]}]}""");
            if (clock) definitions.Add($$"""{"id":"{{Clock}}","revision":1,"triggers":[{"id":"own","window":"afterTurnEnded","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"clock","options":[{"id":"continue"}]}]},{"id":"other","window":"afterTurnEnded","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"clock","options":[{"id":"continue"}]}]}]}""");
            var rules = JsonNode.Parse($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', definitions)}}]}""")!;
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实共享机制驱动", optionLabels = n.ToJsonString().Contains("chooseOption", StringComparison.Ordinal) ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "真实机制") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddSkill(new("fixture:zhanyi-benefit-select", "fixed", "selection")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100000d) });
            var ownerSkills = new List<string> { Skill, Hp }; if (extra) ownerSkills.Add(Extra); if (rescue is not null) ownerSkills.Add(Rescue); if (clock) ownerSkills.Add(Clock);
            builder.AddGeneral(new(Owner, "战意回归", "supporter", Driver, "wei", 12, ownerSkills) { InitialHp = 6 });
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:zhanyi-benefit-other-{s}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定对手", "supporter", "fixture:zhanyi-benefit-select", "wei", 12,
                replacement ? ["fixture:zhanyi-benefit-quiet", "fixture:zhanyi-benefit-lord"] : ["fixture:zhanyi-benefit-quiet"]) { InitialHp = 2 });
            builder.AddDeck(new("fixture:zhanyi-benefit-deck", "固定混合实体", 16, 0, [])
            { PhysicalCards = Enumerable.Range(0, 128).Select(i => new ContentDeckPhysicalCard(new[] { "standard:peach", "standard:slash", "standard:draw_two", "standard:crossbow" }[i % 4], Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "战意回复及期限", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zhanyi-benefit-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
