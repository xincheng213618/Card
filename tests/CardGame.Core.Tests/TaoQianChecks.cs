using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class TaoQianChecks
{
    private const string TqZongluan = "ol:zongluan", TqZhaohuo = "ol:zhaohuo", TqWenren = "ol:wenren";
    private const string TqDriver = "fixture:tq-driver", TqWatch = "fixture:tq-watch", TqQuiet = "fixture:tq-quiet", TqCounter = "fixture:tq-counter", TqConditional = "fixture:tq-source";
    private const string TqOwner = "fixture:tq-owner", TqMode = "identity:classic-tao-qian", TqGift = "skill-program.tao-qian.zhaohuo.give", TqDiscard = "skill-program.tao-qian.zongluan.discard";

    public static void ZongluanWholeVirtualSlashAndUniqueDamagePaymentCold()
    {
        foreach (var scene in new[] { "zong", "zong-counter", "zong-loss", "zong-short", "zong-lion" })
        {
            var s = TqScenario.Start(scene, preparation: true); s.TqActivate(); s.TqReach(() => s.TqBranch("actor")); s.TqPrivate(0); s.TqReject(); s.TqCold();
            s.TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == "actor" && c.Targets.SequenceEqual([0]));
            var root = s.TqOwn(); var id = root.Id; s.TqNeed(root.TaoQian is { Paid: true, ActorSeat: 0, Stage: TaoQianStage.SlashTargets } && !s.TqBranch("cancel") &&
                !s.TqP!.Choices.Where(c => c.Parameters.GetValueOrDefault("tao-qian") == "slash-toggle").SelectMany(c => c.Targets).Contains(2),
                "The real chosen user owns the non-cancellable target draft, and its ordinary one-range Slash excludes the opposite seat.");
            s.TqToggle("slash-toggle", 1); var old = s.TqP!; s.TqToggle("slash-toggle", 3); s.TqRejectOld(old); s.TqFrozen(s.TqOwn(id).TaoQian!.Targets); s.TqCold(); s.TqBranchAnswer("slash-confirm");
            s.TqReach(() => s.TqHold("committed")); var use = s.TqG.ResolutionStack.OfType<CardUseFrame>().Single(u => u.ZongluanSlash?.ProgramFrameId == id); var action = use.Action!;
            s.TqNeed(action.Type == CardActionType.Use && action.ActorSeat == 0 && action.ProviderSeat == 0 && action.RequesterSeat is null && action.EffectiveKind == CardKind.Slash &&
                action.PhysicalCards.Count == 0 && action.EffectiveSuit == Suit.None && action.EffectiveRank == 0 && action.EffectiveIsRed is null && action.TargetSeats.SequenceEqual([1, 3]) &&
                action.ConversionChain.SequenceEqual([root.TaoQian!.Source]) && s.TqE<ZongluanSlashIssuedEvent>().Single().Origin.UseId == use.Id && !s.TqE<ZongluanDiscardIssuedEvent>().Any(),
                "One real multi-target native Slash preserves its actor, exact paid source and genuinely colorless zero-material appearance; it is not two independent uses.");
            s.TqPrivate(0); s.TqCold();
            s.TqReach(() => s.TqHold("completed"));
            s.TqNeed(s.TqG.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == use.Id).Step == ResolutionFrameStep.Completed &&
                s.TqE<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id) == 1 && !s.TqE<ZongluanSlashReturnedEvent>().Any() && !s.TqE<ZongluanDiscardIssuedEvent>().Any(),
                "All actual targets and damage/gift/counter children finish before the completed-Use child; the owning skill cannot issue its discard early.");
            s.TqPrivate(0); s.TqCold(); s.TqContinue(); s.TqReach(() => s.TqBranch("discard-confirm") || s.TqBranch("discard-card"));
            var returned = s.TqE<ZongluanSlashReturnedEvent>().Single(); var cost = s.TqE<ZongluanDiscardIssuedEvent>().Single();
            s.TqNeed(returned is { DamageRecipientCount: 2 } && cost.DamageRecipientCount == 2 && cost.RequiredCount == (scene == "zong-short" ? 1 : 2) &&
                s.TqE<ZongluanDirectDamageEvent>().Where(e => e.UseId == use.Id).Select(e => e.TargetSeat).Order().SequenceEqual([1, 3]) &&
                !s.TqG.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == use.Id) && (scene != "zong-loss" || !s.TqHas(TqZongluan)),
                "The whole native return counts distinct direct positive recipients and publishes only the real affordable HE cost, retaining accepted rights after source loss.");
            if (scene == "zong-counter")
                s.TqNeed(s.TqE<DamageAppliedEvent>().Select(e => e.TargetSeat).Distinct().Order().SequenceEqual([0, 1, 3]) && s.TqE<DamageAppliedEvent>().Count(e => e.SourceSeat != 0 && e.TargetSeat == 0) == 2,
                    "Real after-damage counter-skill children injure a third person, but never inflate this Slash's two direct-recipient discard obligation.");
            var selected = new List<int>();
            if (scene == "zong-lion") selected.Add(s.TqV().Equipment.Single(c => c.Kind == CardKind.SilverLion).Id);
            else if (s.TqE<ZhaohuoGiftPaidEvent>().FirstOrDefault() is { } received) selected.Add(received.CardId);
            foreach (var first in selected) s.TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == "discard-card" && c.Cards.SequenceEqual([first]));
            while (!s.TqBranch("discard-confirm"))
            {
                var card = s.TqP!.Choices.First(c => c.Parameters.GetValueOrDefault("tao-qian") == "discard-card").Cards.Single(); selected.Add(card);
                s.TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == "discard-card" && c.Cards.SequenceEqual([card]));
            }
            s.TqPrivate(0); s.TqCold(); s.TqBranchAnswer("discard-confirm");
            if (scene == "zong-lion")
            {
                s.TqReach(() => s.TqHold("health"));
                s.TqNeed(s.TqOwn(id).TaoQian is { Stage: TaoQianStage.DiscardChildren } && s.TqV().Hp == s.TqV().MaxHp && s.TqG.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any() &&
                    !s.TqE<TaoQianCompletedEvent>().Any(e => e.FrameId == id), "A genuine equipped SilverLion cost owns its native recovery child before the paid skill may complete."); s.TqCold();
            }
            s.TqPlay();
            s.TqNeed(s.TqE<ZongluanDiscardEntityEvent>().Count(e => e.FrameId == id) == cost.RequiredCount && selected.All(card => s.TqG.CardMovements.Count(m => m.CardId == card && m.To == CardLocation.DiscardPile && m.Reason.Value == TqDiscard) == 1) &&
                s.TqE<TaoQianCompletedEvent>().Count(e => e.FrameId == id && e.Paid) == 1 && s.TqE<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id) == 1,
                "The exact original HE entities, including newly received marked gifts or Lion, pay once after the single whole native Use."); s.TqCold();
        }

        var foreign = TqScenario.Start("zong-foreign", preparation: true); foreign.TqActivate(); foreign.TqReach(() => foreign.TqBranch("actor"));
        foreign.TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == "actor" && c.Targets.SequenceEqual([1])); foreign.TqNeed(foreign.TqP!.PlayerSeat == 1 && foreign.TqBranch("slash-toggle"), "A foreign selected actor, rather than the skill owner, really chooses its legal Slash targets.");
        foreign.TqPrivate(1); foreign.TqCold(); foreign.TqReach(() => foreign.TqHold("committed"));
        var foreignUse = foreign.TqG.ResolutionStack.OfType<CardUseFrame>().Single(u => u.ZongluanSlash is not null);
        foreign.TqNeed(foreignUse.Action is { ActorSeat: 1, ProviderSeat: 1, PhysicalCards.Count: 0 } a && a.ConversionChain is [var source] && source.OwnerSeat == 0 && source.SkillId == TqZongluan && a.TargetSeats.All(t => t is 0 or 2),
            "The true foreign native user preserves normal range and the original issuer's independent paid source without masquerading as an assisted supply.");
        foreign.TqPrivate(1); foreign.TqCold(); foreign.TqPlay();
        foreign.TqNeed(foreign.TqE<ZongluanSlashReturnedEvent>().Count(e => e.UseId == foreignUse.Id) == 1 && foreign.TqE<TaoQianCompletedEvent>().Any(e => e.Paid), "A genuine AI target draft and all native children return to the original issuer exactly once."); foreign.TqCold();

        var declined = TqScenario.Start("zong-skip", preparation: true); declined.TqAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); declined.TqPlay();
        declined.TqNeed(!declined.TqE<ZongluanCommittedEvent>().Any() && !declined.TqE<ZongluanSlashIssuedEvent>().Any() && !declined.TqE<ZongluanDiscardEntityEvent>().Any(), "Declining the real optional Preparation offer issues neither native Use nor discard debt."); declined.TqCold();
    }

    public static void ZhaohuoFirstVictimDamageOriginalHandAndTurnTimingCold()
    {
        var s = TqScenario.Start("gift"); s.TqUse("lose", targets: [1]); s.TqPlay();
        s.TqNeed(!s.TqE<ZhaohuoFirstVictimDamageEvent>().Any(), "Actual HP loss is not a positive damage occurrence.");
        s.TqUse("hit-two", targets: [1]); s.TqReach(() => s.TqZhaoChoice()); var before = s.TqV().HandCount; var original = s.TqP!.Choices[0].Cards.Single();
        s.TqNeed(s.TqP.PlayerSeat == 1 && s.TqP.Choices.All(c => c.Cards is [var id] && s.TqV(1).Hand.Any(h => h.Id == id)) && s.TqE<ZhaohuoFirstVictimDamageEvent>().Single() is { VictimSeat: 1, Amount: 2, HandCount: 4 },
            "One actual Damage2 freezes the first positive occurrence and gives the actual victim its own private original-Hand choice.");
        s.TqPrivate(1); s.TqReject(); s.TqCold(); s.TqAdvance(); s.TqReach(() => s.TqHold("gift"));
        var paid = s.TqE<ZhaohuoGiftPaidEvent>().Single(); var giftRoot = s.TqG.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zhaohuo is not null);
        s.TqNeed(paid.CardId == original && paid.DonorSeat == 1 && paid.From == CardLocation.Hand(1) && paid.To == CardLocation.Hand(0) && s.TqV().HandCount == before + 1 &&
            s.TqG.CardMovements.Count(m => m.Sequence == paid.MovementSequence && m.CardId == original && m.From == paid.From && m.To == paid.To && m.Reason.Value == TqGift) == 1 &&
            giftRoot.Zhaohuo is { Paid: true, Stage: ZhaohuoStage.GiftChildren } && !s.TqE<ZhaohuoCompletedEvent>().Any(e => e.FrameId == giftRoot.Id),
            "Exactly one real Hand-to-Hand entity pays before its genuine gain child; multi-point damage does not ask for multiple gifts.");
        s.TqNeed(Enumerable.Range(1, 3).All(v => !s.TqG.CreateSnapshot(v).PublicRevealedCards.Any(c => c.Id == original) && s.TqG.CreateSnapshot(v).Players[0].Hand.Count == 0), "Giving a hidden card does not publish either Hand's private face.");
        s.TqPrivate(0); s.TqCold(); s.TqPlay(); s.TqUse("hit", targets: [1]); s.TqPlay(); s.TqNeed(s.TqE<ZhaohuoGiftPaidEvent>().Length == 1, "The same victim's second actual damage in this turn cannot give another card.");
        s.TqUse("hit", targets: [2]); s.TqPlay(); s.TqNeed(s.TqE<ZhaohuoGiftPaidEvent>().Select(e => e.DonorSeat).Order().SequenceEqual([1, 2]), "A different victim has its own first-damage obligation in the same actual turn.");
        s.TqNextOwnPlay(); s.TqUse("hit", targets: [1]); s.TqPlay(); s.TqNeed(s.TqE<ZhaohuoGiftPaidEvent>().Count(e => e.DonorSeat == 1) == 2 && s.TqE<ZhaohuoGiftPaidEvent>().Where(e => e.DonorSeat == 1).Select(e => e.ActualTurn).Distinct().Count() == 2,
            "A new actual own turn refreshes each victim's first occurrence, rather than each damage point or each current skill frame."); s.TqCold();

        var self = TqScenario.Start("gift-self"); var ownId = self.TqV().Hand[0].Id; var moves = self.TqG.CardMovements.Count;
        self.TqUse("hit", targets: [0]); self.TqReach(() => self.TqZhaoChoice()); self.TqPrivate(0); self.TqReject(); self.TqCold(); self.TqAnswer(c => c.Cards.SequenceEqual([ownId])); self.TqPlay();
        var same = self.TqE<ZhaohuoSelfGiftCommittedEvent>().Single();
        self.TqNeed(same.CardId == ownId && same.Source.OwnerSeat == 0 && same.SequenceBefore == same.SequenceAfter && self.TqG.CardMovements.Count == moves &&
            !self.TqE<ZhaohuoGiftPaidEvent>().Any() && self.TqV().Hand.Any(c => c.Id == ownId) && !self.TqG.GetHumanLegalActions().Any(a => a.CardId == ownId),
            "Self damage uses a real private self-Hand selection and exact scalar mark with zero movement, never a fake Hand-to-same-Hand gain or loss."); self.TqCold();

        var empty = TqScenario.Start("gift-empty"); empty.TqTakeAll(1); empty.TqUse("hit", targets: [1]); empty.TqPlay();
        empty.TqNeed(empty.TqE<ZhaohuoFirstVictimDamageEvent>().Single() is { VictimSeat: 1, HandCount: 0 } && !empty.TqE<ZhaohuoGiftCommittedEvent>().Any(), "A genuine empty-Hand first damage is still the first occurrence, with no fabricated material.");
        empty.TqUse("draw-peer", targets: [1]); empty.TqPlay(); empty.TqUse("hit", targets: [1]); empty.TqPlay();
        empty.TqNeed(empty.TqV(1).HandCount == 1 && !empty.TqE<ZhaohuoGiftCommittedEvent>().Any(), "Obtaining a real card after the first empty-Hand injury cannot retroactively create a later obligation."); empty.TqCold();

        var prevented = TqScenario.Start("gift-prevent"); prevented.TqUse("hit", targets: [1]); prevented.TqReach(() => prevented.TqOptional("fixture:tq-prevent"));
        prevented.TqPrivate(1); prevented.TqCold(); prevented.TqAdvance(); prevented.TqPlay();
        prevented.TqNeed(prevented.TqE<ProgramDamagePreventedEvent>().Length == 1 && !prevented.TqE<ZhaohuoFirstVictimDamageEvent>().Any(), "A genuinely prevented damage attempt consumes no first-positive occurrence.");
        prevented.TqUse("hit", targets: [1]); prevented.TqPlay(); prevented.TqNeed(prevented.TqE<ZhaohuoGiftPaidEvent>().Length == 1, "The later first actual positive injury still issues exactly one original Hand gift."); prevented.TqCold();

        foreach (var scene in new[] { "gift-loss", "gift-death" })
        {
            var loss = TqScenario.Start(scene); loss.TqUse("hit", targets: [1]); loss.TqReach(() => loss.TqHold("gift")); var invoice = loss.TqE<ZhaohuoGiftPaidEvent>().Single(); loss.TqCold(); loss.TqContinue();
            loss.TqReach(() => loss.TqE<ZhaohuoCompletedEvent>().Any(e => e.FrameId == invoice.FrameId));
            loss.TqNeed(loss.TqE<ZhaohuoGiftPaidEvent>().Length == 1 && loss.TqE<ZhaohuoCompletedEvent>().Count(e => e.FrameId == invoice.FrameId && e.Paid) == 1 &&
                loss.TqG.CardMovements.Count(m => m.Sequence == invoice.MovementSequence && m.CardId == invoice.CardId && m.Reason.Value == TqGift) == 1 &&
                (scene == "gift-death" ? !loss.TqV().IsAlive : loss.TqV().IsAlive && !loss.TqHas(TqZhaohuo)),
                "A genuine paid gift drains its native children once through source loss or nonterminal issuer death, without asking for or charging another entity."); loss.TqCold();
        }

        var late = TqScenario.Start("gift-late"); late.TqUse("hit", targets: [1]); late.TqPlay(); late.TqUse("grant-zhaohuo"); late.TqPlay(); late.TqUse("hit", targets: [1]); late.TqPlay();
        late.TqNeed(late.TqHas(TqZhaohuo) && late.TqE<ZhaohuoFirstVictimDamageEvent>().Length == 1 && !late.TqE<ZhaohuoGiftCommittedEvent>().Any(), "Acquiring the skill after this turn's first injury cannot turn a second injury into the first."); late.TqCold();
    }

    private sealed class TqScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine TqG { get; private set; } = game;
        public PendingDecision? TqP => Enumerable.Range(0, 4).Select(i => TqG.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot TqV(int seat = 0) => TqG.CreateSnapshot(seat).Players[seat];
        public T[] TqE<T>() => TqG.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool TqHas(string id) => TqV().Skills?.Any(s => s.ContentId == id) == true;
        public ProgramSkillFrame TqOwn(long? id = null) => TqG.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.TaoQian is not null && (id is null || f.Id == id));
        public bool TqBranch(string b) => TqP?.Choices.Any(c => c.Parameters.GetValueOrDefault("tao-qian") == b) == true;
        public bool TqZhaoChoice() => TqP?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zhaohuo") == true;
        public bool TqHold(string id) => TqP?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
        public bool TqOptional(string id) => TqP?.SkillPrompt?.SkillId == id && TqP.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void TqActivate() => TqAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void TqBranchAnswer(string b) => TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == b);
        public void TqToggle(string b, int seat) { var current = TqOwn().TaoQian!.Targets; TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == b && c.Targets.SequenceEqual(current.Append(seat))); }
        public void TqContinue() { if (TqP!.PlayerSeat == 0) TqAnswer(c => c.Parameters.ContainsKey("option-id")); else TqAdvance(); }
        public void TqAdvance() => TqAccept(new AdvanceOneStepCommand(TqG.Revision));
        public void TqUse(string id, int[]? targets = null, int[]? cards = null) => TqAccept(new UseProgramSkillCommand(0, TqDriver, id, cards ?? [], targets ?? [], TqG.Revision, TqP!.PromptId));
        public void TqEnd() => TqAccept(new EndPlayPhaseCommand(0, TqG.Revision, TqP!.PromptId));
        public void TqPlay() => TqReach(() => TqP is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void TqNextOwnPlay() { var turn = TqG.State.TurnNumber; TqEnd(); TqReach(() => TqG.State.TurnNumber > turn && TqP is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); }
        public void TqTakeAll(int seat)
        {
            while (TqV(seat).HandCount > 0) { TqUse("take", targets: [seat]); TqReach(() => TqP?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); TqAnswer(c => c.Cards.Count == 0); TqPlay(); }
        }
        public void TqPrivate(int seat) => TqNeed(TqP is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => TqG.CreateSnapshot(i).PendingDecision is null), "Only the actual native chooser receives the original private menu.");
        public void TqReject()
        {
            var p = TqP!; var state = TqState(); TqNeed(!TqG.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices[0].Id, TqG.Revision)).Accepted && TqState() == state &&
                !TqG.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished-tao-qian"), TqG.Revision)).Accepted && TqState() == state, "Foreign or unpublished input cannot alter the real source, entities, quota or current private decision.");
        }
        public void TqRejectOld(PendingDecision old) { var state = TqState(); TqNeed(!TqG.Submit(new AnswerPromptCommand(old.PlayerSeat, old.PromptId, old.Choices[0].Id, TqG.Revision)).Accepted && TqState() == state, "A genuinely stale consecutive prompt cannot answer the current target or cost decision."); }
        public void TqAnswer(Func<PromptChoice, bool> pick)
        { var p = TqP; var c = p?.Choices.FirstOrDefault(pick); TqNeed(p is { PlayerSeat: 0 } && c is not null, "The human must choose one genuinely published canonical input."); TqAccept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, TqG.Revision)); }
        public void TqReach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 260; i++)
            {
                if (done()) return; TqNeed(TqG.State.Winner == Winner.None, "Fixture ended before boundary at " + line); var p = TqP;
                if (p is null || p.PlayerSeat != 0) TqAdvance();
                else if (p.Kind == DecisionKind.PlayCard) TqEnd();
                else if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) TqContinue();
                else if (TqZhaoChoice()) TqAnswer(c => c.Cards.Count == 1);
                else if (TqBranch("discard-card")) TqAnswer(c => c.Parameters.GetValueOrDefault("tao-qian") == "discard-card");
                else if (TqBranch("discard-confirm")) TqBranchAnswer("discard-confirm");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) TqAnswer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) TqAnswer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) TqAnswer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected Tao Qian boundary at " + line + " " + TqDiagnostic());
            }
            throw new InvalidOperationException("Missing bounded Tao Qian boundary at " + line + " " + TqDiagnostic());
        }
        public void TqAccept(GameCommand command)
        {
            try { var result = TqG.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); TqNeed(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + TqDiagnostic(), e); }
        }
        public void TqNeed(bool value, string why) { if (!value) throw new InvalidOperationException(why + " " + TqDiagnostic()); }
        public void TqFrozen<T>(IReadOnlyList<T> list) { var rejected = false; try { ((IList)list).Add(default(T)); } catch (NotSupportedException) { rejected = true; } TqNeed(rejected, "Owning selections expose detached frozen collections."); }
        public void TqCold() { var state = TqState(); TqG = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(TqG.CreateCheckpoint())), registry); TqNeed(state == TqState(), "Cold replay preserves every viewer, real native parent, private choice, whole-Use return and exact paid entity."); }
        private string TqState() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(TqG.CreateSnapshot(i))), Frames = TqG.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = TqG.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), TqG.CardMovements, Zones = TqG.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(TqG.AcceptedCommands) });
        private string TqDiagnostic() => JsonSerializer.Serialize(new { Scene = scene, TqG.State.TurnNumber, TqG.State.CurrentSeat, TqG.State.Phase, TqG.State.Winner, Prompt = TqP, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, TqV(i).Hp, TqV(i).MaxHp, TqV(i).IsAlive, TqV(i).HandCount, Skills = TqV(i).Skills?.Select(s => s.ContentId) }), Frames = TqG.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = TqG.Events.TakeLast(12).Select(e => e.Payload), Moves = TqG.CardMovements.TakeLast(8) });
        public static TqScenario Start(string scene, bool preparation = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new TqFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "gift-death" ? Role.Renegade : Role.Lord, ModeId = TqMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new TqScenario(scene, g, r); s.TqAccept(new StartGameCommand()); s.TqReach(() => s.TqP is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.TqAccept(new SelectGeneralCommand(0, TqOwner, g.Revision, s.TqP!.PromptId));
            if (scene == "zong-lion")
            {
                s.TqReach(() => s.TqP?.SkillPrompt?.SkillId == TqDriver && s.TqP.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
                s.TqAnswer(c => c.Targets.SequenceEqual([0]));
            }
            if (preparation) s.TqReach(() => s.TqOptional(TqZongluan)); else s.TqPlay(); return s;
        }
    }

    private sealed class TqFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-tao-qian-core", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static JsonNode N(string value) => JsonNode.Parse(value)!;
            static string C(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            static JsonNode A(string id, string effects, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            static JsonNode T(string id, string window, string subject, string effects, string more = "") => N("{\"id\":\"" + id + "\",\"window\":\"" + window + "\",\"subject\":\"" + subject + "\",\"optional\":false," + more + "\"effects\":[" + effects + "]}");
            TqProduction(b);
            var driver = new JsonObject { ["id"] = TqDriver, ["revision"] = 1, ["activations"] = new JsonArray(
                A("hit", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", 1), A("hit-two", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":2}", 1),
                A("lose", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", 1), A("draw-peer", "{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":1}", 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", 1),
                A("grant-zhaohuo", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + TqZhaohuo + "\"]}")) };
            if (scene == "zong-lion") driver["triggers"] = new JsonArray(T("real-start-equipment", "gameStarting", "owner", "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"},{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"lion\"},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"));
            var watches = new JsonArray(T("real-health", "afterHpRecovered", "owner", C("health")));
            if (scene.StartsWith("zong", StringComparison.Ordinal))
            {
                var committed = T("real-committed", "cardUseCommitted", "owner", C("committed"), "\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],").AsObject();
                var completed = T("real-completed", "cardUseCompleted", "owner", C("completed"), "\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],").AsObject();
                committed.Remove("subject"); completed.Remove("subject");
                watches.Add(committed); watches.Add(completed);
            }
            var afterGift = scene is "zong-loss" or "gift-loss" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : scene == "gift-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}" : "";
            watches.Add(T("real-original-gift", "cardsGained", "owner", C("gift") + afterGift, "\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + TqGift + "\"],"));
            var defs = new JsonArray(driver, new JsonObject { ["id"] = TqWatch, ["revision"] = 1, ["triggers"] = watches },
                new JsonObject { ["id"] = TqQuiet, ["revision"] = 1, ["triggers"] = new JsonArray(T("real-skip-play", "afterNormalDraw", "owner", "{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}")) });
            if (scene is "zong-loss" or "gift-loss") defs.Add(N("{\"id\":\"" + TqConditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\"" + (scene == "zong-loss" ? TqZongluan : TqZhaohuo) + "\",\"minimumHp\":13}]}"));
            if (scene == "zong-counter") defs.Add(new JsonObject { ["id"] = TqCounter, ["revision"] = 1, ["triggers"] = new JsonArray(T("real-counter", "afterDamageApplied", "owner", C("counter") + ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"eventSource\"},{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", "\"damageOccurrence\":\"perDamage\",\"usageScope\":\"game\",\"usageLimit\":1,")) });
            if (scene == "gift-prevent") defs.Add(N("{\"id\":\"fixture:tq-prevent\",\"revision\":1,\"triggers\":[{\"id\":\"real-first-prevent\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":true,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Small actual native commands", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Genuine small fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:tq-pick"; b.AddSkill(new(pick, "Human", "Public general selection") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var own = new List<string> { TqDriver, TqWatch };
            if (scene.StartsWith("zong", StringComparison.Ordinal)) own.Add(scene == "zong-loss" ? TqConditional : TqZongluan);
            if (scene is "zong" or "zong-counter" or "zong-loss") own.Add(TqZhaohuo);
            if (scene.StartsWith("gift", StringComparison.Ordinal) && scene != "gift-late") own.Add(scene == "gift-loss" ? TqConditional : TqZhaohuo);
            b.AddGeneral(new(TqOwner, "陶谦真实技能夹具", "ol-tao-qian", pick, "qun", 12, own, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:tq-peer-" + i).ToArray();
            foreach (var i in Enumerable.Range(1, 3))
            {
                var peerPick = "fixture:tq-pick-" + i; b.AddSkill(new(peerPick, "Peer", "Public selection") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string> { TqQuiet, TqWatch }; if (scene == "zong-counter") skills.Add(TqCounter); if (scene == "gift-prevent") skills.Add("fixture:tq-prevent");
                b.AddGeneral(new(peers[i - 1], "原生其他角色", "supporter", peerPick, "qun", 12, skills, GeneralGender.Male));
            }
            const string entity = "fixture:tq-entity"; var kind = scene == "zong-lion" ? CardKind.SilverLion : CardKind.Slash;
            b.AddCard(new(entity, "原实体" + kind, CardCatalog.Get(kind).CategoryName, "Real physical entity", kind));
            b.AddDeck(new("fixture:tq-deck", "80 fixed actual entities", scene == "zong-short" ? 1 : 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(entity, new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            var roles = scene == "gift-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(TqMode, "陶谦真实原生边界", 4, 4, roles, "fixture:tq-deck", GeneralCandidateCount: 4, GeneralPoolIds: [TqOwner, .. peers]));
        }
        private static void TqProduction(IContentRegistryBuilder b)
        {
            string Read(string part) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-tao-qian." + part + ".json") ?? throw new InvalidOperationException("Missing actual Tao Qian embedded " + part); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in new[] { TqZongluan, TqZhaohuo, TqWenren }) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
