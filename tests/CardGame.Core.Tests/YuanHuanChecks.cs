using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YuanHuanChecks
{
    private const string Deru = "ol:deru", Linjie = "ol:linjie", Owner = "fixture:yh-owner", Driver = "fixture:yh-driver", Watch = "fixture:yh-watch", Health = "fixture:yh-health", Mode = "identity:classic-yuan-huan-fixture";
    private const string Take = "skill-program.yuan-huan.deru.take", Fill = "skill-program.yuan-huan.deru.fill", Discard = "skill-program.yuan-huan.linjie.discard", Draw = "skill-program.yuan-huan.linjie.draw";
    public static void DeruPresenceSetsAllNoneAndOpaqueRandomPrivacyCold()
    {
        foreach (var guess in new[] { 15, 0, 14 })
        {
            var (g, r) = Start(); Use(g, "lose", [0]); Play(g); var hp = V(g, 0).Hp; var ownHand = V(g, 0).HandCount; var targetIds = V(g, 1).Hand.Select(c => c.Id).ToArray();
            Begin(g, 1); var p = P(g)!;
            Need(p is { PlayerSeat: 0, IsPrivate: true } && p.Choices.Count == 16 && p.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) && Root(g).YuanHuan is { GuessMask: null, ActualMask: null },
                "All 16 four-name presence guesses, including none and all, are privately offered independently of the target's real hidden hand.");
            Reject(g, new("deru-forged-mask-16")); g = Cold(g, r); Guess(g, guess);
            if (guess != 14)
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
                Need(V(g, 0).Hp == hp + 1 && V(g, 1).Hand.Select(c => c.Id).SequenceEqual(targetIds) && Root(g).YuanHuan is { Stage: YuanHuanStage.RecoveryChildren, BasicCandidates: null, GuessMask: var selected, ActualMask: 1 } && selected == guess,
                    "Matching presence or absence heals before the random take; the original comparison is frozen while the real native recovery child is open.");
                g = Cold(g, r); Continue(g);
            }
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var frame = Root(g); var receipt = frame.YuanHuan!; var movement = E<YuanHuanStepIssuedEvent>(g).Single(e => e.FrameId == frame.Id && e.Reason == Take);
            Need(receipt is { Stage: YuanHuanStage.TakeChildren, ActualMask: 1 } && receipt.GuessMask == guess && receipt.BasicCandidates!.SequenceEqual(targetIds) &&
                movement is { ActualCount: 1, CardId: { } card } && receipt.BasicCandidates![receipt.RandomIndex] == card && V(g, 0).HandCount == ownHand + 1 &&
                V(g, 0).Hand.Any(c => c.Id == card) && V(g, 1).HandCount == targetIds.Length - 1 && Enumerable.Range(1, 3).All(s =>
                    g.CreateSnapshot(s).Players[0].Hand.Count == 0 && !g.CreateSnapshot(s).PublicRevealedCards.Any(c => c.Id == card)),
                "A mismatch obtains one exact native-random basic hand entity from the frozen full candidate set, without exposing the obtained hand or unselected private candidates to peers.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(V(g, 0).Hp == hp + (guess == 14 ? 0 : 1) && E<YuanHuanStepIssuedEvent>(g).Count(e => e.Reason == Take && e.ActualCount == 1) == 1 &&
                E<YuanHuanStepIssuedEvent>(g).Single(e => e.Reason == Fill).ActualCount == 0 && E<YuanHuanCompletedEvent>(g).Count(e => e.FrameId == frame.Id) == 1,
                "All-selected and none-selected guesses may overlap heal and steal; the exact four-name opposite mask gets no heal, and partial correctness never receives the all-correct refill.");
            var before = State(g); var second = g.Submit(new UseProgramSkillCommand(0, Deru, "guess-basic-names", [], [1], g.Revision, P(g)!.PromptId));
            Need(!second.Accepted && State(g) == before, "The same real Play phase cannot activate Deru a second time."); _ = Cold(g, r);
        }
    }
    public static void DeruExactEmptyTargetAndNativeIdentityNamesCold()
    {
        {
            var (g, r) = Start(); Use(g, "empty-other", [1]); Play(g); Use(g, "empty-own"); Play(g); Use(g, "lose", [0]); Play(g);
            Need(V(g, 1).HandCount == 0 && V(g, 0).HandCount == 0, "The empty-target scenario pays real native hand discards.");
            Begin(g, 1); Need(P(g)!.Choices.Count == 16, "A truly handless target does not prune any possible Deru guess."); g = Cold(g, r); Guess(g, 0);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); g = Cold(g, r); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var frame = Root(g); var receipt = frame.YuanHuan!;
            Need(receipt is { GuessMask: 0, ActualMask: 0, Stage: YuanHuanStage.FillChildren, BasicCandidates.Count: 0, RandomIndex: -1 } &&
                E<YuanHuanStepIssuedEvent>(g).Single(e => e.FrameId == frame.Id && e.Reason == Take).ActualCount == 0 && V(g, 0).Hp == V(g, 0).MaxHp && V(g, 0).HandCount == V(g, 0).Hp,
                "Guessing no basic names against a real empty hand is exact set equality: heal first, no invented steal or RNG payment, then actual draw up to the newly recovered current HP.");
            g = Cold(g, r); Continue(g); Play(g); Need(E<YuanHuanCompletedEvent>(g).Count(e => e.FrameId == frame.Id) == 1, "The actual large refill returns only once.");
        }
        foreach (var scenario in new[] { "fire", "thunder", "identity", "view-as" })
        {
            var (g, r) = Start(scenario); Begin(g, 1); Guess(g, scenario == "view-as" ? 0 : 15); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var frame = Root(g); var receipt = frame.YuanHuan!;
            Need(receipt.ActualMask == (scenario == "view-as" ? 0 : 1), "All native elemental Slashes and actual hand identities share the Slash name; a merely available view-as conversion does not change a hidden entity's current name.");
            if (scenario == "identity") Need(receipt is { TakenKind: CardKind.FireSlash, TakenPrintedKind: CardKind.Crossbow } &&
                E<YuanHuanStepIssuedEvent>(g).Single(e => e.FrameId == frame.Id && e.Reason == Take).CardId is { } id && V(g, 0).Hand.Single(c => c.Id == id).Kind == CardKind.Crossbow,
                "The true hand-only identity qualifies before the opaque payment, and the original printed equipment entity correctly returns to its own name after changing holder.");
            if (scenario == "view-as") Need(receipt is { GuessMask: 0, ActualMask: 0, BasicCandidates.Count: 0, Stage: YuanHuanStage.FillChildren }, "Optional Crossbow-to-Slash view-as choices do not fabricate basic-hand candidates.");
            g = Cold(g, r); Continue(g); Play(g);
        }
    }
    public static void LinjieActualSourceChooserHandDiscardAndNoSourceCold()
    {
        foreach (var scenario in new[] { "plain", "identity", "view-as" })
        {
            var (g, r) = Start(scenario); var hp = V(g, 0).Hp; var ownHand = V(g, 0).HandCount; var sourceIds = V(g, 1).Hand.Select(c => c.Id).ToArray();
            Use(g, "incoming", [1]); Reach(g, () => Action(g, "discard")); var p = P(g)!; var frame = Root(g);
            Need(p is { PlayerSeat: 1, IsPrivate: true } && p.Choices.All(c => c.Cards.Count == 1 && sourceIds.Contains(c.Cards[0])) &&
                Enumerable.Range(0, 4).Where(s => s != 1).All(s => g.CreateSnapshot(s).PendingDecision is null) && frame.YuanHuan is { TargetSeat: 1, DamageAmount: 2, Stage: YuanHuanStage.SourceDiscard } && V(g, 0).Hp == hp - 2,
                "One real two-point damage instance names its original actual source as the only private chooser of an actual owned hand discard.");
            var before = State(g); Need(!g.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && State(g) == before, "The skill owner cannot answer the damage source's concealed payment.");
            g = Cold(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision));
            if (scenario != "view-as") { Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); g = Cold(g, r); Continue(g); }
            Play(g); var discard = E<YuanHuanStepIssuedEvent>(g).Single(e => e.FrameId == frame.Id && e.Reason == Discard);
            Need(discard is { ActualCount: 1, CardId: { } card, From.OwnerSeat: 1 } && g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == Discard) == 1 &&
                V(g, 0).HandCount == ownHand + (scenario == "view-as" ? 0 : 1) && E<YuanHuanCompletedEvent>(g).Count(e => e.FrameId == frame.Id) == 1,
                "The exact source hand entity is really discarded once; only its frozen effective Slash name rewards one actual draw, while optional view-as equipment does not.");
            var starts = E<YuanHuanStartedEvent>(g).Length; Use(g, "source-less"); Play(g); Use(g, "empty-other", [1]); Play(g); Use(g, "incoming", [1]); Play(g);
            Need(E<YuanHuanStartedEvent>(g).Length == starts && E<DamageAppliedEvent>(g).Any(e => e.SourceLess), "Source-less actual damage and a genuinely emptied damage source cannot invent a Linjie hand cost or reward.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start(); var count = V(g, 0).HandCount; Use(g, "self"); Reach(g, () => Action(g, "discard")); Need(P(g)!.PlayerSeat == 0, "Actual sourced self-damage makes the owner the real Linjie payer.");
            g = Cold(g, r); Answer(g, c => c.Cards.Count == 1); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); g = Cold(g, r); Continue(g); Play(g);
            Need(V(g, 0).HandCount == count && E<YuanHuanStepIssuedEvent>(g).Count(e => e.Reason == Discard) == 1 && E<YuanHuanStepIssuedEvent>(g).Count(e => e.Reason == Draw && e.ActualCount == 1) == 1,
                "Sourced self-damage pays one real hand discard and then one reward, without duplicating a source/target branch.");
        }
    }
    public static void YuanHuanPaidRecoveryGainLossDeathAndNativeChildrenCold()
    {
        foreach (var scenario in new[] { "loss", "death" })
        {
            var (g, r) = Start(scenario); Begin(g, 1); Guess(g, 15); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); var frame = Root(g); var paid = E<YuanHuanStepIssuedEvent>(g).Single(e => e.FrameId == frame.Id && e.Reason == Take);
            g = Cold(g, r); Continue(g);
            if (scenario == "loss")
            {
                Reach(g, () => Option(g, "after-loss")); Need(V(g, 0).Skills!.All(s => s.ContentId != Deru) && E<YuanHuanCompletedEvent>(g).All(e => e.FrameId != frame.Id), "A genuine gained-card child removes the original conditional Deru source before its paid parent returns.");
                g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-loss"); Play(g);
            }
            else
            {
                Reach(g, () => g.ResolutionStack.OfType<DyingFrame>().Any() || !V(g, 0).IsAlive); g = Cold(g, r);
                Reach(g, () => !V(g, 0).IsAlive && E<YuanHuanCompletedEvent>(g).Any(e => e.FrameId == frame.Id));
                Need(g.State.Winner == Winner.None && E<PlayerDyingEvent>(g).Any() && E<DyingResolvedEvent>(g).Any(e => !e.Survived), "Actual non-Lord owner death drains its genuine native dying subtree without ending the whole match.");
            }
            Need(E<YuanHuanStepIssuedEvent>(g).Count(e => e.FrameId == frame.Id && e.Reason == Take) == 1 && g.CardMovements.Count(m => m.CardId == paid.CardId && m.Reason.Value == Take) == 1 && E<YuanHuanCompletedEvent>(g).Count(e => e.FrameId == frame.Id) == 1,
                "Cold recovery and skill loss or death cannot reroll the chosen entity, pay a completed take twice, or strand the original owning cursor."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("redirect"); var lord = g.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat; Need(lord != 0, "The real recovering owner is a non-Lord.");
            Use(g, "lose", [0]); Play(g); Use(g, "lose", [lord]); Play(g); var hp = V(g, 0).Hp; var lordHp = V(g, lord).Hp;
            Begin(g, 1); Guess(g, 15); Reach(g, () => g.ResolutionStack.OfType<RecoveryReplacementFrame>().Any()); var frame = Root(g); var recovery = g.ResolutionStack.OfType<RecoveryReplacementFrame>().Single();
            Need(recovery.ParentFrameId == frame.Id && recovery.Attempt is { SourceSeat: 0, TargetSeat: 0, Amount: 1 } && recovery.Return is { Continuation: PostEventContinuation.AwaitedProgramMovement, ResumeFrameId: var parent } && parent == frame.Id,
                "The accepted Deru heal issues a real recovery replacement with the exact owning frame and original instruction return.");
            g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("recovery-action") == "redirect" && c.Targets.SequenceEqual([lord]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health && E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == lord));
            Need(V(g, 0).Hp == hp && V(g, lord).Hp == lordHp + 1 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.TargetSeat == lord), "The native replacement truly heals the eligible wounded Lord and opens its real health child, while the original Deru owner stays wounded.");
            g = Cold(g, r); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch); g = Cold(g, r); Continue(g); Play(g);
            Need(E<YuanHuanStepIssuedEvent>(g).Count(e => e.FrameId == frame.Id && e.Reason == Take && e.ActualCount == 1) == 1 && E<YuanHuanCompletedEvent>(g).Count(e => e.FrameId == frame.Id) == 1, "The native recovery and provider-draw children return before one genuine random take."); _ = Cold(g, r);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.YuanHuan is not null);
    private static bool Action(GameEngine g, string branch) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "yuan-huan" && c.Parameters.ContainsKey(branch)) == true;
    private static bool Option(GameEngine g, string id) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
    private static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real YuanHuan command."); }
    private static void Answer(GameEngine g, Func<PromptChoice,bool> predicate) { var p = P(g)!; var choices = p.Choices.Where(predicate).ToArray(); Need(V(g, p.PlayerSeat).IsHuman && choices.Length > 0, "The exact human-owned native choice is absent: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choices[0].Id, g.Revision)); }
    private static void Guess(GameEngine g, int mask) => Answer(g, c => c.Parameters.GetValueOrDefault("guess-mask") == mask.ToString());
    private static void Begin(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Deru, "guess-basic-names", [], [target], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 250; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "YuanHuan fixture ended before the actual native boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && V(g, p.PlayerSeat).IsHuman)
            {
                if (p.Kind == DecisionKind.PlayCard) Accept(g, new EndPlayPhaseCommand(p.PlayerSeat, g.Revision, p.PromptId));
                else if (p.SkillPrompt?.SkillId is Health or Watch && Option(g, "continue")) Continue(g);
                else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected native YuanHuan prompt: " + Diagnostic(g));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual YuanHuan boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(5).Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }) } : null,
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(5).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold JSON replay preserves hidden guesses, exact frozen names, random state, native private chooser and paid child cursor."); return restored; }
    private static void Reject(GameEngine g, ChoiceId choice) { var before = State(g); var p = P(g)!; Need(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice, g.Revision)).Accepted && State(g) == before, "An unpublished guess cannot expose a hand or change a paid cursor."); }
    private static (GameEngine,ContentRegistry) Start(string scenario = "plain")
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario is "death" or "redirect" ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-yuan-huan", new(1,0,0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:yuan-huan");
            var defs = new List<string>
            {
                $$"""
                {"id":"{{Driver}}","revision":1,"activations":[
                 {"id":"lose","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                 {"id":"empty-own","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},
                 {"id":"empty-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","amount":64,"zones":["hand"]}]},
                 {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                 {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                 {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]}]},
                """.TrimEnd(','),
                $$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}""",
                """{"id":"fixture:yh-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""
            };
            var tail = scenario == "loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"lost\",\"options\":[{\"id\":\"after-loss\"}]}" : scenario == "death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
            defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Take}}","{{Fill}}","{{Draw}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}{{tail}}]}]}""");
            var peerExtra = new List<string> { Health };
            if (scenario == "identity") { peerExtra.Add("fixture:yh-identity"); defs.Add("""{"id":"fixture:yh-identity","revision":1,"cardIdentities":[{"id":"real-hand-fire-slash","zones":["hand"],"inputKinds":["crossbow"],"inputSuits":[],"outputKind":"fireSlash"}]}"""); }
            if (scenario == "view-as") { peerExtra.Add("fixture:yh-view-as"); defs.Add("""{"id":"fixture:yh-view-as","revision":1,"viewAs":[{"id":"optional-slash","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true}]}"""); }
            if (scenario == "loss") defs.Add($$"""{"id":"fixture:yh-hp-source","revision":1,"hpSkillGrants":[{"id":"healthy-deru","skillId":"{{Deru}}","minimumHp":17}]}""");
            if (scenario == "redirect") { peerExtra.Add("fixture:yh-replacement"); defs.Add("""{"id":"fixture:yh-replacement","revision":1,"cardPolicies":[{"id":"real-lord-recovery","kind":"redirectOwnTurnFactionRecovery","factionId":"qun","ownerRole":"lord","value":1,"providerDrawCount":1}]}"""); }
            var labels = defs.Select(d => JsonNode.Parse(d)!).ToDictionary(d => d["id"]!.GetValue<string>(), d => new { name = d["id"]!.GetValue<string>(), description = "袁涣真实原生夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(key => d[key]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',', defs)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "原生规则夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:yh-pick", "固定本人选将", "公开角色选择评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000d) });
            b.AddGeneral(new(Owner, "实际袁涣", "ol-yuan-huan", "fixture:yh-pick", "qun", 16, [Driver, Watch, Health, Linjie, scenario == "loss" ? "fixture:yh-hp-source" : Deru], GeneralGender.Male));
            var peers = Enumerable.Range(1,3).Select(i => $"fixture:yh-peer-{i}").ToArray(); foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", "fixture:yh-quiet", "qun", 12, peerExtra));
            var kind = scenario switch { "fire" => CardKind.FireSlash, "thunder" => CardKind.ThunderSlash, "identity" or "view-as" => CardKind.Crossbow, _ => CardKind.Slash };
            b.AddCard(new("fixture:yh-material", "实际实体材料", kind == CardKind.Crossbow ? "装备牌" : "基本牌", "原生元素杀或实体身份区分", kind));
            b.AddDeck(new("fixture:yh-deck", "固定同质实体", 4,2,[]) { PhysicalCards = Enumerable.Range(0,64).Select(_ => new ContentDeckPhysicalCard("fixture:yh-material", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "袁涣真实行为边界", 4,4,new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }, "fixture:yh-deck", GeneralCandidateCount:4, GeneralPoolIds:[Owner,.. peers]));
        }
    }
}
