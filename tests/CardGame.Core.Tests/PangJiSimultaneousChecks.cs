using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class PangJiChecks
{
    private const string PsOwner = "fixture:pg-simul-owner", PsMode = "identity:classic-pg-simul", PsDriver = "fixture:pg-simul-driver";
    private const string PsQuiet = "fixture:pg-simul-quiet", PsWatch = "fixture:pg-simul-watch", PsConditional = "fixture:pg-simul-conditional";
    private const string PsClaim = "fixture:pg-simul-claim", PsInsert = "fixture:pg-simul-insert";
    private const string PsGushe = "ol:gushe", PsJici = "ol:jici", PsDouyu = "ol:douyu", PsZenrun = "ol:zenrun";
    private const string PsDraw = "skill-program.pang-ji.douyu.draw", PsObtain = "skill-program.sun-hong.zenrun.obtain", PsCompensation = "skill-program.sun-hong.zenrun.compensation";

    public static void PangJiSimultaneousPindianPaidChildrenAndSourceRightsCold()
    {
        foreach (var scene in new[] { "win", "swap-loss", "paid-death", "replacement" })
        {
            var s = PsScenario.Start(scene); s.Stock();
            var one = s.GiveRank(13, 1); var two = s.GiveRank(7, 2); var three = s.GiveRank(7, 3);
            int[] replacements = [];
            if (scene == "replacement") replacements = [s.GiveRank(1, 3), s.GiveRank(1, 3)];
            var source = s.Card(1);
            s.Group(source, [1, 2, 3]); s.Reach(() => s.Optional(PsDouyu)); var root = s.GroupRoot(); var id = root.Id;
            var original = root.DouyuContest!;
            s.Need(original is { Simultaneous: true, Ready: false, Paid: false } && original.SourceSeat == 0 &&
                original.OriginalCards.Select(c => (c.Seat, c.CardId, c.PaymentFrom)).SequenceEqual([(0, source, CardLocation.Hand(0)), (1, one, CardLocation.Hand(1)), (2, two, CardLocation.Hand(2)), (3, three, CardLocation.Hand(3))]) &&
                s.E<PindianCardsRevealedEvent>().All(e => e.ParentFrameId != id) && s.E<DouyuSwapCommittedEvent>().All(e => e.ContestId != id),
                "The native common group freezes four genuine original owners and entities before any reveal, exchange or physical payment.");
            var nativePrompt = s.P!; s.Private(0); s.Cold(); s.Activate();
            s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "douyu") == true);
            var swapFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Douyu is not null);
            var swapSource = swapFrame.Douyu!.Source;
            s.Need(s.P!.ValidCardIds.Count == 0 && s.P.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 1) &&
                s.P.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([1, 2, 3]) &&
                Enumerable.Range(0, 4).All(v => !s.G.CreateSnapshot(v).PublicRevealedCards.Any(c => original.OriginalCards.Any(o => o.CardId == c.Id))) &&
                original.OriginalCards.All(c => s.Zone(c.CardId) == c.PaymentFrom),
                "A common source receives one blind opportunity with all actual opponents; no viewer sees committed faces or fictitious moved materials.");
            s.Private(0); s.Reject(); s.Frozen(original.OriginalCards); s.Frozen(original.Assignments); s.Frozen(original.Swaps); s.Cold();
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "swap" && c.Targets.SequenceEqual([1])); s.RejectOld(nativePrompt);
            if (scene == "swap-loss")
            {
                s.Reach(() => s.Hold("reveal"));
                s.Need(s.GroupRoot().SimultaneousPindian is { Revealed: true, Stage: SimultaneousPindianStage.RevealChildren } &&
                    s.E<PindianResultDeterminedEvent>().All(e => e.FrameId != id) && s.E<DouyuSwapCommittedEvent>().Single(e => e.ContestId == id).Source == swapSource,
                    "The source is lost in a genuine common-payment movement child after the blind exchange and before the first settled comparison.");
                s.Cold(); s.Continue();
            }
            if (scene == "replacement") PsReplacement(s, id, replacements);
            else
            {
                s.Reach(() => s.Hold("draw"));
                var draw = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Douyu?.DrawStarted == true);
                s.Need(draw.Douyu is { Stage: DouyuStage.DrawChildren, ActualDrawIds.Count: 2, ReplacementGainedIds.Count: 0 } receipt &&
                    receipt.ContestId == id && receipt.ComparisonIndex == 0 && receipt.SwapFrameId == swapFrame.Id && receipt.IssuedSource == swapSource &&
                    s.E<DouyuWinDrawReturnedEvent>().Single(e => e.FrameId == draw.Id) is { RequestedCount: 2, ActualCount: 2, ReplacementCount: 0 } &&
                    !s.E<DouyuRewardClosedEvent>().Any(e => e.SwapFrameId == swapFrame.Id) &&
                    !s.E<SimultaneousPindianClaimsIssuedEvent>().Any(e => e.ProgramFrameId == id),
                    "Only the actual selected winning pair issues one native Draw2; its gain child and original source remain pending before any result claim.");
                if (scene == "swap-loss") s.Need(!s.V().Skills!.Any(k => k.ContentId == PsDouyu) && s.V().IsAlive,
                    "A real lost conditional leaf does not cancel the already-committed exchange's eventual winning benefit.");
                s.Private(0); s.Cold(); s.Continue();
                if (scene == "win")
                {
                    s.Reach(() => s.Hold("recovered"));
                    s.Need(s.G.ResolutionStack.Any(f => f.Id == draw.Id) && s.V().Hp == s.V().MaxHp &&
                        s.E<DouyuRewardClosedEvent>().All(e => e.SwapFrameId != swapFrame.Id),
                        "A real HP loss and recovery child complete before the native draw benefit returns to the common comparison.");
                    s.Private(0); s.Cold(); s.Continue();
                }
            }
            s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == id));
            var reveal = s.E<PindianCardsRevealedEvent>().Single(e => e.ParentFrameId == id && e.Simultaneous);
            var swap = s.E<DouyuSwapCommittedEvent>().Single(e => e.ContestId == id);
            var comparisons = s.E<PindianResultDeterminedEvent>().Where(e => e.FrameId == id).ToArray();
            s.Need(reveal.Cards.Select(c => (c.Seat, c.CardId, c.OriginalRank)).SequenceEqual([(0, one, 13), (1, source, 1), (2, two, 7), (3, three, 7)]) &&
                swap.Source == swapSource && swap.OwnerCardBefore == source && swap.OpponentCardBefore == one && swap.OwnerCardAfter == one && swap.OpponentCardAfter == source &&
                comparisons.Select(e => e.Result.OpponentSeat).SequenceEqual([1, 2, 3]) && comparisons.All(e => e.Result.SourceCardId == one && e.Result.SourceRank == 13 && e.Result.SourceWon) &&
                s.E<DouyuCandidateCommittedEvent>().Count(e => e.ContestId == id && e.Source.OwnerSeat == 0) == 1 &&
                s.E<DouyuWinDrawStartedEvent>().Count(e => e.ContestId == id && e.SwapFrameId == swapFrame.Id && e.ComparisonIndex == 0) == 1 &&
                s.E<DouyuRewardClosedEvent>().Count(e => e.ContestId == id && e.SwapFrameId == swapFrame.Id && e.DrawStarted) == 1 &&
                s.E<SimultaneousPindianCompletedEvent>().Count(e => e.ProgramFrameId == id && e.CompletedComparisons == 3) == 1,
                "One actual common reveal and one source opportunity produce three final logical wins, but only the chosen swapped pair yields its single Draw2 and once-only completion.");
            foreach (var c in original.OriginalCards)
                s.Need(s.G.CardMovements.Count(m => m.CardId == c.CardId && m.From == c.PaymentFrom && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1 &&
                    s.E<DouyuNativePaymentEvent>().Count(e => e.ContestId == id && e.OriginalSeat == c.Seat && e.CardId == c.CardId && e.From == c.PaymentFrom) == 1,
                    "Each original entity pays from its true original owner once, even though final logical Pindian ownership is exchanged.");
            var claims = s.E<SimultaneousPindianClaimPaidEvent>().Where(e => e.ProgramFrameId == id && e.Taken).ToArray();
            if (scene == "paid-death") s.Need(!s.V().IsAlive && s.E<DouyuWinDrawReturnedEvent>().Count(e => e.ContestId == id && e.ActualCount == 2) == 1,
                "Actual issuer death inside the already-returned draw's gain child drains that paid benefit and preserves the native whole-group return.");
            else s.Need(claims.SelectMany(e => e.CardIds).Distinct().Count() == 4 && claims.Sum(e => e.CardIds.Count) == 4 &&
                s.G.CardMovements.Count(m => m.CardId == one && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == "program.pindian.claim") == 1 &&
                reveal.Cards.All(c => !s.G.CardMovements.Any(m => m.CardId == c.CardId && m.Reason == CardMoveReasons.PindianFinish)),
                "Real per-comparison claims obtain the common source once and the three other entities once; claimed cards are never discarded again during cleanup.");
            s.Cold();
        }
        PsJiciUnselectedWins();
    }

    private static void PsReplacement(PsScenario s, long contest, int[] originals)
    {
        s.Reach(() => s.Optional(PsZenrun)); var draw = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Douyu?.DrawStarted == true);
        var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == draw.Id);
        s.Need(attempt.Context is { RecipientSeat: 0, RequestedCount: 2 } && attempt.Context.Reason.Value == PsDraw &&
            s.E<DouyuWinDrawReturnedEvent>().All(e => e.FrameId != draw.Id), "The real swapped-pair Draw2 is replaceable before its original materials or outcome exist.");
        s.Private(0); s.Cold(); s.Activate(); s.Reach(() => s.Zen("target"));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([3])); s.Reach(() => s.Zen("pick"));
        s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "pick").All(c => c.Cards.Count == 0) && s.G.CreateSnapshot(0).Players[3].Hand.Count == 0,
            "The replacement requester sees foreign Hand slots, without the two committed replacement faces.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick"); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick");
        var replacement = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zenrun is not null);
        s.Need(replacement.Zenrun!.Selected.Select(c => c.CardId).Order().SequenceEqual(originals.Order()), "The real private slot selections commit precisely the two previously gifted entities.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
        s.Need(s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id).Replacement!.GainedIds.Order().SequenceEqual(originals.Order()) &&
            s.E<DouyuWinDrawReturnedEvent>().All(e => e.FrameId != draw.Id), "Two real obtained entities and their gain child remain inside the unfinished native draw attempt.");
        s.Private(0); s.Cold(); s.Continue();
        s.Reach(() => s.P is { PlayerSeat: 3 } p && p.SkillPrompt?.SkillId == PsZenrun && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw"));
        s.Private(3); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Reach(() => s.Hold("compensated"));
        s.Need(s.P!.PlayerSeat == 3 && s.E<ZenrunCompensationReturnedEvent>().Single(e => e.FrameId == replacement.Id) is { ActualDrawCount: 2, ReplacementGainedCount: 0 } &&
            s.E<DouyuWinDrawReturnedEvent>().All(e => e.FrameId != draw.Id), "The victim's actual compensation Draw2 and all its children finish before Douyu receives a replacement outcome.");
        s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.E<DouyuWinDrawReturnedEvent>().Any(e => e.FrameId == draw.Id));
        var returned = s.E<DouyuWinDrawReturnedEvent>().Single(e => e.FrameId == draw.Id);
        s.Need(returned is { ActualCount: 0, ReplacementCount: 2, RequestedCount: 2 } && returned.AttemptFrameId == attempt.Id &&
            s.E<DouyuWinDrawEntityEvent>().Where(e => e.FrameId == draw.Id && e.Replacement).Select(e => e.CardId).Order().SequenceEqual(originals.Order()) &&
            !s.G.CardMovements.Any(m => m.Reason.Value == PsDraw) && originals.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(3) && m.To == CardLocation.Hand(0) && m.Reason.Value == PsObtain) == 1) &&
            s.E<DouyuWinDrawReturnedEvent>().Count(e => e.ContestId == contest) == 1,
            "A genuine replacement preserves Actual0 and two separate replacement identities without forging original DrawPile materials or repeating the benefit."); s.Cold();
    }

    private static void PsJiciUnselectedWins()
    {
        var s = PsScenario.Start("jici"); s.Stock(); var warmSource = s.Card(1); s.GiveRank(13, 1); s.Group(warmSource, [1]); s.Play();
        s.Need(s.V().Markers!.Single(m => m.Kind == PlayerMarkerKind.RaoShe).Count == 1 && s.E<DouyuSwapCommittedEvent>().Length == 0,
            "A real declined-exchange losing contest creates exactly one Raoshe marker without inventing a Jici state.");
        s.End(); s.Play(); var source = s.Card(13); var one = s.GiveRank(1, 1); var two = s.GiveRank(1, 2); var three = s.GiveRank(1, 3);
        s.Group(source, [1, 2, 3]); s.Reach(() => s.Optional(PsDouyu)); var id = s.GroupRoot().Id;
        s.Cold(); s.Activate(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "douyu") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "swap" && c.Targets.SequenceEqual([1])); s.Reach(() => s.Optional(PsJici));
        var reveal = s.G.ResolutionStack.OfType<PindianRevealWindowFrame>().Single();
        s.Need(reveal.Simultaneous && reveal.ParentFrameId == id && reveal.Cards.Single(c => c.Seat == 0) is { CardId: var card, Rank: 1 } && card == one,
            "The real post-exchange common reveal presents the newly assigned low card to ordinary Jici, rather than reusing its original owner's rank.");
        s.Private(0); s.Cold(); s.Activate(); s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == id));
        var results = s.E<PindianResultDeterminedEvent>().Where(e => e.FrameId == id).Select(e => e.Result).ToArray();
        s.Need(results.Length == 3 && results[0] is { SourceRank: 2, OpponentRank: 13, SourceWon: false } && results[0].SourceCardId == one && results[0].OpponentCardId == source &&
            results.Skip(1).All(r => r.SourceRank == 2 && r.OpponentRank == 1 && r.SourceWon) &&
            s.E<PindianRevealRankAdjustedEvent>().Count(e => e.WindowFrameId == reveal.Id && e.CardId == one && e.Before == 1 && e.Bonus == 1 && e.After == 2) == 1 &&
            !s.E<DouyuWinDrawStartedEvent>().Any(e => e.ContestId == id) && s.E<DouyuRewardClosedEvent>().Single(e => e.ContestId == id).DrawStarted == false &&
            s.E<SimultaneousPindianClaimPaidEvent>().Where(e => e.ProgramFrameId == id && e.Taken).SelectMany(e => e.CardIds).Order().SequenceEqual(new[] { one, two, three }.Order()),
            "Final ordinary Jici ranks drive all comparisons and real claims; wins against unselected opponents never satisfy the losing exchanged pair's Draw2 condition."); s.Cold();
    }

    private sealed class PsScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int card) => G.CreateCardZoneDiagnostics().Single(d => d.CardId == card).Location;
        public ProgramSkillFrame GroupRoot() => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SimultaneousPindian is not null);
        public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Hold(string option) => P?.SkillPrompt?.SkillId == PsWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public bool Zen(string branch) => P?.SkillPrompt?.SkillId == PsZenrun && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public int Card(int rank) => V().Hand.First(c => c.Rank == rank).Id;
        public void Stock() { Use("stock"); Play(); Need(V().Hand.Count(c => c.Rank == 1) >= 4 && V().Hand.Count(c => c.Rank == 7) >= 4 && V().Hand.Count(c => c.Rank == 13) >= 4, "The fixed small draw has four real entities of each required printed rank without searching seeds."); }
        public int GiveRank(int rank, int seat) { var id = Card(rank); Use("give", [id], [seat]); Play(); return id; }
        public void Use(string activation, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, PsDriver, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Group(int card, int[] targets) => Accept(new UseProgramSkillCommand(0, PsGushe, "simultaneous-pindian", [card], targets, G.Revision, P!.PromptId));
        public void Private(int seat) => Need(P is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the native chooser sees the actual private prompt.");
        public void Frozen<T>(IReadOnlyList<T> list) { var rejected = false; try { ((IList)list).Add(default(T)); } catch (NotSupportedException) { rejected = true; } Need(rejected, "The common original cards, assignments and swaps are detached immutable lists."); }
        public void Reject()
        { var p = P!; var state = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && state == State() && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("unpublished-douyu-common"), G.Revision)).Accepted && state == State(), "Foreign or unpublished exchange input cannot mutate hidden assignment, native cost or benefit."); }
        public void RejectOld(PendingDecision old) { var state = State(); Need(!G.Submit(new AnswerPromptCommand(old.PlayerSeat, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && state == State(), "The original optional token cannot answer a later native child."); }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P; var c = p?.Choices.FirstOrDefault(match); Need(p is { PlayerSeat: 0 } && c is not null, "The human must answer one genuinely published canonical choice."); Accept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The native simultaneous fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == PsWatch) Continue();
                else if (p.SkillPrompt?.SkillId == PsInsert && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0]));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "simultaneous-pindian" && c.Parameters.GetValueOrDefault("branch") == "claim")) Answer(c => c.Parameters.GetValueOrDefault("branch") == "claim");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "simultaneous-pindian" && c.Parameters.GetValueOrDefault("branch") == "draw")) Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected common Pindian boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded common Pindian boundary " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command)
        { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves private common assignments, actual original payments, final ranks, source rights and every native child cursor."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Results = E<PindianResultDeterminedEvent>(), Draws = E<DouyuWinDrawReturnedEvent>(), Moves = G.CardMovements.TakeLast(8) });
        public static PsScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new PsFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, ModeId = PsMode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new PsScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, PsOwner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class PsFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pang-ji-simultaneous", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            PsProduction(b, "ol-pang-ji", PsDouyu); PsProduction(b, "ol-wang-lang", PsGushe, PsJici);
            if (scene == "replacement") PsProduction(b, "ol-sun-hong", PsZenrun);
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards > 0 ? "\"sourceZones\":[\"hand\"]," : "") + "\"effects\":[" + effects + "]}");
            static string Choice(string option) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"" + option + "\"}]}";
            var definitions = new JsonArray(new JsonObject { ["id"] = PsDriver, ["revision"] = 1, ["activations"] = new JsonArray(A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":16}"), A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1)), ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]") },
                N($$"""{"id":"{{PsQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""),
                N($$"""{"id":"{{PsClaim}}","revision":1,"cardPolicies":[{"id":"native-group-claim","kind":"pindianClaimAllWhenSourceWins"}]}"""));
            if (scene == "swap-loss") definitions.Add(N($$"""{"id":"{{PsConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{PsDouyu}}","minimumHp":13}]}"""));
            if (scene == "jici") definitions.Add(N($$"""{"id":"{{PsInsert}}","revision":1,"triggers":[{"id":"new-real-play","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}"""));
            var watch = new JsonArray();
            void Gain(string option, string reason, string after = "") => watch.Add(N("{\"id\":\"actual-" + option + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + reason + "\"],\"optional\":false,\"effects\":[" + Choice(option) + (after.Length == 0 ? "" : "," + after) + "]}"));
            if (scene == "swap-loss") watch.Add(N("{\"id\":\"real-common-payment\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill.pindian.reveal\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Choice("reveal") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            if (scene == "win")
            {
                Gain("draw", PsDraw, "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}");
                watch.Add(N("{\"id\":\"real-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("recovered") + "]}"));
            }
            else if (scene == "paid-death") Gain("draw", PsDraw, "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}");
            else if (scene == "swap-loss") Gain("draw", PsDraw);
            if (scene == "replacement") { Gain("obtained", PsObtain); Gain("compensated", PsCompensation); }
            if (watch.Count > 0) definitions.Add(new JsonObject { ["id"] = PsWatch, ["revision"] = 1, ["triggers"] = watch });
            var labels = definitions.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实多人拼点与斗誉原生子流程",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实多人拼点夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:pg-simul-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var own = new List<string> { PsDriver, PsGushe, PsJici, PsClaim, scene == "swap-loss" ? PsConditional : PsDouyu };
            if (watch.Count > 0) own.Add(PsWatch);
            if (scene == "replacement") own.Add(PsZenrun); if (scene == "jici") own.Add(PsInsert);
            b.AddGeneral(new(PsOwner, "真实多人斗誉者", "supporter", pick, "qun", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:pg-simul-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var peerPick = "fixture:pg-simul-peer-pick-" + i; b.AddSkill(new(peerPick, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "真实多人拼点参与者", "supporter", peerPick, "shu", 12, scene == "replacement" ? [PsQuiet, PsWatch] : [PsQuiet])); }
            b.AddDeck(new("fixture:pg-simul-deck", "十八张真实三点数杀", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 18).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i < 6 ? 1 : i < 12 ? 7 : 13)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(PsMode, "真实多人斗誉边界", 4, 4, roles, "fixture:pg-simul-deck", GeneralCandidateCount: 4, GeneralPoolIds: [PsOwner, .. peers]));
        }
        private static void PsProduction(IContentRegistryBuilder b, string bundle, params string[] ids)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing production resource " + bundle + "." + suffix); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
