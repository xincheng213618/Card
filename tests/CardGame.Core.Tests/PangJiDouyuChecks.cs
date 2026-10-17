using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class PangJiChecks
{
    private const string DySkill = "ol:douyu", DyDriver = "fixture:dy-driver", DyWatch = "fixture:dy-watch";
    private const string DyOwner = "fixture:dy-owner", DyMode = "identity:classic-douyu", DyDraw = "skill-program.pang-ji.douyu.draw";

    public static void DouyuBlindOriginalEntitySwapRankClaimsAndWinDrawCold()
    {
        var rules = JsonNode.Parse(DyRead("ol-pang-ji", "rules"))!;
        rules["skills"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == DySkill)!["triggers"]![0]!["allowNoEventTarget"] = true;
        var refused = false;
        try { _ = SkillProgramCatalog.Load(rules.ToJsonString(), DyRead("ol-pang-ji", "presentation")); }
        catch (InvalidOperationException) { refused = true; }
        DyRequire(refused, "An unsupported policy cannot silently acquire Douyu's custom private collector authority.");

        foreach (var scene in new[] { "win", "loss", "tie", "skip", "both", "alcohol", "jici", "claim", "paid-loss", "paid-death", "top", "pile" })
        {
            var s = DyScenario.DyStart(scene);
            if (scene == "pile")
            {
                var output = s.DyG.GetHumanLegalActions().First(a => a.ProgramSkillId == "ol:xiewei");
                s.DyAccept(new UseProgramSkillCommand(0, "ol:xiewei", output.ProgramActivationId!, output.SelectableCardIds, [1], s.DyG.Revision, s.DyP!.PromptId));
                s.DyPlay();
                s.DyAccept(new UseProgramSkillCommand(0, "ol:youque", "minimum-bait-pindian", [], [1], s.DyG.Revision, s.DyP!.PromptId));
                s.DyReach(() => s.DyP?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "bait") == true);
                s.DyAnswer(c => c.Parameters.GetValueOrDefault("branch") == "bait");
            }
            else if (scene == "top")
            {
                s.DyUse("top");
                s.DyReach(() => s.DyP?.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-top") == true);
                s.DyCold(); s.DyAnswer(c => c.Parameters.GetValueOrDefault("action") == "pindian-top");
            }
            else s.DyUse("pair", [s.DyV().Hand.First().Id]);
            s.DyReach(() => s.DyOptional(DySkill));
            var pair = s.DyG.ResolutionStack.OfType<PindianFrame>().Single(); var contestId = pair.Id;
            var frozen = pair.DouyuContest!; var original = frozen.OriginalCards.ToArray();
            s.DyNeed(original.Length == 2 && !frozen.Paid && !frozen.Ready && frozen.Swaps.Count == 0 &&
                original.Select(c => c.CardId).Distinct().Count() == 2 && original.All(c => s.DyZone(c.CardId) == c.PaymentFrom) &&
                !s.DyE<PindianResultDeterminedEvent>().Any(e => e.FrameId == contestId),
                "The native pair freezes two genuine unrevealed entities in their original payment zones before either skill or reveal.");
            s.DyNeed(Enumerable.Range(0, 4).All(v => !s.DyG.CreateSnapshot(v).PublicRevealedCards.Any(c => original.Any(o => o.CardId == c.Id))) &&
                Enumerable.Range(1, 3).All(v => s.DyG.CreateSnapshot(v).PendingDecision is null),
                "Unrevealed original cards are absent from public views and only the real optional owner receives a private menu.");
            if (scene == "top") s.DyNeed(original[0].PaymentFrom == CardLocation.Processing && original[0].ReservationSequence is { } reserved &&
                original[1].PaymentFrom == CardLocation.DrawPile && s.DyG.CardMovements.Count(m => m.Sequence == reserved && m.CardId == original[0].CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == "pindian.reserve-top") == 1,
                "Source top is genuinely reserved once; the other participant's still-unrevealed top is not moved into Hand.");
            if (scene == "pile") s.DyNeed(pair.OwnedPileSource is { } owned && original[0].PaymentFrom == owned.Pile.Location &&
                owned.CardId == original[0].CardId && !s.DyG.CardMovements.Any(m => m.CardId == owned.CardId && m.To == CardLocation.Hand(0) &&
                    m.Sequence > s.DyG.CardMovements.Last(put => put.CardId == owned.CardId && put.To == owned.Pile.Location).Sequence),
                "The genuine minimum bait remains an original public-pile payment, without a manufactured Hand transfer.");
            s.DyReject(); s.DyCold();
            if (scene == "skip") s.DyAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else
            {
                s.DyAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                s.DyNeed(s.DyP is { PlayerSeat: 0, IsPrivate: true } p && p.ValidCardIds.Count == 0 &&
                    p.Choices.All(c => c.Cards.Count == 0 && c.Targets.SequenceEqual([1])) &&
                    s.DyG.ResolutionStack.OfType<ProgramSkillFrame>().Last().Douyu is { Stage: DouyuStage.OpponentChoice },
                    "The blind swap exposes only its actual opponent, never the opponent's committed identity, rank or a result-derived choice.");
                var old = s.DyP!; s.DyReject(); s.DyCold();
                s.DyAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "douyu" && c.Parameters.GetValueOrDefault("branch") == "swap");
                s.DyRejectOld(old);
            }
            if (scene == "jici")
            {
                s.DyReach(() => s.DyOptional("ol:jici"));
                var shown = s.DyE<PindianCardsRevealedEvent>().Single(e => e.ParentFrameId == contestId);
                s.DyNeed(shown.Cards.Single(c => c.Seat == 0).CardId == original[1].CardId && shown.Cards.Single(c => c.Seat == 0).OriginalRank == 7 &&
                    !s.DyE<PindianResultDeterminedEvent>().Any(e => e.FrameId == contestId),
                    "The exchanged real identity becomes public before JiCi changes that participant's actual revealed rank.");
                s.DyCold(); s.DyAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            }
            var wins = scene is not ("loss" or "tie" or "skip");
            if (wins)
            {
                s.DyReach(() => s.DyHold());
                var draw = s.DyE<DouyuWinDrawReturnedEvent>().Single(e => e.ContestId == contestId);
                var owner = s.DyG.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == draw.FrameId);
                s.DyNeed(draw.RequestedCount == 2 && draw.ActualCount == 2 && draw.ReplacementCount == 0 &&
                    owner.Douyu is { DrawStarted: true, Stage: DouyuStage.DrawChildren } &&
                    s.DyE<DouyuRewardClosedEvent>().All(e => e.SwapFrameId != owner.Douyu.SwapFrameId) &&
                    s.DyG.ResolutionStack.OfType<PindianFrame>().Single().PindianStep != PindianStep.ClaimResult,
                    "A real two-card native Draw returns to its own paid receipt and pauses in its genuine gain child before claims or completion.");
                s.DyNeed(s.DyE<DouyuWinDrawEntityEvent>().Count(e => e.FrameId == owner.Id && !e.Replacement) == 2 &&
                    s.DyE<DouyuWinDrawEntityEvent>().Where(e => e.FrameId == owner.Id && !e.Replacement).All(e =>
                    s.DyG.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence > draw.Before && m.Sequence <= draw.After &&
                        m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == DyDraw) == 1),
                    "Every actual reward card has one original DrawPile-to-winner payment, separate from either Pindian cost.");
                s.DyCold(); s.DyAnswer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            if (scene == "claim")
            {
                s.DyReach(() => s.DyP?.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-claim") == true);
                s.DyNeed(s.DyP!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true").Cards.Order().SequenceEqual(original.Select(c => c.CardId).Order()) &&
                    s.DyE<DouyuRewardClosedEvent>().Single(e => e.ContestId == contestId).DrawStarted,
                    "The original policy can claim both final paid identities only after the actual swapped winner's Draw children finish.");
                s.DyCold(); s.DyAnswer(c => c.Parameters.GetValueOrDefault("take") == "true");
            }
            if (scene == "paid-death")
            {
                s.DyReach(() => s.DyG.ResolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == 0));
                s.DyNeed(s.DyV().Hp <= 0 && s.DyV().IsAlive && s.DyE<DouyuWinDrawReturnedEvent>().Count(e => e.ContestId == contestId) == 1 &&
                    original.All(c => s.DyZone(c.CardId) == CardLocation.Processing),
                    "The genuine Draw gain child enters native Dying while both original paid contest cards remain Processing.");
                s.DyCold();
                s.DyReach(() => s.DyP is { PlayerSeat: 1 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-claim"));
                var claim = s.DyP!.Choices.Single(c => c.Parameters.GetValueOrDefault("take") == "true");
                s.DyNeed(!s.DyV().IsAlive && s.DyV(1).IsAlive && s.DyE<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 &&
                    s.DyG.State.Winner == Winner.None && claim.Cards.SequenceEqual([original[0].CardId]) &&
                    s.DyG.ResolutionStack.OfType<PindianFrame>().Single(f => f.Id == contestId).Result is { SourceWon: true } &&
                    s.DyE<DouyuRewardClosedEvent>().Count(e => e.ContestId == contestId && e.DrawStarted) == 1,
                    "The paid winner's real death does not erase the living opponent's native policy claim or repeat the completed Draw right.");
                s.DyCold(); s.DyStep();
                s.DyReach(() => !s.DyG.ResolutionStack.Any(f => f.Id == contestId));
                s.DyNeed(s.DyV(1).Hand.Any(c => c.Id == original[0].CardId) &&
                    s.DyE<DouyuWinDrawReturnedEvent>().Count(e => e.ContestId == contestId) == 1 &&
                    s.DyE<DouyuRewardClosedEvent>().Count(e => e.ContestId == contestId) == 1,
                    "The actual AI policy owner takes its original final assignment once and the dead winner's reward remains settled once.");
            }
            else s.DyPlay();
            var result = s.DyE<PindianResultDeterminedEvent>().Single(e => e.FrameId == contestId).Result;
            var swaps = s.DyE<DouyuSwapCommittedEvent>().Where(e => e.ContestId == contestId).OrderBy(e => e.Ordinal).ToArray();
            s.DyNeed(swaps.Length == (scene == "skip" ? 0 : scene == "both" ? 2 : 1) &&
                result.SourceCardId == original[scene is "skip" or "both" ? 0 : 1].CardId &&
                result.OpponentCardId == original[scene is "skip" or "both" ? 1 : 0].CardId &&
                s.DyE<DouyuWinDrawReturnedEvent>().Count(e => e.ContestId == contestId) == (wins ? 1 : 0),
                "Repeated legal participant swaps permute the same two original entities, and only an activated native winner receives exactly one Draw2.");
            if (scene == "both") s.DyNeed(swaps.Select(e => e.Source.OwnerSeat).SequenceEqual([0, 1]) && swaps[1].OwnerCardBefore == swaps[0].OpponentCardAfter &&
                swaps[1].OpponentCardBefore == swaps[0].OwnerCardAfter, "The blind AI uses the current unique assignment after the first real swap rather than a copied or oracle-selected pair.");
            if (scene == "alcohol") s.DyNeed(original.All(c => c.PrintedKind == CardKind.Alcohol) && result.SourceRank == 13 && result.OpponentRank == 7,
                "Alcohol's native king identity applies to the final assigned participant while the two printed original Alcohol payments remain unchanged.");
            if (scene == "jici") s.DyNeed(result.SourceRank == 13 && result.OpponentRank == 7 && s.DyE<PindianRevealRankAdjustedEvent>().Any(e => e.CardId == original[1].CardId && e.Before == 7 && e.After == 13),
                "JiCi modifies the actually received and revealed entity before the final swapped-pair win qualification.");
            if (scene == "paid-loss") s.DyNeed(s.DyV().Skills?.All(skill => skill.ContentId != DySkill) == true &&
                s.DyE<DouyuRewardClosedEvent>().Single(e => e.ContestId == contestId && e.DrawStarted).SwapFrameId == swaps[0].FrameId,
                "A genuine reward-gain child can remove the source without cancelling, repeating or reassigning the already paid Draw right.");
            foreach (var card in original)
            {
                var payment = s.DyE<DouyuNativePaymentEvent>().Single(e => e.ContestId == contestId && e.OriginalSeat == card.Seat);
                s.DyNeed(payment.CardId == card.CardId && payment.From == card.PaymentFrom &&
                    s.DyG.CardMovements.Count(m => m.Sequence == payment.MovementSequence && m.CardId == card.CardId && m.To == CardLocation.Processing &&
                        m.From == (payment.AlreadyReserved ? CardLocation.DrawPile : card.PaymentFrom)) == 1 &&
                    (scene == "claim" ? s.DyZone(card.CardId) == CardLocation.Hand(0) && !s.DyG.CardMovements.Any(m => m.CardId == card.CardId && m.Reason == CardMoveReasons.PindianFinish) :
                     scene == "paid-death" && card.CardId == original[0].CardId ? s.DyZone(card.CardId) == CardLocation.Hand(1) &&
                        s.DyG.CardMovements.Count(m => m.CardId == card.CardId && m.From == CardLocation.Processing && m.To == CardLocation.Hand(1) && m.Reason.Value == "program.pindian.claim") == 1 &&
                        !s.DyG.CardMovements.Any(m => m.CardId == card.CardId && m.Reason == CardMoveReasons.PindianFinish) :
                        s.DyG.CardMovements.Count(m => m.CardId == card.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.PindianFinish) == 1),
                    "Each original card pays from its original native zone exactly once; its final real claim or cleanup never creates an extra Hand payment.");
            }
            s.DyNeed(!s.DyG.ResolutionStack.Any(f => f.Id == contestId), "The original native pair and all paid reward/claim children complete exactly once."); s.DyCold();
        }
    }

    private static void DyRequire(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static string DyRead(string bundle, string part)
    { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + part + ".json") ?? throw new InvalidOperationException("Missing embedded " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private sealed class DyScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine DyG = game;
        public PendingDecision? DyP => Enumerable.Range(0, 4).Select(s => DyG.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot DyV(int seat = 0) => DyG.CreateSnapshot(seat).Players[seat];
        public T[] DyE<T>() where T : IGameEvent => DyG.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation DyZone(int id) => DyG.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public bool DyOptional(string skill) => DyP is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool DyHold() => DyP is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == DyWatch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void DyUse(string binding, IReadOnlyList<int>? cards = null) => DyAccept(new UseProgramSkillCommand(0, DyDriver, binding, cards ?? [], [1], DyG.Revision, DyP!.PromptId));
        public void DyStep() => DyAccept(new AdvanceOneStepCommand(DyG.Revision));
        public void DyAnswer(Func<PromptChoice, bool> pick)
        { var p = DyP; var c = p?.Choices.FirstOrDefault(pick); DyNeed(p is { PlayerSeat: 0 } && c is not null, "Only the real human chooses an actually published input."); DyAccept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, DyG.Revision)); }
        public void DyPlay() => DyReach(() => DyP is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void DyNeed(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + " " + DyDiagnostic()); }
        public void DyAccept(GameCommand command)
        { try { var result = DyG.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); DyNeed(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); } catch (Exception e) { throw new InvalidOperationException("Douyu " + scene + " " + command.GetType().Name + " cause=" + e.Message + " inner=" + e + " " + DyDiagnostic(), e); } }
        public void DyReject()
        { var p = DyP!; var before = DyState(); DyNeed(!DyG.Submit(new AnswerPromptCommand(2, p.PromptId, p.Choices[0].Id, DyG.Revision)).Accepted && before == DyState() && !DyG.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("unpublished-douyu-target"), DyG.Revision)).Accepted && before == DyState(), "Foreign or forged private input cannot change the native selections or reveal either entity."); }
        public void DyRejectOld(PendingDecision old)
        { var before = DyState(); DyNeed(!DyG.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, DyG.Revision)).Accepted && before == DyState(), "Replaying the old private choice cannot exchange or draw twice."); }
        public void DyReach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 190; i++)
            {
                if (done()) return; DyNeed(DyG.State.Winner == Winner.None, "The fixed native fixture ended before boundary " + line); var p = DyP;
                if (p is null || p.PlayerSeat != 0) DyStep();
                else if (DyHold()) { DyCold(); DyAnswer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); }
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) DyAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    DyAnswer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected real Douyu frontier " + line + " " + DyDiagnostic());
            }
            throw new InvalidOperationException("Missing bounded Douyu frontier " + line + " " + DyDiagnostic());
        }
        public void DyCold() { var before = DyState(); DyG = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(DyG.CreateCheckpoint())), registry); DyNeed(before == DyState(), "Cold replay retains every private view, original native payment zone, unique logical assignment and exact draw child cursor."); }
        private string DyState() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(DyG.CreateSnapshot(s))), Frames = DyG.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = DyG.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), DyG.CardMovements, Zones = DyG.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(DyG.AcceptedCommands) });
        private string DyDiagnostic() => JsonSerializer.Serialize(new { Scene = scene, DyG.State.TurnNumber, DyG.State.Phase, Prompt = DyP, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, DyV(s).Hp, DyV(s).HandCount }), Frames = DyG.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = DyG.Events.TakeLast(10).Select(e => e.Payload) });
        public static DyScenario DyStart(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new DyFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Rebel : Role.Lord, PlayerCount = 4, ModeId = DyMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new DyScenario(scene, g, registry); s.DyAccept(new StartGameCommand()); s.DyReach(() => s.DyP is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.DyAccept(new SelectGeneralCommand(0, DyOwner, g.Revision, s.DyP!.PromptId)); s.DyPlay(); return s;
        }
    }
    private sealed class DyFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pang-ji-douyu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog c, params string[] only)
            { foreach (var (id, p) in c.Programs.Where(p => only.Length == 0 || only.Contains(p.Key))) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] }); }
            Add(SkillProgramCatalog.Load(DyRead("ol-pang-ji", "rules"), DyRead("ol-pang-ji", "presentation")), DySkill);
            if (scene == "jici") Add(SkillProgramCatalog.Load(DyRead("ol-wang-lang", "rules"), DyRead("ol-wang-lang", "presentation")));
            if (scene == "pile") Add(SkillProgramCatalog.Load(DyRead("ol-guan-hai", "rules"), DyRead("ol-guan-hai", "presentation")));
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards) => new() { ["id"] = id, ["minCards"] = cards, ["maxCards"] = cards, ["sourceZones"] = new JsonArray("hand"), ["minTargets"] = 1, ["maxTargets"] = 1, ["targetKind"] = "otherLivingWithHand", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            var driver = new JsonObject { ["id"] = DyDriver, ["revision"] = 1, ["activations"] = new JsonArray(
                A("pair", "{\"op\":\"pindian\",\"target\":\"selectedTarget\",\"amount\":1}", 1),
                A("top", "{\"op\":\"startPindian\",\"target\":\"owner\",\"opponentRef\":{\"kind\":\"selectedTarget\"},\"resultBind\":\"contest\",\"visibility\":\"public\"}", 0)) };
            if (scene == "jici") driver["triggers"] = N("[{\"id\":\"native-mark\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"changeParticipantMarker\",\"target\":\"owner\",\"marker\":\"raoShe\",\"amount\":7}]}]");
            var policy = new JsonArray();
            if (scene is "win" or "both" or "claim" or "paid-loss" or "paid-death" or "top" or "pile") policy.Add(N("{\"id\":\"native-final-rank\",\"kind\":\"pindianRankBySuit\",\"inputSuit\":\"spade\",\"value\":13}"));
            if (scene == "loss") policy.Add(N("{\"id\":\"native-final-rank\",\"kind\":\"pindianRankBySuit\",\"inputSuit\":\"spade\",\"value\":1}"));
            if (scene == "claim") policy.Add(N("{\"id\":\"native-claim\",\"kind\":\"pindianClaimAllWhenSourceWins\"}"));
            if (scene == "alcohol") policy.Add(N("{\"id\":\"native-alcohol\",\"kind\":\"alcoholKingIdentityRank\",\"cardKinds\":[\"alcohol\"]}"));
            if (scene == "top") policy.Add(N("{\"id\":\"native-top\",\"kind\":\"pindianTopCardChoice\"}"));
            var defs = new JsonArray(driver,
                N("{\"id\":\"" + DyWatch + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-reward-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"optional\":false,\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + DyDraw + "\"],\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"actual-child\",\"options\":[{\"id\":\"continue\"}]}" + (scene is "paid-loss" or "paid-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-death" ? 20 : 1) + "}" : "") + "]}]}"));
            if (policy.Count > 0) defs.Add(new JsonObject { ["id"] = "fixture:dy-policy", ["revision"] = 1, ["cardPolicies"] = policy });
            if (scene == "paid-loss") defs.Add(N("{\"id\":\"fixture:dy-conditional\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"native-health-source\",\"skillId\":\"" + DySkill + "\",\"minimumHp\":13}]}"));
            if (scene == "top") defs.Add(N("{\"id\":\"fixture:dy-peer-top\",\"revision\":1,\"cardPolicies\":[{\"id\":\"native-peer-top\",\"kind\":\"pindianTopCardChoice\"}]}"));
            if (scene == "paid-death") defs.Add(N("{\"id\":\"fixture:dy-peer-claim\",\"revision\":1,\"cardPolicies\":[{\"id\":\"native-living-claim\",\"kind\":\"pindianClaim\"}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Actual native blind Pindian fixture", optionLabels = n["id"]!.GetValue<string>() == DyWatch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            Add(SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            const string pick = "fixture:dy-pick"; b.AddSkill(new(pick, "Human", "Fixed public draft") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var skills = new List<string> { DyDriver, DyWatch, scene == "paid-loss" ? "fixture:dy-conditional" : DySkill };
            if (policy.Count > 0) skills.Add("fixture:dy-policy");
            if (scene == "jici") skills.AddRange(["ol:gushe", "ol:jici"]); if (scene == "pile") skills.AddRange(["ol:xiewei", "ol:youque"]);
            b.AddGeneral(new(DyOwner, "斗誉原生检查", "supporter", pick, "qun", 12, skills, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:dy-peer-" + i).ToArray();
            foreach (var i in Enumerable.Range(1, 3))
            {
                var peerPick = "fixture:dy-peer-pick-" + i; b.AddSkill(new(peerPick, "Peer", "Fixed public draft") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                b.AddGeneral(new(peers[i - 1], "原生拼点对手", "supporter", peerPick, "qun", 12, i == 1 && scene == "both" ? [DySkill] : scene == "top" ? ["fixture:dy-peer-top"] : scene == "paid-death" ? ["fixture:dy-peer-claim"] : [], GeneralGender.Male));
            }
            b.AddDeck(new("fixture:dy-deck", "Actual fixed rank-seven originals", 6, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(scene == "alcohol" ? "standard:alcohol" : "standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(DyMode, "真实私密拼点交换", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:dy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [DyOwner, .. peers]));
        }
    }
}
