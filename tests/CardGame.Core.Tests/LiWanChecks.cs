using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiWanChecks
{
    private const string Lianju = "ol:lianju", Silv = "ol:silv", Driver = "fixture:lw-driver", Watch = "fixture:lw-watch", Owner = "fixture:lw-owner", Mode = "identity:classic-li-wan-fixture";
    public static void LianjuActualUsesSameColorGiftAndZeroReturnCold()
    {
        var (g, r) = Start();
        Native(g, LegalActionKind.Alcohol); Play(g); Native(g, LegalActionKind.Slash, target: 1); Play(g);
        var actual = E<LianjuEntityUsedEvent>(g).Where(e => e.ActualTurn == 1 && e.ActorSeat == 0).Select(e => e.CardId).Distinct().ToArray();
        Need(actual.Length == 2 && actual.All(id => g.CardMovements.Last(m => m.CardId == id).To == CardLocation.DiscardPile), "Lianju records the exact real used materials, including converted cards, after native cleanup.");
        End(g); Reach(g, () => Offer(g, "same-color-gift")); g = Cold(g, r); Activate(g); Gift(g, false, 1, actual);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var paid = E<LiWanPaidEvent>(g).Single();
        Need(paid.Count == 2 && V(g, 1).Hand.Where(c => actual.Contains(c.Id)).All(c => c.SkillMarkLabel == "闾") &&
            V(g, 1).Hand.Count(c => actual.Contains(c.Id)) == 2 && Enumerable.Range(0, 4).Where(s => s != 1).All(s => g.CreateSnapshot(s).Players[1].Hand.Count == 0),
            "The same-color two-card native gain is paid before real gain children; only visible card faces show the persistent mark.");
        g = Cold(g, r); Continue(g); Reach(g, () => Offer(g, "issued-opposite-color"));
        var due = g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        Need(due.OwnerSeat == 1 && E<TurnEndedEvent>(g).All(e => e.TurnNumber != due.TurnNumber), "The return occurs at the recipient's actual next Ending, before TurnEnded.");
        Activate(g); Reach(g, () => Branch(g, "finish"));
        Need(P(g)!.Choices.All(c => c.Cards.Count == 0), "Black first gifts require red return entities; unrelated black used cards are excluded.");
        g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Reach(g, () => E<LiWanCompletedEvent>(g).Length == 2);
        Need(E<LiWanPaidEvent>(g).Last().Count == 0 && E<LianjuPromiseConsumedEvent>(g).Length == 1 && E<SilvEntityMarkedEvent>(g).Select(e => e.CardId).Distinct().Count() == 2,
            "A zero-card opposite-color return is legal and consumes the original promise once without duplicating marks.");
        _ = Cold(g, r);
        {
            var (changed, registry) = Start("actor"); Native(changed, LegalActionKind.Slash, target: 1);
            Reach(changed, () => P(changed)?.SkillPrompt?.SkillId == "fixture:lw-role" && P(changed)!.Choices.Any(c => c.Targets.SequenceEqual([3])));
            Answer(changed, c => c.Targets.SequenceEqual([3]));
            Reach(changed, () => P(changed)?.SkillPrompt?.SkillId == "fixture:lw-role" && P(changed)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
            var changedUse = changed.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Action is { ActorSeat: 3, ProviderSeat: 0 });
            var original = E<LianjuEntityUsedEvent>(changed).Single(e => e.UseFrameId == changedUse.Id && e.ActionId == changedUse.Action!.ActionId);
            Need(changedUse.SourceSeat == 3 && original.ActorSeat == 0 && E<LianjuUseActorChangedEvent>(changed) is [{ PreviousActor: 0, Actor: 3, Provider: 0 }],
                "A real native actor replacement retains the original accepted use/material provider and issues an exact final-user chain.");
            changed = Cold(changed, registry); Answer(changed, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(changed);
            Need(E<CardUseFinishedEvent>(changed).Count(e => e.ResolutionId == changedUse.Id) == 1 && E<LianjuEntityUsedEvent>(changed).Count(e => e.CardId == original.CardId && e.UseFrameId == changedUse.Id) == 1,
                "The changed-source native use resolves once without rewriting the original use invoice or duplicating its physical material.");
            End(changed); Reach(changed, () => Offer(changed, "same-color-gift")); Activate(changed);
            Reach(changed, () => Branch(changed, "black")); Answer(changed, c => c.Parameters.GetValueOrDefault("branch") == "black"); Answer(changed, c => c.Targets.SequenceEqual([1]));
            Need(P(changed)!.Choices.All(c => c.Cards.Count == 0), "The original physical provider cannot reclaim the card through her own Lianju after its actual user became another actor.");
            changed = Cold(changed, registry); Answer(changed, c => c.Parameters.GetValueOrDefault("branch") == "finish");
            Reach(changed, () => E<LiWanCompletedEvent>(changed).Length == 1); _ = Cold(changed, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool Action(GameEngine g, string action) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static bool Branch(GameEngine g, string branch) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static bool Offer(GameEngine g, string binding) => P(g)?.SkillPrompt?.SkillId == Lianju && Action(g, "activate") &&
        g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any(f => f.Items[f.ItemIndex].Candidate?.BindingId == binding);
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Li Wan command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the native human chooser may answer."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Native(GameEngine g, LegalActionKind kind, int? card = null, int? target = null)
    {
        var a = g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == kind && a.CardId is not null && (card is null || a.CardId == card) && (target is null || a.TargetSeats.SequenceEqual([target.Value])))
            ?? throw new InvalidOperationException("Missing native Li Wan action " + kind + ": " + Diagnostic(g));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Gift(GameEngine g, bool red, int recipient, IReadOnlyList<int> cards)
    {
        Reach(g, () => Branch(g, "red")); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == (red ? "red" : "black"));
        Answer(g, c => c.Targets.SequenceEqual([recipient])); foreach (var id in cards) Answer(g, c => c.Cards.SequenceEqual([id]));
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 360; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "The native Li Wan fixture ended before its boundary: " + Diagnostic(g));
            var p = P(g);
            if (p is { Kind: DecisionKind.PlayCard } && V(g, p.PlayerSeat).IsHuman) End(g);
            else if (p is not null && V(g, p.PlayerSeat).IsHuman)
            {
                if (Action(g, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RespondDodge && p.Choices.Any(c => c.Cards.Count > 0)) Answer(g, c => c.Cards.Count > 0);
                else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (p.SkillPrompt?.SkillId == Watch) Continue(g);
                else throw new InvalidOperationException("Unexpected native Li Wan prompt: " + Diagnostic(g));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing native Li Wan boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g),
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Hp, V(g, s).IsAlive, V(g, s).HandCount }), Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold replay preserves every viewer, native card label, private selector and paid event cursor."); return restored; }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario is "death" or "dead-issuer" ? Role.Rebel : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-li-wan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:li-wan");
            var defs = new List<string>
            {
                $$"""{"id":"fixture:lw-hp-source","revision":1,"hpSkillGrants":[{"id":"lianju-live","skillId":"{{Lianju}}","minimumHp":14},{"id":"silv-live","skillId":"{{Silv}}","minimumHp":14}]}""",
                """{"id":"fixture:lw-peer","revision":1,"viewAs":[{"id":"native-slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}]}""",
                $$"""
                {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"native-slash","inputKinds":["dodge"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"native-alcohol","inputKinds":["dodge"],"inputSuits":[],"outputKind":"alcohol","forPlay":true,"forResponse":false},{"id":"native-aoe","inputKinds":["dodge"],"inputSuits":[],"outputKind":"arrowBarrage","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],"activations":[
                  {"id":"poke-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"poke-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                  {"id":"drop","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":4}]},
                  {"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                  {"id":"dead-issuer-clear-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"continueAfterOwnerDeath":true,"effects":[{"op":"loseHp","target":"owner","amount":20},{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"discard-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":2,"maximumCards":2,"resultBind":"discard"},{"op":"moveBoundCards","target":"owner","sourceBind":"discard","destination":"discardPile","awaitMovementTriggers":true}]},
                  {"id":"give-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedHandRankSum","target":"owner","exactRankSum":7,"resultBind":"gift"},{"op":"moveBoundCards","target":"owner","sourceBind":"gift","destination":"selectedTargetHand","awaitMovementTriggers":true}]},
                  {"id":"foreign-discard","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]}
                """,
                $$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.li-wan.lianju.gain","skill-program.li-wan.silv.draw","skill-program.li-wan.silv.refund"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]}"""
            };
            if (scenario == "death") defs.Add("""{"id":"fixture:lw-refund-death","revision":1,"triggers":[{"id":"actual-refund-death","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.li-wan.silv.refund"],"priority":-100,"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}""");
            if (scenario == "actor") defs.Add("""{"id":"fixture:lw-role","revision":1,"triggers":[{"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"role-return","options":[{"id":"continue"}]}]}]}""");
            if (scenario == "dead-issuer") defs.Add("""{"id":"fixture:lw-live-holder-discard","revision":1,"triggers":[{"id":"actual-live-holder-self-discard","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"holder-discard"},{"op":"moveBoundCards","target":"owner","sourceBind":"holder-discard","destination":"discardPile","awaitMovementTriggers":true}]}]}""");
            var names = defs.Select(d => JsonNode.Parse(d)!).ToDictionary(d => d["id"]!.GetValue<string>(), d => new { name = d["id"]!.GetValue<string>(), description = "真实李婉命令夹具",
                optionLabels = d["id"]!.GetValue<string>() is Watch or "fixture:lw-role" ? new Dictionary<string, string> { ["continue"] = "继续真实子窗" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际李婉行为") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:lw-owner-pick"; b.AddSkill(new(pick, "固定本人选将", "选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000d) });
            b.AddGeneral(new(Owner, "实际李婉", "ol-li-wan", pick, "wei", 16, new[] { Driver, Watch, "fixture:lw-hp-source" }
                .Concat(scenario == "death" ? ["fixture:lw-refund-death"] : []).Concat(scenario == "actor" ? ["fixture:lw-role"] : []).ToArray(), GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:lw-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", "fixture:lw-peer", "wei", 12,
                new[] { Watch }.Concat(scenario == "dead-issuer" ? ["fixture:lw-live-holder-discard"] : []).ToArray()));
            b.AddDeck(new("fixture:lw-deck", "固定原生实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "李婉原生边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:lw-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
