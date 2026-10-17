using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class PangJiChecks
{
    private const string PgSkill = "ol:biguo", PgDriver = "fixture:pg-driver", PgWatch = "fixture:pg-watch", PgQuiet = "fixture:pg-quiet";
    private const string PgOwner = "fixture:pg-owner", PgMode = "identity:classic-pang-ji", PgCost = "skill-program.pang-ji.biguo.discard", PgGain = "skill-program.pang-ji.biguo.obtain";

    public static void BiguoActualDrawDiscardCategoryObtainAndTargetLeaseCold()
    {
        foreach (var scene in new[] { "gain", "gain-loss", "gain-lion" })
        {
            var s = PgScenario.PgStart(scene); var before = s.PgV().HandCount;
            s.PgNeed(s.PgG.State.Phase == TurnPhase.Draw && !s.PgE<BiguoArmedEvent>().Any(), "The genuine optional offer belongs to the end of this actual Draw phase, before Play or any paid arm.");
            s.PgPrivate(0); s.PgReject(); s.PgCold(); s.PgActivate();
            var f = s.PgOwn(); var id = f.Id; var source = f.PangJi!.Source;
            s.PgNeed(f.PangJi is { Paid: false, Stage: PangJiStage.CostChoice } && f.WindowContext?.Window == SkillProgramTriggerWindow.DrawPhaseEnded, "The original native draw-end binding owns its unpaid HE choice.");
            var costId = scene == "gain-lion" ? s.PgV().Equipment.Single(c => c.Kind == CardKind.SilverLion).Id : s.PgV().Hand[0].Id;
            var old = s.PgP!; s.PgPrivate(0); s.PgCold(); s.PgAnswer(c => c.Parameters.GetValueOrDefault("pang-ji") == "cost-card" && c.Cards.SequenceEqual([costId])); s.PgRejectOld(old);
            if (scene == "gain-lion")
            {
                s.PgReach(() => s.PgHold("health"));
                s.PgNeed(s.PgV().Hp == s.PgV().MaxHp && s.PgOwn(id).PangJi is { Paid: true, Stage: PangJiStage.CostChildren } && s.PgG.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any() && !s.PgE<BiguoObtainEntityPaidEvent>().Any(), "An actual equipped SilverLion cost resolves its true recovery child before obtaining any replacement cards."); s.PgCold();
            }
            s.PgReach(() => s.PgHold("cost")); var paid = s.PgE<BiguoCostPaidEvent>().Single();
            s.PgNeed(paid.FrameId == id && paid.CardId == costId && paid.From == (scene == "gain-lion" ? CardLocation.Equipment(0) : CardLocation.Hand(0)) && paid.To == CardLocation.DiscardPile && paid.Reason.Value == PgCost &&
                s.PgG.CardMovements.Count(m => m.Sequence == paid.Sequence && m.CardId == costId && m.From == paid.From && m.To == paid.To) == 1 && !s.PgE<BiguoObtainEntityIssuedEvent>().Any(), "Exactly one original HE entity pays a real logical discard before its movement children; no obtain happens early.");
            s.PgPrivate(0); s.PgCold(); s.PgContinue(); s.PgReach(() => s.PgHold("gain"));
            var obtained = s.PgE<BiguoObtainEntityPaidEvent>().Where(e => e.FrameId == id).OrderBy(e => e.Index).ToArray();
            var expected = scene == "gain-lion" ? 1 : 3;
            s.PgNeed(obtained.Length == expected && obtained.All(e => e.From == CardLocation.DrawPile && e.To == CardLocation.Hand(0) && s.PgG.CardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.Reason.Value == PgGain) == 1) && obtained.Select(e => PgCategory(e.PrintedKind)).Distinct().Count() == expected &&
                s.PgOwn(id).PangJi is { Paid: true, Stage: PangJiStage.ObtainChildren } && !s.PgE<BiguoArmedEvent>().Any() && !s.PgE<PangJiCompletedEvent>().Any(e => e.FrameId == id), "The publicly shown actual Deck entities contain at most one of each real category; obtain is not Draw and its children precede arming/completion.");
            s.PgNeed(s.PgE<ProgramCardsRevealedEvent>().Single(e => e.FrameId == id && e.Bind == "biguo-category-gain").Cards.Select(c => c.Id).SequenceEqual(obtained.Select(e => e.CardId)) &&
                Enumerable.Range(0, 4).All(v => obtained.All(e => s.PgG.CreateSnapshot(v).PublicRevealedCards.Any(c => c.Id == e.CardId))), "All viewers see exactly the real revealed obtain faces, while the cost selection remains private.");
            s.PgFrozen(s.PgOwn(id).PangJi!.Obtained); s.PgPrivate(0); s.PgCold(); s.PgPlay();
            var arm = s.PgE<BiguoArmedEvent>().Single();
            s.PgNeed(arm.FrameId == id && arm.Source == source && arm.ActualTurn == s.PgG.State.TurnNumber && arm.TurnOwner == 0 && s.PgE<PangJiCompletedEvent>().Count(e => e.FrameId == id && e.Paid) == 1 && s.PgV().HandCount == before - (scene == "gain-lion" ? 0 : 1) + expected &&
                (scene != "gain-loss" || !s.PgHas(PgSkill)), "The exact paid source arms only after its children, even if the real cost observer removed that source; missing Deck categories are never fabricated."); s.PgCold();
            if (scene == "gain-loss")
            {
                s.PgPrepareIncoming(2); s.PgUse("request", targets: [1]); s.PgReach(() => s.PgOptional(PgSkill)); s.PgActivate(); s.PgReach(() => s.PgNative("pindian-start"));
                var origin = s.PgE<BiguoTargetAcceptedEvent>().Single().Origin;
                s.PgNeed(!s.PgHas(PgSkill) && origin.Source == source && origin.ArmFrameId == id && s.PgG.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == origin.UseId).Action is { ActorSeat: 1 } a && a.TargetSeats.SequenceEqual([0]) &&
                    s.PgP!.Choices.Any(c => c.Targets.SequenceEqual([2])), "An already paid arm survives genuine source loss and targets a real foreign native Use, retaining a legal third-person Pindian choice."); s.PgPrivate(0); s.PgCold();
                s.PgChoosePindian(2); s.PgPlay(); s.PgNeed(s.PgE<BiguoPindianReturnedEvent>().Single().OpponentSeat == 2 && s.PgE<CardUseFinishedEvent>().Count(e => e.ResolutionId == origin.UseId) == 1, "The issued third-person contest and the original foreign Slash each return exactly once despite source loss.");
            }
        }
        var skip = PgScenario.PgStart("skip"); var hand = skip.PgV().HandCount; skip.PgAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); skip.PgPlay();
        skip.PgNeed(skip.PgV().HandCount == hand && !skip.PgE<BiguoCostCommittedEvent>().Any() && !skip.PgE<BiguoArmedEvent>().Any(), "Skipping the real optional draw-end offer pays nothing and creates no target entitlement."); skip.PgCold();
        var cancel = PgScenario.PgStart("cancel"); cancel.PgActivate(); cancel.PgAnswer(c => c.Parameters.GetValueOrDefault("pang-ji") == "cancel"); cancel.PgPlay();
        cancel.PgNeed(!cancel.PgE<BiguoCostPaidEvent>().Any() && !cancel.PgE<BiguoArmedEvent>().Any() && cancel.PgE<PangJiCompletedEvent>().Single() is { Paid: false }, "Cancelling the unpaid HE draft differs from paying and cannot arm later target triggers."); cancel.PgCold();
    }

    public static void BiguoNativeTargetPindianNotWinTypeBanAndExpiryCold()
    {
        foreach (var scene in new[] { "ban-win", "ban-loss", "ban-tie", "ban-user" })
        {
            var s = PgScenario.PgStart(scene); s.PgFinishGain(); var opponent = scene == "ban-user" ? 1 : 2; s.PgPrepareIncoming(opponent);
            s.PgUse("request", targets: [1]); s.PgReach(() => s.PgOptional(PgSkill));
            var incoming = s.PgG.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Action is { ActorSeat: 1, EffectiveKind: CardKind.Slash }); var incomingId = incoming.Id; var original = incoming.Action!.PhysicalCards.Single();
            s.PgNeed(incoming.Action.TargetSeats.SequenceEqual([0]) && !s.PgE<CardUseFinishedEvent>().Any(e => e.ResolutionId == incomingId), "An accepted actual foreign Slash remains the parent of the target-trigger Pindian, without resolving or paying twice.");
            s.PgPrivate(0); s.PgCold(); s.PgActivate(); s.PgReach(() => s.PgNative("pindian-start"));
            var p = s.PgG.ResolutionStack.OfType<PindianFrame>().Single(); var sourceCard = s.PgV().Hand.First(c => c.Kind == CardKind.Slash).Id;
            s.PgNeed(p.OpponentSeat is null && p.ParentProcessingCardIds?.Contains(original.CardId) == true && s.PgP!.Choices.Any(c => c.Targets.SequenceEqual([opponent]) && c.Cards.SequenceEqual([sourceCard])), "The real generic Pindian selector is independent of the original card user and preserves the already held Slash cost.");
            s.PgPrivate(0); s.PgReject(); s.PgCold(); s.PgAnswer(c => c.Parameters.GetValueOrDefault("action") == "pindian-start" && c.Targets.SequenceEqual([opponent]) && c.Cards.SequenceEqual([sourceCard]));
            s.PgNeed(s.PgP is { PlayerSeat: > 0 } && s.PgG.ResolutionStack.OfType<PindianFrame>().Single().OpponentSeat == opponent && s.PgV(opponent).Hand.Single().Kind == CardKind.Crossbow, "The actual selected other privately chooses its sole original Equipment entity; no source or target is substituted."); s.PgPrivate(opponent); s.PgCold(); s.PgAdvance();
            if (scene == "ban-user") s.PgReach(() => s.PgNative("pindian-claim")); else s.PgReach(() => s.PgP is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var determined = s.PgE<PindianResultDeterminedEvent>().Single(e => e.FrameId == p.Id); var result = determined.Result; var bans = s.PgE<BiguoCategoryBanEvent>().Where(e => e.PindianId == p.Id).ToArray();
            var losers = scene == "ban-tie" ? new[] { 0, opponent } : scene == "ban-loss" ? new[] { 0 } : new[] { opponent };
            s.PgNeed(result.SourceCardId == sourceCard && result.OpponentSeat == opponent && result.SourceRank == (scene == "ban-loss" ? 1 : scene == "ban-tie" ? 7 : 13) && result.OpponentRank == 7 &&
                bans.Length == losers.Length * 2 && losers.All(seat => bans.Where(e => e.Seat == seat).Select(e => e.Category).ToHashSet().SetEquals([SkillProgramCardCategory.Basic, SkillProgramCardCategory.Equipment])) &&
                s.PgE<BiguoPindianCardCategoryEvent>().Count(e => e.PindianId == p.Id) == 2 && !s.PgE<CardUseFinishedEvent>().Any(e => e.ResolutionId == incomingId), "Final actual ranks determine both types; ties ban both participants, and the bans exist while the original native Use is still accepted and unfinished.");
            if (scene == "ban-user")
            {
                s.PgNeed(s.PgG.ResolutionStack.OfType<PindianFrame>().Single().PindianStep == PindianStep.ClaimResult && bans.All(e => e.Seat == 1) && !s.PgE<BiguoPindianReturnedEvent>().Any(), "The original card user's ban is already effective before a real result-claim child, rather than after the contest returns.");
                s.PgPrivate(0); s.PgCold(); s.PgAnswer(c => c.Parameters.GetValueOrDefault("action") == "pindian-claim" && c.Parameters.GetValueOrDefault("take") == "true"); s.PgReach(() => s.PgHold("claim-gain"));
                s.PgNeed(bans.All(b => s.PgE<BiguoCategoryBanEvent>().Contains(b)) && !s.PgE<CardUseFinishedEvent>().Any(e => e.ResolutionId == incomingId), "A genuine claim movement child cannot delay the type ban or discard the original Slash's continuation."); s.PgCold();
                s.PgReach(() => s.PgP is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            }
            var response = s.PgP!; var hasDodge = response.Choices.Any(c => c.Cards.Count == 1);
            s.PgNeed(s.PgV().Hand.Any(c => c.Kind == CardKind.Slash) && hasDodge == !losers.Contains(0), "The same real Slash-to-Dodge conversion is offered only when the actual responder's effective Basic type is not banned.");
            s.PgPrivate(0); s.PgCold(); s.PgAnswer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-defense-request" or "faction-slash-request")); s.PgPlay();
            s.PgNeed(s.PgE<BiguoPindianReturnedEvent>().Count(e => e.PindianId == p.Id) == 1 && s.PgE<CardUseDeclaredEvent>().Count(e => e.ResolutionId == incomingId) == 1 && s.PgE<CardUseFinishedEvent>().Count(e => e.ResolutionId == incomingId) == 1 &&
                s.PgE<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == 0) && s.PgG.CardMovements.Count(m => m.CardId == original.CardId && m.From == original.From && m.To == CardLocation.Processing) == 1,
                "The original paid Slash completes all targets and actual damage once even when its user became Basic-banned during its own target window.");
            if (losers.Contains(0))
            {
                s.PgNeed(!s.PgG.GetHumanLegalActions().Any(a => a.CardId is { } card && s.PgV().Hand.Any(c => c.Id == card && PgCategory(c.Kind) is SkillProgramCardCategory.Basic or SkillProgramCardCategory.Equipment)), "A losing owner cannot publish a new Basic or Equipment Use while the turn remains open.");
                var id = s.PgV().Hand.First(c => c.Kind == CardKind.Slash).Id; s.PgRejectCommand(new PlayCardCommand(0, id, [1], s.PgG.Revision, s.PgP!.PromptId));
                s.PgGive(1, CardKind.Slash); s.PgUse("duel", targets: [1]); s.PgReach(() => s.PgP is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
                s.PgNeed(s.PgV().Hand.Any(c => c.Kind == CardKind.Slash) && s.PgP!.Choices.All(c => c.Cards.Count == 0), "A true Duel Played response is also banned despite a real Slash remaining in Hand; the rule is not limited to ordinary play menus."); s.PgCold(); s.PgAnswer(c => c.Cards.Count == 0); s.PgPlay();
            }
            if (scene == "ban-user")
            {
                s.PgGive(1, CardKind.Slash); var uses = s.PgE<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 1); s.PgUse("request", targets: [1]); s.PgPlay();
                s.PgNeed(s.PgV(1).Hand.Any(c => c.Kind == CardKind.Slash) && s.PgE<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 1) == uses, "A fresh forced request cannot spend the banned user's new real Basic card after the previous accepted Use finished.");
            }
            var turn = s.PgG.State.TurnNumber; s.PgEnd(); s.PgPlay();
            s.PgNeed(s.PgG.State.TurnNumber == turn && s.PgE<TurnEndedEvent>().Count(e => e.TurnNumber == turn && e.ActorSeat == 0) == 1 && s.PgG.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id && s.PgV().Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash)), "Native TurnEnded expires the ban before the genuine Dianjun inserted tail Play, without waiting for a new actual turn or cursor."); s.PgCold();
            var released = s.PgG.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id && s.PgV().Hand.Any(c => c.Id == id && c.Kind == CardKind.Slash) && a.TargetSeats.SequenceEqual([1]));
            s.PgAccept(new PlayCardCommand(0, released.CardId!.Value, released.TargetSeats, s.PgG.Revision, s.PgP!.PromptId, released.PlayedCardKind, released.TargetCardId) { ConversionSource = released.ConversionSource }); s.PgPlay();
            s.PgNeed(s.PgE<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0 && e.CardId == released.CardId), "The formerly banned type executes a real new native Use after the exact expiry boundary, rather than merely appearing in a query."); s.PgCold();
        }
    }

    private static SkillProgramCardCategory PgCategory(CardKind kind) => kind is CardKind.Slash or CardKind.Dodge or CardKind.Peach ? SkillProgramCardCategory.Basic : kind == CardKind.Duel ? SkillProgramCardCategory.Trick : SkillProgramCardCategory.Equipment;

    private sealed class PgScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine PgG { get; private set; } = game;
        public PendingDecision? PgP => Enumerable.Range(0, 4).Select(i => PgG.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot PgV(int seat = 0) => PgG.CreateSnapshot(seat).Players[seat];
        public T[] PgE<T>() => PgG.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool PgHas(string id) => PgV().Skills?.Any(s => s.ContentId == id) == true;
        public ProgramSkillFrame PgOwn(long? id = null) => PgG.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.PangJi is not null && (id is null || f.Id == id));
        public bool PgHold(string id) => PgP?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
        public bool PgNative(string id) => PgP?.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == id) == true;
        public bool PgOptional(string id) => PgP?.SkillPrompt?.SkillId == id && PgP.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void PgActivate() => PgAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void PgAdvance() => PgAccept(new AdvanceOneStepCommand(PgG.Revision));
        public void PgContinue() { if (PgP!.PlayerSeat == 0) PgAnswer(c => c.Parameters.ContainsKey("option-id")); else PgAdvance(); }
        public void PgUse(string id, int[]? targets = null, int[]? cards = null) => PgAccept(new UseProgramSkillCommand(0, PgDriver, id, cards ?? [], targets ?? [], PgG.Revision, PgP!.PromptId));
        public void PgEnd() => PgAccept(new EndPlayPhaseCommand(0, PgG.Revision, PgP!.PromptId));
        public void PgPlay() => PgReach(() => PgP is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PgFinishGain() { PgActivate(); PgAnswer(c => c.Parameters.GetValueOrDefault("pang-ji") == "cost-card" && PgV().Hand.Any(h => c.Cards.SequenceEqual([h.Id]))); PgPlay(); PgNeed(PgE<BiguoArmedEvent>().Length == 1, "The actual discard/obtain children must arm the later native target window."); }
        public void PgGive(int seat, CardKind kind) { var id = PgV().Hand.First(c => c.Kind == kind).Id; PgUse("give", targets: [seat], cards: [id]); PgPlay(); }
        public void PgTakeAll(int seat)
        {
            while (PgV(seat).HandCount > 0) { PgUse("take", targets: [seat]); PgReach(() => PgP?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); PgAnswer(c => c.Cards.Count == 0); PgPlay(); }
        }
        public void PgPrepareIncoming(int opponent) { PgTakeAll(opponent); PgGive(opponent, CardKind.Crossbow); PgGive(1, CardKind.Slash); }
        public void PgChoosePindian(int opponent) { var card = PgV().Hand.First(c => c.Kind == CardKind.Slash).Id; PgAnswer(c => c.Parameters.GetValueOrDefault("action") == "pindian-start" && c.Cards.SequenceEqual([card]) && c.Targets.SequenceEqual([opponent])); }
        public void PgPrivate(int seat) => PgNeed(PgP is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => PgG.CreateSnapshot(i).PendingDecision is null), "Only the real chooser receives its private native choice.");
        public void PgReject()
        {
            var p = PgP!; var state = PgState(); PgNeed(!PgG.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices[0].Id, PgG.Revision)).Accepted && PgState() == state && !PgG.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished-pang-ji"), PgG.Revision)).Accepted && PgState() == state, "Foreign or unpublished choices cannot pay, reveal, arm or change the original native parent.");
        }
        public void PgRejectCommand(GameCommand command) { var state = PgState(); PgNeed(!PgG.Submit(command).Accepted && state == PgState(), "An unpublished forbidden Use cannot move material, consume a quota or change the current decision."); }
        public void PgRejectOld(PendingDecision old) { var state = PgState(); PgNeed(!PgG.Submit(new AnswerPromptCommand(old.PlayerSeat, old.PromptId, old.Choices[0].Id, PgG.Revision)).Accepted && PgState() == state, "A genuine old cost prompt cannot answer a later native child decision."); }
        public void PgAnswer(Func<PromptChoice, bool> pick) { var p = PgP; var c = p?.Choices.FirstOrDefault(pick); PgNeed(p is { PlayerSeat: 0 } && c is not null, "The human must choose one actually published canonical input."); PgAccept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, PgG.Revision)); }
        public void PgReach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 260; i++)
            {
                if (done()) return; PgNeed(PgG.State.Winner == Winner.None, "Fixture ended before exact boundary at " + line); var p = PgP;
                if (p is null || p.PlayerSeat != 0) PgAdvance();
                else if (p.Kind == DecisionKind.PlayCard) PgEnd();
                else if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) PgContinue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) PgAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) PgAnswer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) PgAnswer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected Pang Ji boundary at " + line + " " + PgDiagnostic());
            }
            throw new InvalidOperationException("Missing bounded Pang Ji boundary at " + line + " " + PgDiagnostic());
        }
        public void PgAccept(GameCommand command)
        { try { var r = PgG.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); PgNeed(r.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(r)); } catch (Exception e) { throw new InvalidOperationException("Pang Ji scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + PgDiagnostic(), e); } }
        public void PgNeed(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + " " + PgDiagnostic()); }
        public void PgFrozen<T>(IReadOnlyList<T> list) { var frozen = false; try { ((IList)list).Add(default(T)); } catch (NotSupportedException) { frozen = true; } PgNeed(frozen, "The owning receipt's nested entity collections are detached and immutable."); }
        public void PgCold() { var before = PgState(); PgG = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(PgG.CreateCheckpoint())), registry); PgNeed(before == PgState(), "Cold replay preserves all four private views, the true original Use/Pindian parent, choices, paid sources, entities and once-only returns."); }
        private string PgState() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(PgG.CreateSnapshot(i))), Frames = PgG.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = PgG.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), PgG.CardMovements, Zones = PgG.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(PgG.AcceptedCommands) });
        private string PgDiagnostic() => JsonSerializer.Serialize(new { Scene = scene, PgG.State.TurnNumber, PgG.State.CurrentSeat, PgG.State.Phase, PgG.State.Winner, Prompt = PgP, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, PgV(i).Hp, PgV(i).MaxHp, PgV(i).HandCount, Skills = PgV(i).Skills?.Select(s => s.ContentId) }), Frames = PgG.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = PgG.Events.TakeLast(12).Select(e => e.Payload), Moves = PgG.CardMovements.TakeLast(8) });
        public static PgScenario PgStart(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new PgFixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = PgMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r); var s = new PgScenario(scene, g, r);
            s.PgAccept(new StartGameCommand()); s.PgReach(() => s.PgP is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.PgAccept(new SelectGeneralCommand(0, PgOwner, g.Revision, s.PgP!.PromptId));
            if (scene == "gain-lion") { s.PgReach(() => s.PgP?.SkillPrompt?.SkillId == PgDriver && s.PgP.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")); s.PgAnswer(c => c.Targets.SequenceEqual([0])); }
            s.PgReach(() => s.PgOptional(PgSkill)); return s;
        }
    }

    private sealed class PgFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-pang-ji-biguo-core", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string part) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-pang-ji." + part + ".json") ?? throw new InvalidOperationException("Missing actual Pang Ji embedded " + part); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in new[] { PgSkill, "ol:douyu" }) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description) { Program = production.Programs[id], ProgramPresentation = production.Presentations[id] });
            if (scene.StartsWith("ban-", StringComparison.Ordinal)) CharacterFixtureContent.Register(b, "ol:zhang-yi");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string C(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => new() { ["id"] = id, ["minCards"] = cards, ["maxCards"] = cards, ["sourceZones"] = new JsonArray("hand"), ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static JsonNode T(string id, string window, string effects, string more = "") => N("{\"id\":\"" + id + "\",\"window\":\"" + window + "\",\"subject\":\"owner\",\"optional\":false," + more + "\"effects\":[" + effects + "]}");
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var driver = new JsonObject { ["id"] = PgDriver, ["revision"] = 1, ["activations"] = new JsonArray(
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\",\"amount\":1}", cards: 1, targets: 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("request", "{\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"request\"}", targets: 1), duel),
                ["viewAs"] = N("[{\"id\":\"actual-dodge\",\"inputKinds\":[\"slash\"],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true}]"),
                ["modifiers"] = N("[{\"id\":\"range\",\"query\":\"slashDistanceLimit\",\"operation\":\"unlimited\",\"value\":0,\"priority\":0},{\"id\":\"quota\",\"query\":\"slashLimit\",\"operation\":\"add\",\"value\":5,\"priority\":0}]") };
            if (scene == "gain-lion") driver["triggers"] = new JsonArray(T("real-equipped-lion", "gameStarting", "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"},{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"lion\"},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"));
            var watches = new JsonArray(T("real-cost-child", "cardsMoved", C("cost") + (scene == "gain-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : ""), "\"sourceZones\":[\"hand\",\"equipment\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + PgCost + "\"],"),
                T("real-obtain-child", "cardsGained", C("gain"), "\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + PgGain + "\"],"),
                T("real-lion-recovery", "afterHpRecovered", C("health")),
                T("real-claim-child", "cardsGained", C("claim-gain"), "\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"program.pindian.claim\"],"));
            var rank = scene == "ban-loss" ? 1 : scene == "ban-tie" ? 7 : 13;
            var policies = new JsonArray(N("{\"id\":\"actual-final-rank\",\"kind\":\"pindianRankBySuit\",\"inputSuit\":\"spade\",\"value\":" + rank + "}"));
            if (scene == "ban-user") policies.Add(N("{\"id\":\"actual-claim\",\"kind\":\"pindianClaimAllWhenSourceWins\"}"));
            var defs = new JsonArray(driver, new JsonObject { ["id"] = PgWatch, ["revision"] = 1, ["triggers"] = watches },
                new JsonObject { ["id"] = PgQuiet, ["revision"] = 1, ["triggers"] = new JsonArray(T("quiet", "afterNormalDraw", "{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}")) },
                new JsonObject { ["id"] = "fixture:pg-rank", ["revision"] = 1, ["cardPolicies"] = policies });
            if (scene == "gain-loss") defs.Add(N("{\"id\":\"fixture:pg-conditional\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"actual-health-source\",\"skillId\":\"ol:biguo\",\"minimumHp\":13}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Small genuine native Pang Ji command fixture", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "True native command fixture") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string pick = "fixture:pg-pick"; b.AddSkill(new(pick, "Human", "Public draft") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var skills = new List<string> { PgDriver, PgWatch, "fixture:pg-rank", scene == "gain-loss" ? "fixture:pg-conditional" : PgSkill }; if (scene.StartsWith("ban-", StringComparison.Ordinal)) skills.Add("ol:dianjun");
            b.AddGeneral(new(PgOwner, "愎果原生检查", "supporter", pick, "qun", 12, skills, GeneralGender.Male)); var peers = Enumerable.Range(1, 3).Select(i => "fixture:pg-peer-" + i).ToArray();
            foreach (var i in Enumerable.Range(1, 3)) { var p = "fixture:pg-pick-" + i; b.AddSkill(new(p, "Peer", "Public draft") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "原生其他角色", "supporter", p, "qun", 12, [PgQuiet], GeneralGender.Male)); }
            var kinds = scene == "gain-lion" ? new[] { CardKind.SilverLion } : new[] { CardKind.Slash, CardKind.Duel, CardKind.Crossbow };
            foreach (var kind in kinds) b.AddCard(new("fixture:pg-" + kind, "原实体" + kind, CardCatalog.Get(kind).CategoryName, "Actual physical card", kind));
            b.AddDeck(new("fixture:pg-deck", "63 fixed rank-seven real entities", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 63).Select(i => new ContentDeckPhysicalCard("fixture:pg-" + kinds[i % kinds.Length], Suit.Spade, 7)).ToArray() });
            b.AddMode(new(PgMode, "真实摸牌结束与原生拼点母窗", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:pg-deck", GeneralCandidateCount: 4, GeneralPoolIds: [PgOwner, .. peers]));
        }
    }
}
