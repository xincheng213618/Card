using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DragonBoatGeneralChecks
{
    private const string Driver = "fixture:dragonboat-general-driver", Quiet = "fixture:dragonboat-general-quiet";
    private const string Watch = "fixture:dragonboat-general-watch", Mode = "fixture:dragonboat-generals";

    public static void ActualPackageDraftIsOptInAndSharedWithOnlyTheTeammate()
    {
        const string mode = "activity:dragon-boat-2018";
        Need(!StandardContentRegistry.CreateWithClassicGenerals().Modes.ContainsKey(mode), "The ordinary classic registry keeps the activity opt-in.");
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeDragonBoatActivities: true);
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:wei", ModeId = mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        var views = Enumerable.Range(0, 8).Select(seat => g.CreateSnapshot(seat)).ToArray();
        var draft = views.Select(v => v.DragonBoatDraft!).ToArray();
        var offered = draft.SelectMany(d => d.GeneralIds).ToArray();
        Need(views[0].Players.GroupBy(p => p.TeamId).Count() == 4 && views[0].Players.GroupBy(p => p.TeamId).All(t => t.Count() == 2) &&
            draft.All(d => d.GeneralIds.Count == 6) && offered.Length == 48 && offered.Distinct(StringComparer.Ordinal).Count() == 48 &&
            offered.Select(id => registry.Generals[id].CharacterId ?? id).Distinct(StringComparer.Ordinal).Count() == 48 &&
            offered.Count(id => id.StartsWith("dragonboat:", StringComparison.Ordinal)) == 4,
            "The real package produces four two-seat public teams, six candidates each, exactly four single-seat activity specials and 48 distinct character families.");
        Need(draft.Select((d, viewer) => d.TeammateSeat != viewer && views[viewer].Players[d.TeammateSeat].TeamId == views[viewer].Players[viewer].TeamId &&
                d.GeneralIds.All(id => registry.Modes[mode].DragonBoatActivity!.PlayerPoolIds[views[viewer].Players[viewer].TeamId!].Contains(id))).All(ok => ok) &&
            Enumerable.Range(1, 7).All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "Only the true teammate's legal faction draft is shared; the human's private decision is not published to other seats.");
        Cold(g, registry);
        var chosen = P(g)!.Choices.First().Parameters["general-id"];
        Accept(g, new SelectGeneralCommand(0, chosen, g.Revision, P(g)!.PromptId));
        Until(g, () => E<SetupCompletedEvent>(g).Any());
        Need(g.CreateSnapshot(0).Players.All(p => p.GeneralId is not null && p.MaxHp >= 3),
            "The genuine interactive draft reaches native setup completion with the selected generals and mode health.");
        Cold(g, registry);
    }

    public static void TreasurePaymentsCapAndTwoActualRoundsIncludeExtraTurns()
    {
        var (g, registry) = Start("rules"); Play(g);
        Use(g, "damage-two", [4]); Play(g);
        Use(g, "self-loss", []); Play(g);
        Need(!E<DragonBoatTreasureChangedEvent>(g).Any(), "Actual friendly damage and HP loss grant no enemy-damage treasure.");
        Use(g, "damage-twenty", [1]); Play(g);
        Need(g.CreateSnapshot(0).DragonBoatStandings!.Single(t => t.TeamId == "team:wei").Treasures == 12 &&
            V(g, 1).IsAlive && V(g, 1).Hp == 1 && g.CreateSnapshot(0).Status != EngineStatus.Completed,
            "A genuine twenty-point enemy hit caps the team at twelve; mandatory Aiye restores its victim and the race continues.");
        var payments = E<DragonBoatTreasureChangedEvent>(g).Count();
        Use(g, "damage-two", [2]); Play(g);
        Need(E<DragonBoatTreasureChangedEvent>(g).Count() == payments, "Further actual damage cannot mint a thirteenth treasure or a duplicate cap payment.");
        Use(g, "extra", []); Play(g);
        var turn = g.CreateSnapshot(0).TurnNumber;
        var startedBeforeExtra = E<TurnStartedEvent>(g).Count();
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.CreateSnapshot(0).TurnNumber > turn);
        Need(E<TurnStartedEvent>(g).Skip(startedBeforeExtra).Select(e => (e.ActorSeat, e.TurnNumber)).SequenceEqual([(0, turn + 1)]) &&
            E<RoundStartedEvent>(g).Count() == 1 && !E<RoundEndedEvent>(g).Any(),
            "The granted extra turn is real but does not consume the next normal seat or close the first round.");
        Cold(g, registry);
        Until(g, () => g.CreateSnapshot(0).Status == EngineStatus.Completed, endPlays: true);
        Need(E<RoundEndedEvent>(g).Select(e => e.RoundNumber).SequenceEqual([1, 2]) &&
            !E<RoundStartedEvent>(g).Any(e => e.RoundNumber > 2) && E<TurnStartedEvent>(g).Count() >= 17 &&
            g.CreateSnapshot(0).Winner == Winner.RankedTeams && E<DragonBoatTeamRankedEvent>(g).Count() == 4 &&
            g.CreateSnapshot(0).DragonBoatStandings!.Select(t => t.Rank).Order().SequenceEqual([1, 2, 3, 4]),
            "Exactly two actual native rounds finish after the extra-turn cursor returns, publishing one complete four-team ranking.");
        Cold(g, registry);
    }

    public static void LianpianUsesRealConsecutiveTargetsWithoutTheOrdinaryOlQuota()
    {
        var (g, registry) = Start("lianpian");
        Play(g);
        Native(g, LegalActionKind.Duel, 1); Play(g);
        Need(!Paid(g, "dragonboat:lianpian").Any(), "The first real card use cannot manufacture a consecutive target.");
        for (var index = 0; index < 4; index++)
        {
            Native(g, LegalActionKind.Duel, 1);
            Until(g, () => Optional(g, "dragonboat:lianpian"));
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "dragonboat-general") == true);
            Need(P(g)!.Choices.All(c => c.Targets.SequenceEqual([1])), "Only the actual common target is an activity Lianpian recipient.");
            if (index == 0) Cold(g, registry);
            Answer(g, c => c.Targets.SequenceEqual([1]));
            if (index == 0)
            {
                Until(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.DragonBoatGeneral is { Stage: DragonBoatGeneralStage.DrawChildren }));
                Cold(g, registry);
            }
            Play(g);
        }
        Need(Paid(g, "dragonboat:lianpian").Count() == 4 && Paid(g, "dragonboat:lianpian").All(e => e.RecipientSeat == 1 && e.ActualCount == 1) &&
            !g.Events.Select(e => e.Payload).OfType<LianpianGiftPaidEvent>().Any(),
            "The 2018 activity directly draws for the common target four times; the ordinary OL self-draw/gift and three-use quota do not leak into it.");
        Cold(g, registry);
    }

    public static void DianhuCountsRealDamagePointsAndJianjiKeepsItsNativeExactCard()
    {
        var (g, registry) = Start("huang-quan");
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "dragonboat:dianhu");
        Answer(g, c => c.Targets.SequenceEqual([1])); Play(g);
        Need(E<DragonBoatDianhuArmedEvent>(g).Single(e => e.OwnerSeat == 0).TargetSeat == 1,
            "Activity Dianhu freezes one actual game-start target.");
        Use(g, "damage-two", [1]);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        Need(Paid(g, "dragonboat:dianhu").Count() == 1, "A two-point actual damage pauses after its first independently paid point draw.");
        Cold(g, registry); Play(g);
        Need(Paid(g, "dragonboat:dianhu").Count() == 2, "Each actual point, rather than each damage event, draws one real card.");
        Use(g, "recover-two", [1]); Play(g);
        Use(g, "damage-two", [2]); Play(g);
        Need(Paid(g, "dragonboat:dianhu").Count() == 2, "The ordinary OL recovery rider and unrelated target damage are absent.");
        var jianji = g.GetHumanLegalActions().Single(a => a.ProgramSkillId == "dragonboat:jianji");
        Need(jianji.SelectableTargetSeats.SequenceEqual([4]), "Activity Jianji publishes only the real teammate as its target.");
        Accept(g, new UseProgramSkillCommand(0, "dragonboat:jianji", "draw-and-offer-exact-card", [], [4], g.Revision, P(g)!.PromptId));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.JianjiUse is not null));
        var native = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.JianjiUse is not null);
        Need(native.SourceSeat == 4 && native.JianjiUse!.ActorSeat == 4 && native.Action is { } action &&
            action.ProviderSeat == 4 && action.PhysicalCards is [var material] && material.CardId == native.JianjiUse.CardId &&
            E<HuangQuanDrawPaidEvent>(g).Single(e => e.Source.OwnerSeat == 0 && e.Source.SkillId == "dragonboat:jianji").CardCount == 1,
            "Jianji's recipient issues an actual native card use using exactly the one real card drawn by the paid request.");
        Cold(g, registry); Play(g);
        Need(E<JianjiUseReturnedEvent>(g).Count(e => e.Receipt.Source.OwnerSeat == 0 && e.Receipt.Source.SkillId == "dragonboat:jianji") == 1 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == "dragonboat:jianji"),
            "The true use returns once and consumes the owner's one actual-Play Jianji use.");
        Cold(g, registry);
    }

    public static void XingzhaoSharesThresholdBenefitsAndWenjiTunjiangUseTeamRules()
    {
        var (g, registry) = Start("tang-zi"); Play(g);
        Use(g, "damage-two", [1]); Play(g);
        Need(V(g, 0).Skills!.Any(s => s.Id == "dragonboat:xunxun") && V(g, 4).Skills!.Any(s => s.Id == "dragonboat:xunxun") &&
            !V(g, 1).Skills!.Any(s => s.Id == "dragonboat:xunxun"),
            "Two actual team treasures grant the self-contained activity Xunxun to Tang Zi and the teammate, independently of ordinary OL Tang Zi.");
        Use(g, "damage-two", [1]); Play(g);
        var equipmentSequence = g.CardMovements.Count;
        var equipmentHand = V(g, 0).HandCount;
        Native(g, LegalActionKind.Equip, 0);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var equipmentDraw = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OwnerSeat == 0 && f.SkillId == "dragonboat:xingzhao");
        Cold(g, registry); Play(g);
        Need(E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == equipmentDraw.Id && e.OwnerSeat == 0 && e.SkillId == "dragonboat:xingzhao" &&
                e.BindingId == "team-four-equipment" && e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && e.Activated && e.Completed) == 1 &&
            g.CardMovements.Skip(equipmentSequence).Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.dragonboat:xingzhao.Draw") == 1 && V(g, 0).HandCount == equipmentHand,
            "At four treasures an actual native equipment use runs Xingzhao's draw and its real child window.");
        Use(g, "damage-two", [1]); Play(g);
        var before = V(g, 0).Hand.Select(c => c.Id).ToArray();
        var movement = g.CardMovements.Count;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0));
        Need(before.All(id => V(g, 0).Hand.Any(c => c.Id == id)) &&
            !g.CardMovements.Skip(movement).Any(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "Six actual treasures skip the real discard phase while a hand above its limit retains every entity.");
        Cold(g, registry);

        var (w, wr) = Start("liu-qi");
        Until(w, () => Optional(w, "dragonboat:wenji"));
        Answer(w, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(w, () => P(w)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wenji") == true);
        Need(P(w)!.Choices.All(c => c.Targets.SequenceEqual([4])), "Activity Wenji can request only the teammate's actual gift.");
        Cold(w, wr); Answer(w, c => c.Targets.SequenceEqual([4])); Play(w);
        var gift = E<WenjiGiftPaidEvent>(w).Single(e => e.Source.OwnerSeat == 0);
        Need(gift.RecipientSeat == 4 && E<WenjiNameArmedEvent>(w).Single(e => e.Grant.Source.OwnerSeat == 0).Grant.Name == gift.Name,
            "The real teammate chooses and pays the actual name whose native responses are prohibited for the current turn.");
        Cold(w, wr);
        Accept(w, new EndPlayPhaseCommand(0, w.Revision, P(w)!.PromptId));
        Until(w, () => Optional(w, "dragonboat:tunjiang"));
        Answer(w, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(w, () => P(w)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "dragonboat-general") == true);
        Need(P(w)!.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([0]),
            "An undamaging friendly actual turn offers only Liu Qi or that turn owner, deduplicating his own ending.");
        // This is Liu Qi's own ending: the only distinct beneficiary is himself.
        Answer(w, c => c.Targets.SequenceEqual([0]));
        Until(w, () => Paid(w, "dragonboat:tunjiang").Any());
        Need(Paid(w, "dragonboat:tunjiang").Single().RecipientSeat == 0 && Paid(w, "dragonboat:tunjiang").Single().RequestedCount == 2,
            "The activity draws exactly two for the chosen beneficiary rather than using ordinary OL's faction-count/untargeted-Play rule.");
        Cold(w, wr);
    }

    private static IEnumerable<DragonBoatDrawPaidEvent> Paid(GameEngine g, string skill) => E<DragonBoatDrawPaidEvent>(g).Where(e => e.OwnerSeat == 0 && e.SkillId == skill);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 8).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Optional(GameEngine g, string skill) => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, LegalActionKind kind, int target)
    {
        var action = g.GetHumanLegalActions().First(a => a.Kind == kind && a.CardId is not null &&
            (kind == LegalActionKind.Equip || a.TargetSeats.SequenceEqual([target])));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource });
    }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Until(GameEngine g, Func<bool> done, bool endPlays = false)
    {
        for (var n = 0; n < 4000; n++)
        {
            if (done()) return;
            var p = P(g);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && endPlays)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p is { PlayerSeat: 0 } && p.Kind != DecisionKind.PlayCard && p.Kind != DecisionKind.SelectGeneral)
            {
                var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "dragonboat-general") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") != "activate");
                Need(choice is not null, "The bounded Dragon Boat fixture needs an explicit native response.");
                Accept(g, new AnswerPromptCommand(0, p.PromptId, choice!.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException($"Dragon Boat general boundary missing: {g.CreateSnapshot(0).Status}/{P(g)?.Kind}/{P(g)?.SkillPrompt?.SkillId}; frames={string.Join(',', g.ResolutionStack.Select(f => f.Kind))}");
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Dragon Boat general command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 8).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements) &&
            g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).SequenceEqual(copy.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
            "All private player views, exact team gifts, native drawn entities and child receipts cold-replay from actual commands.");
    }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:wei", ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        var general = P(g)!.Choices.First(c => !c.Parameters.GetValueOrDefault("general-id", "").StartsWith("dragonboat:", StringComparison.Ordinal));
        var id = general.Parameters.GetValueOrDefault("general-id") ?? general.Id.Value;
        Accept(g, new SelectGeneralCommand(0, id, g.Revision, P(g)!.PromptId));
        return (g, registry);
    }
    private static string Embedded(string suffix)
    {
        using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.dragonboat-activity." + suffix)!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("dragonboat-general-fixture", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog)
            {
                foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) });
            }
            Add(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hand","query":"initialHandSize","operation":"add","value":8,"priority":0},{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"activations":[
                {"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":8,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                {"id":"recover-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":8,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]},
                {"id":"self-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"damage-twenty","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.dragonboat:lianpian.draw","skill-program.dragonboat:dianhu.draw","skill-program.dragonboat:xingzhao.Draw","skill-program.wenji.gift","skill-program.dragonboat:tunjiang.draw"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]},
                {"id":"native-foreign-use","window":"cardUseTargetsFinalized","ownerRelation":"observer","cardKinds":["slash"],"condition":{"kind":"not","children":[{"kind":"cardActionActorIsCurrentTurn"}]},"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}
              ]}
            ]}
            """;
            Add(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "Native driver", description = "Native actual damage and cards." },
                    [Quiet] = new { name = "Quiet", description = "Skip foreign play." },
                    [Watch] = new { name = "Actual paid child", description = "Real child checkpoint.", optionLabels = new Dictionary<string, string> { ["continue"] = "Continue" } } } })));
            var skills = scenario switch { "lianpian" => new[] { "dragonboat:lianpian" }, "huang-quan" => new[] { "dragonboat:dianhu", "dragonboat:jianji" },
                "tang-zi" => new[] { "dragonboat:xingzhao" }, "rules" => [], _ => new[] { "dragonboat:wenji", "dragonboat:tunjiang" } };
            var factions = new[] { "wei", "shu", "wu", "qun" };
            var pools = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var faction in factions)
            {
                var ids = Enumerable.Range(0, 12).Select(i => $"fixture:dragonboat-{faction}-{i}").ToArray();
                foreach (var id in ids) b.AddGeneral(new(id, id, "", faction == "wei" ? Driver : Quiet, faction,
                    faction == "wei" ? 4 : 20, faction == "wei" ? skills.Append(Watch).ToArray() : [Watch]) { CharacterId = "character:" + id });
                pools["team:" + faction] = ids;
            }
            foreach (var id in new[] { "su-fei", "huang-quan", "tang-zi", "liu-qi" })
                b.AddGeneral(new("dragonboat:" + id, id, "", Quiet, "qun", 4) { CharacterId = "character:" + id });
            var special = new Dictionary<string, string[]> { ["wei"] = ["huang-quan", "tang-zi"], ["shu"] = ["huang-quan", "liu-qi"],
                ["wu"] = ["su-fei", "tang-zi"], ["qun"] = ["su-fei", "liu-qi"] };
            foreach (var faction in factions) pools["team:" + faction] = pools["team:" + faction].Concat(special[faction].Select(id => "dragonboat:" + id)).ToArray();
            const string deck = "fixture:dragonboat-general-deck";
            var kind = scenario == "tang-zi" ? "standard:crossbow" : scenario == "huang-quan" ? "standard:slash" : "standard:duel";
            b.AddDeck(new(deck, deck, 4, 0, []) { PhysicalCards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(kind, Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, Mode, 8, 8, new Dictionary<string, int>(), deck, 6, pools.Values.SelectMany(p => p).Distinct().ToArray(), ContentModeKind.Team,
                pools.Keys.ToDictionary(t => t, _ => 2)) { DragonBoatActivity = new(pools, PlayerTier: 1, Rounds: 2) });
        }
    }
}
