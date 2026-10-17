using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhouQunChecks
{
    private const string Owner = "fixture:zq-owner", Mode = "identity:classic-zhou-qun", Driver = "fixture:zq-driver", Quiet = "fixture:zq-quiet", Watch = "fixture:zq-watch", Conditional = "fixture:zq-conditional", Replacer = "fixture:zq-replacer";
    private static string Scene = "";

    public static void ChenshuoCompleteRepeatedPairsAndNativeGainCold()
    {
        var (g, registry) = Start("reveal", false);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "ol:chenshuo"); Activate(g); Reach(g, () => Stage(g, ZhouQunStage.ChooseHandOwner));
        Need(P(g)!.Choices.All(c => c.Cards.Count == 0), "The issuer selects a participant without seeing that participant's unshown Hand.");
        Branch(g, "hand-owner", target: 1); Reach(g, () => Stage(g, ZhouQunStage.ChooseHandOwner));
        Need(E<ChenshuoHandShownEvent>(g).Length == 1 && E<ChenshuoTopRevealedEvent>(g).Length == 1 && !g.CardMovements.Any(m => m.Reason.Value == "skill-program.zhou-qun.chenshuo.gain"), "One real Hand show and one top show precede the complete repeated target selection, rather than three automatic top flips.");
        g = Cold(g, registry); Branch(g, "hand-owner", target: 2); g = Cold(g, registry); Reach(g, () => Stage(g, ZhouQunStage.ChooseHandOwner));
        Branch(g, "hand-owner", target: 0); Reach(g, () => Stage(g, ZhouQunStage.ChooseShownHand)); var shown = V(g, 0).Hand[0].Id; Branch(g, "show-hand", card: shown); Reach(g, () => At(g, Watch));
        var f = Own(g)!; var revealed = f.ZhouQun!.Revealed.ToArray(); Frozen(f.ZhouQun.ShownCards); Frozen(f.ZhouQun.Revealed); g = Cold(g, registry); Continue(g); Play(g);
        Need(E<ChenshuoHandShownEvent>(g).Select(e => e.Card.OwnerSeat).SequenceEqual([1, 2, 0]) && E<ChenshuoTopRevealedEvent>(g).Length == 3 && E<ChenshuoTopRevealedEvent>(g).All(e => e.Matched), "Each of the three complete iterations has its own real selected Hand owner and frozen comparison; three is the deck reveal cap.");
        Need(revealed.All(id => V(g, 0).Hand.Any(c => c.Id == id)) && V(g, 0).Hand.Any(c => c.Id == shown) && g.CardMovements.Count(m => m.Reason.Value == "skill-program.zhou-qun.chenshuo.gain" && m.To == CardLocation.Hand(0)) == 3 && E<ZhouQunCompletedEvent>(g).Count(e => e.FrameId == f.Id) == 1, "The issuer gains only the three revealed deck entities after the real gain subtree; shown Hand cards stay with their holders and no gain repeats."); _ = Cold(g, registry);
        (g, registry) = Start("reveal-decline", false); Reach(g, () => At(g, "ol:chenshuo")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
        Need(E<ChenshuoHandShownEvent>(g).Length == 0 && E<ChenshuoTopRevealedEvent>(g).Length == 0, "Declining the genuine optional Preparation trigger reveals and gains no cards."); _ = Cold(g, registry);
    }

    public static void TianhouPrivateExchangeSourceLossAndExactNextInvocationCold()
    {
        var (g, registry) = Start("tianhou-loss"); Use(g, "permanent-weather"); Play(g); End(g); Reach(g, () => Stage(g, ZhouQunStage.ExchangeHand));
        var f = Own(g)!; var top = f.ZhouQun!.Viewed.ToArray(); var hand = V(g, 0).Hand[0].Id;
        Need(top.Length == 3 && top.All(id => g.CreateSnapshot(0).PrivateRevealedCards!.Any(c => c.Id == id)) && Enumerable.Range(1, 3).All(s => !(g.CreateSnapshot(s).PrivateRevealedCards ?? []).Any(c => top.Contains(c.Id))), "Only the true issuer privately sees the three original top entities before any exchange or public reveal.");
        var before = State(g); var p = P(g)!; Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && before == State(g), "Another viewer cannot submit the private exchange or pay its cost."); Frozen(f.ZhouQun.Viewed); g = Cold(g, registry);
        Branch(g, "hand", card: hand); Branch(g, "hand-done"); Branch(g, "top", card: top[0]); Reach(g, () => At(g, Watch)); g = Cold(g, registry); Continue(g); Reach(g, () => Stage(g, ZhouQunStage.ChooseWeatherCard));
        Need(!V(g, 0).Skills!.Any(s => s.ContentId == "ol:tianhou") && V(g, 0).Hand.Any(c => c.Id == top[0]) && !V(g, 0).Hand.Any(c => c.Id == hand), "The actual swap and its HP child remove the conditional Tianhou source after payment while preserving the native exchange result.");
        Branch(g, "weather-card", card: hand); Need(Enumerable.Range(0, 4).All(s => (g.CreateSnapshot(s).PublicRevealedCards ?? []).Any(c => c.Id == hand)), "Only the selected true top card is publicly revealed for the weather decision."); Branch(g, "weather-target", target: 0); var grant = E<TianhouWeatherGrantedEvent>(g).Single(); var turn = g.State.TurnNumber;
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > turn);
        Need(grant.SkillId == "ol:lieshu" && V(g, 0).Skills!.Any(s => s.ContentId == "ol:lieshu") && E<TianhouWeatherExpiredEvent>(g).Length == 0, "A paid weather grant survives source loss and intervening real turns; it does not expire at next Preparation or raw TurnEnded.");
        Use(g, "heal"); Play(g); Need(V(g, 0).Skills!.Any(s => s.ContentId == "ol:tianhou"), "Real recovery reacquires the conditional Tianhou source without injecting runtime state."); End(g); Reach(g, () => Stage(g, ZhouQunStage.ExchangeHand));
        Need(E<TianhouWeatherExpiredEvent>(g).Single().GrantId == grant.GrantId && V(g, 0).Skills!.Any(s => s.ContentId == "ol:lieshu"), "The next actual invocation removes only its original sourced grant and retains the independently granted permanent same-name skill."); g = Cold(g, registry); Branch(g, "hand-done"); Reach(g, () => Stage(g, ZhouQunStage.ChooseWeatherCard)); Branch(g, "weather-card", card: P(g)!.Choices.First().Cards.Single()); Branch(g, "weather-target", target: 2);
        Need(E<TianhouWeatherGrantedEvent>(g).Length == 2 && E<TianhouWeatherGrantedEvent>(g).Select(e => e.GrantId).Distinct().Count() == 2, "Zero exchange is allowed and a later invocation owns a different exact weather source."); _ = Cold(g, registry);
    }

    public static void WeatherExtremaFireAndThunderNativeChildrenCold()
    {
        var (g, registry) = Start("extrema"); Use(g, "hurt-owner"); Play(g); var hp = V(g, 1).Hp; End(g); Reach(g, () => E<ZhouQunStartedEvent>(g).Any(e => e.Op == SkillProgramEffectOp.ResolveYanshuangEndingLoss));
        Reach(g, () => E<ZhouQunHpPaidEvent>(g).Count(e => e.TargetSeat == 1) == 2);
        Need(V(g, 1).Hp == hp - 2 && V(g, 0).Hp == hp && E<ZhouQunStartedEvent>(g).Where(e => e.Source.OwnerSeat == 0).Select(e => e.Op).Contains(SkillProgramEffectOp.ResolveLieshuEndingLoss), "A tied maximum actual Ending actor first loses1, then the now-minimum same actor loses1; the holder is not the HP-loss target."); _ = Cold(g, registry);

        (g, registry) = Start("thunder-chain"); var own = V(g, 0).Hp; Use(g, "fire-incoming", targets: [2]); Play(g);
        Need(V(g, 0).Hp == own && E<ProgramDamagePreventedEvent>(g).Any(e => e.SkillId == "ol:zhouyu" && e.SourceSeat == 2 && e.TargetSeat == 0), "Zhouyu prevents real Fire caused by another role even when the weather holder is the damaged target.");
        var peer = V(g, 1).Hp; Use(g, "fire-own", targets: [1]); Play(g); Need(V(g, 1).Hp == peer - 1, "The same holder's own sourced Fire is not another role caused damage.");
        foreach (var seat in new[] { 0, 1, 3 }) { Use(g, "chain", targets: [seat]); Play(g); }
        Use(g, "thunder-incoming", targets: [2]); Reach(g, () => At(g, Watch) && Own(g)?.ZhouQun?.Stage == ZhouQunStage.HpChildren); var r = Own(g)!.ZhouQun!; Need(r.Targets.SequenceEqual([2, 0]) && r.Cursor == 1 && E<DamageAppliedEvent>(g).Count(e => e.Nature == DamageNature.Thunder) == 2, "The second actual chain recipient freezes its clockwise neighbors2 then0, and the issuer's real LoseHp pauses at the second neighboring child under that exact weather instruction. " + JsonSerializer.Serialize(new { r.Stage, r.Cursor, r.Targets, Parent = Own(g)!.WindowContext, Paid = E<ZhouQunHpPaidEvent>(g), Thunder = E<DamageAppliedEvent>(g).Where(e => e.Nature == DamageNature.Thunder), Hp = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp }), State = Diagnostic(g) })); Frozen(r.Targets); g = Cold(g, registry); Continue(g); Play(g);
        Need(E<DamageAppliedEvent>(g).Count(e => e.Nature == DamageNature.Thunder) == 3 && E<ZhouQunHpPaidEvent>(g).Length == 6 && E<ZhouQunStartedEvent>(g).Count(e => e.Op == SkillProgramEffectOp.ResolveZhouyuThunderNeighbors) == 3, "Every real chain recipient emits one distinct Thunder window; each schedules neighboring LoseHp, never extra Damage or duplicate chain propagation."); _ = Cold(g, registry);

        (g, registry) = Start("thunder-dying"); Use(g, "hurt-peer", targets: [1]); Play(g); Need(V(g, 1).Hp == 1, "The actual native setup leaves one HP on the intended neighbor."); Use(g, "thunder-incoming", targets: [2]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        Need(Own(g)?.ZhouQun is { Stage: ZhouQunStage.HpChildren, Cursor: 0 } && g.ResolutionStack.OfType<DyingFrame>().Last().VictimSeat == 1, "A real neighbor loss reaches native dying below the original weather instruction."); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass"); Play(g);
        Need(!V(g, 1).IsAlive && E<ZhouQunHpPaidEvent>(g).Count(e => e.TargetSeat is 1 or 3) == 2 && E<ZhouQunCompletedEvent>(g).Length == 1, "True death returns through the original typed parent and the remaining frozen neighbor still pays exactly once."); _ = Cold(g, registry);
    }

    public static void NingwuFinalActorNativeJudgmentAndWholeUseNullificationCold()
    {
        foreach (var scene in new[] { "ningwu-zero", "ningwu-physical", "ningwu-adjacent" })
        {
            var (g, registry) = Start(scene); var target = scene == "ningwu-adjacent" ? 2 : 1; var hp = V(g, target).Hp;
            if (scene == "ningwu-physical") Native(g, a => a.Kind == LegalActionKind.Slash && a.ConversionSource?.SkillId == Driver && a.TargetSeats.SequenceEqual([target])); else Use(g, "virtual-slash", targets: [target]);
            Reach(g, () => At(g, Replacer) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3]))); Answer(g, c => c.Targets.SequenceEqual([3]));
            if (scene != "ningwu-adjacent")
            {
                Reach(g, () => At(g, Watch)); var f = Own(g)!; var r = f.ZhouQun!; var judgment = g.ResolutionStack.OfType<JudgmentFrame>().Single(j => j.Id == r.JudgmentId);
                Need(r.Peer == 3 && r.Targets.SequenceEqual([target]) && judgment.TargetSeat == 3 && judgment.SourceSeat == 0 && r.SlashRank == (scene == "ningwu-zero" ? 0 : 7), "Ningwu judges the final actual different user and keeps the native numeric-zero or physical converted Slash's real frozen rank and sole target. " + JsonSerializer.Serialize(new { Scene, Receipt = r, Judgment = judgment, LiveAction = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == r.UseId).Action, Issued = E<NingwuJudgmentIssuedEvent>(g), Hand = V(g, 0).Hand.Select(c => new { c.Id, c.Rank }), State = Diagnostic(g) })); g = Cold(g, registry); Continue(g);
            }
            Play(g); var use = E<ProgramCardUseActorReplacedEvent>(g).Single().CardUseFrameId;
            if (scene == "ningwu-zero") Need(E<NingwuUseNullifiedEvent>(g).Single().UseId == use && E<NingwuJudgmentIssuedEvent>(g).Single().SlashRank == 0 && V(g, target).Hp == hp && E<DamageAppliedEvent>(g).Length == 0, "The actual judgment7 exceeds the native numeric-zero Slash rank and nullifies this entire exact Use, preserving target HP and producing no actual damage.");
            else Need(E<NingwuUseNullifiedEvent>(g).Length == 0 && V(g, target).Hp == hp - 1 && E<NingwuJudgmentIssuedEvent>(g).Length == (scene == "ningwu-adjacent" ? 0 : 1), "Equal real ranks keep a converted Slash effective, while actual neighboring seats do not issue a judgment at all.");
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use) == 1 && (scene != "ningwu-physical" || g.CardMovements.Count(m => m.Reason == CardMoveReasons.Use && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1), "The original accepted use finishes once and its true physical conversion cost is never paid again after cold judgment return."); _ = Cold(g, registry);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame? Own(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ZhouQun is not null);
    private static bool At(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill;
    private static bool Stage(GameEngine g, ZhouQunStage stage) => Own(g)?.ZhouQun?.Stage == stage && P(g)?.PlayerSeat == 0;
    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Zhou Qun command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "No actual human choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision)); }
    private static void Branch(GameEngine g, string branch, int? card = null, int? target = null) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == branch && (card is null || c.Cards.SequenceEqual([card.Value])) && (target is null || c.Targets.SequenceEqual([target.Value])));
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, Func<LegalAction, bool> predicate) { var a = g.GetHumanLegalActions().First(predicate); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 250; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, "Unexpected completion at " + line + ": " + Diagnostic(g)); var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (At(g, Watch)) Continue(g);
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (Stage(g, ZhouQunStage.ChooseHandOwner)) Branch(g, "hand-owner", target: 0);
            else if (Stage(g, ZhouQunStage.ChooseShownHand)) Branch(g, "show-hand", card: p.Choices[0].Cards.Single());
            else if (Stage(g, ZhouQunStage.ExchangeHand)) Branch(g, "hand-done");
            else if (Stage(g, ZhouQunStage.ChooseWeatherCard)) Branch(g, "weather-card", card: p.Choices[0].Cards.Single());
            else if (Stage(g, ZhouQunStage.ChooseWeatherRecipient)) Branch(g, "weather-target", target: 0);
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(g, c => c.Cards.Count == 0);
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else throw new InvalidOperationException("Unexpected native boundary at " + line + ": " + Diagnostic(g));
        }
        throw new InvalidOperationException("Bounded native boundary not reached at " + line + ": " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, g.State.Winner, Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(4).Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null, Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Kind = f.GetType().Name, Stage = (f as ProgramSkillFrame)?.ZhouQun?.Stage }), Facts = g.Events.TakeLast(6).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay retains private views, per-iteration shown cards, exact grants and once-only native children."); return copy; }
    private static void Frozen<T>(IReadOnlyList<T> values) { if (values.Count == 0) return; var refused = false; try { ((IList<T>)values)[0] = values[0]; } catch (NotSupportedException) { refused = true; } Need(refused, "Original weather collections must be frozen."); }
    private static (GameEngine, ContentRegistry) Start(string scene, bool play = true)
    {
        Scene = scene; var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); if (play) Play(g); return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhou-qun", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:zhou-qun"); var defs = new List<JsonNode>();
            void Skill(string id, string body) => defs.Add(JsonNode.Parse("{\"id\":\"" + id + "\",\"revision\":1," + body + "}")!);
            string A(string id, string effects, int targets = 0) => "{\"id\":\"" + id + "\",\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}";
            var acts = new[] { A("virtual-slash", "{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}", 1), A("hurt-owner", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("hurt-peer", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":11}", 1), A("heal", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"), A("permanent-weather", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"ol:lieshu\"]}"), A("chain", "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":true}", 1), A("fire-incoming", "{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":1,\"nature\":\"fire\"}", 1), A("fire-own", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1,\"nature\":\"fire\"}", 1), A("thunder-incoming", "{\"op\":\"damage\",\"target\":\"owner\",\"sourceRef\":{\"kind\":\"selectedTarget\"},\"amount\":1,\"nature\":\"thunder\"}", 1) };
            var rescueViewAs = scene == "thunder-dying" ? ",{\"id\":\"actual-dying-peach\",\"inputKinds\":[\"crossbow\"],\"inputSuits\":[],\"outputKind\":\"peach\",\"forPlay\":false,\"forResponse\":true}" : "";
            Skill(Driver, "\"activations\":[" + string.Join(',', acts) + "],\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":50,\"priority\":0},{\"id\":\"range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":10,\"priority\":0}],\"viewAs\":[{\"id\":\"actual-converted-slash\",\"inputKinds\":[\"crossbow\"],\"inputSuits\":[],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}" + rescueViewAs + "]");
            Skill(Quiet, "\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}],\"modifiers\":[{\"id\":\"range\",\"query\":\"attackRange\",\"operation\":\"add\",\"value\":10,\"priority\":0}]");
            Skill(Conditional, "\"hpSkillGrants\":[{\"id\":\"healthy-weather\",\"skillId\":\"ol:tianhou\",\"minimumHp\":13}]");
            Skill(Replacer, "\"triggers\":[{\"id\":\"final-actor\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"priority\":100,\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"}]}]");
            var window = scene.StartsWith("ningwu", StringComparison.Ordinal) ? "\"window\":\"judgmentFinalized\",\"subject\":\"any\",\"judgmentReasons\":[\"ningwu\"],\"minimumRank\":1,\"maximumRank\":13,\"suits\":[\"spade\",\"heart\",\"club\",\"diamond\"],\"excludedReasons\":[]," : scene == "thunder-chain" ? "\"window\":\"afterHealthChanged\",\"subject\":\"owner\",\"condition\":{\"kind\":\"healthChangeKindIs\",\"hpChangeKind\":\"loss\"}," : "\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + (scene == "tianhou-loss" ? "skill-program.zhou-qun.tianhou.exchange" : "skill-program.zhou-qun.chenshuo.gain") + "\"],";
            var watcherUsage = scene.StartsWith("ningwu", StringComparison.Ordinal) ? "" : "\"usageScope\":\"game\",\"usageLimit\":1,";
            Skill(Watch, "\"triggers\":[{\"id\":\"actual-native-child\"," + window + "\"optional\":false," + watcherUsage + "\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"pause\",\"options\":[{\"id\":\"continue\"}]}" + (scene == "tianhou-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "") + "]}]");
            var labels = defs.ToDictionary(n => n["id"]!.GetValue<string>(), n => new { name = n["id"]!.GetValue<string>(), description = "周群真实原生夹具", optionLabels = n["id"]!.GetValue<string>() == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : [] });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "周群原生边界") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver }; if (scene.StartsWith("reveal", StringComparison.Ordinal)) own.Add("ol:chenshuo"); if (scene == "tianhou-loss") own.Add(Conditional); if (scene == "extrema") own.AddRange(["ol:lieshu", "ol:yanshuang"]); if (scene.StartsWith("thunder", StringComparison.Ordinal)) own.Add("ol:zhouyu"); if (scene.StartsWith("ningwu", StringComparison.Ordinal)) own.AddRange(["ol:ningwu", Replacer]); if (scene != "thunder-dying" && scene != "extrema") own.Add(Watch);
            b.AddSkill(new("fixture:zq-owner-pick", "本人", "固定候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) }); b.AddGeneral(new(Owner, "周群本人", "ol-zhou-qun", "fixture:zq-owner-pick", "shu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:zq-peer-" + i).ToArray(); foreach (var (id, index) in peers.Select((id, index) => (id, index))) { var pick = id + "-pick"; b.AddSkill(new(pick, "其他角色", "固定候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (4 - index)) }); b.AddGeneral(new(id, "其他角色", "supporter", pick, "shu", 12, [Quiet])); }
            b.AddCard(new("fixture:zq-crossbow", "弩", "真实实体", "固定牌面", CardKind.Crossbow)); b.AddDeck(new("fixture:zq-deck", "周群固定原生牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("fixture:zq-crossbow", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "周群真实原生", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:zq-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
