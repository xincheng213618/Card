using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeZouShiChecks
{
    private const string Yz = "le:yunzheng", Hx = "le:huoxin", Zd = "fixture:lzs-driver", Zc = "fixture:lzs-conversion";
    private const string Zp = "fixture:lzs-probe", Zl = "fixture:lzs-locked", Zn = "fixture:lzs-neutral", Zw = "fixture:lzs-watch";
    private const string Zq = "fixture:lzs-quiet", Zg = "fixture:lzs-granted", Zo = "fixture:lzs-owner", Zm = "identity:classic-le-zou-shi";
    private const string Zcond = "fixture:lzs-condition", Zaoe = "fixture:lzs-assault", Zforced = "fixture:lzs-forced", Zobtain = "skill-program.le-zou-shi.huoxin.obtain";
    private static bool Marked(CardSnapshot card) => card.SkillMarkLabel?.Contains("筝", StringComparison.Ordinal) == true;

    public static void YunzhengOpeningEntitiesDynamicNonLockedSuppressionAndRestoreCold()
    {
        var s = ZsScenario.Start("opening", opening: true);
        var mother = s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.YunzhengInitialEntities is not null);
        var initial = mother.YunzhengInitialEntities!;
        s.Need(initial is IList { IsReadOnly: true } && initial.Count(e => e.OwnerSeat == 0) == 4 && !s.E<YunzhengInitializedEvent>().Any() &&
            initial.Where(e => e.OwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(s.V().Hand.Select(c => c.Id)),
            "The actual GameStarting mother freezes detached final opening entities before a higher-priority native observer and before any marks are issued.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        var original = s.E<YunzhengInitialEntityMarkedEvent>().Where(e => e.OriginalOwnerSeat == 0).ToArray();
        s.Need(original.Length == 4 && original.All(e => s.G.CardMovements.Count(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId &&
            m.CardKind == e.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.InitialDeal) == 1) &&
            s.V().Hand.All(Marked) && s.G.CreateSnapshot(0).Players is IList { IsReadOnly: true } && s.V().Hand is IList { IsReadOnly: true },
            "Only four genuine opening Hand entities are marked, with their exact initial-deal invoices and frozen player views.");
        s.Use("draw-four"); s.Play(); var later = s.V().Hand.Where(c => !Marked(c)).Select(c => c.Id).ToArray();
        s.Need(later.Length == 4 && s.E<YunzhengInitialEntityMarkedEvent>().Length == 4, "A later real Draw4 creates no new opening marks.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.V().HandCount == 8 && !s.G.CardMovements.Any(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "The true discard phase exempts marked current Hand entities while keeping the ordinary four-card limit for later unmarked cards.");
        var mark = original[0].CardId; s.Use("give", [mark], [1]); s.Play();
        s.PeerLimit(1, 5); s.Need(Marked(s.V(1).Hand.Single(c => c.Id == mark)),
            "A transferred global mark immediately suppresses the other holder's non-locked hand-limit skill, while its locked +2 skill remains.");
        s.Use("grant-peer", targets: [1]); s.Play(); s.PeerLimit(1, 5); s.Need(s.E<SkillsAcquiredEvent>().Count(e => e.PlayerSeat == 1 && e.SourceSkillId == Zq && e.SkillIds.SequenceEqual([Zg])) == 1 && !s.V(1).Skills!.Any(k => k.ContentId == Zg),
            "Acquiring another non-locked skill while holding a marked Hand card cannot bypass the current source-based suppression.");
        s.Use("lose-yun"); s.Play(); s.PeerLimit(1, 12); s.Need(s.V(1).Skills!.Any(k => k.ContentId == Zg) && Marked(s.V(1).Hand.Single(c => c.Id == mark)),
            "Losing the last real Yunzheng source immediately restores both non-locked grants without deleting the physical entity mark.");
        s.Use("grant-yun"); s.Play(); s.PeerLimit(1, 5); s.Need(s.E<YunzhengInitializedEvent>().Length == 1,
            "Regranting Yunzheng restores suppression without reinitializing later cards or duplicating the opening receipt.");
        s.Take(1, mark); s.Play(); s.PeerLimit(1, 12); s.Need(Marked(s.V().Hand.Single(c => c.Id == mark)),
            "The actual marked Hand departure immediately restores the former holder; the original holder's reacquired entity retains its mark."); s.Cold();

        s = ZsScenario.Start("dynamic"); var foreign = s.E<YunzhengInitialEntityMarkedEvent>().First(e => e.OriginalOwnerSeat == 1).CardId;
        s.Need(s.Actions().Any(a => a.ProgramSkillId == Zp), "Before the actual foreign marked gain, the human's non-locked activation is legal.");
        s.Take(1, foreign); s.Play();
        s.Need(!s.Actions().Any(a => a.ProgramSkillId == Zp) && s.Actions().Any(a => a.ProgramSkillId == Zl),
            "The genuine Hand transfer removes non-locked actions immediately but preserves a locked skill's legal action.");
        s.Reject(new UseProgramSkillCommand(0, Zp, "probe", [], [], s.G.Revision, s.P!.PromptId));
        var count = s.V().HandCount; s.Skill(Zl, "probe"); s.Play(); s.Need(s.V().HandCount == count + 1, "A surviving locked activation really executes its native Draw1 while the non-locked action is rejected.");
        s.Use("give", [foreign], [2]); s.Play(); s.Need(s.Actions().Any(a => a.ProgramSkillId == Zp), "Leaving the exact Hand entity restores the real non-locked action in the same actual Play.");
        count = s.V().HandCount; s.Skill(Zp, "probe"); s.Play(); s.Need(s.V().HandCount == count + 1, "The restored action really executes rather than merely reappearing as a stale UI definition.");
        s.Take(2, foreign); s.Play(); s.Need(!s.Actions().Any(a => a.ProgramSkillId == Zp), "Reacquiring the marked entity suppresses the currently held grant again.");
        s.Use("kill-peer", targets: [1]); s.Reach(() => !s.V(1).IsAlive); s.Play();
        s.Need(s.G.State.Winner == Winner.None && s.Actions().Any(a => a.ProgramSkillId == Zp) && Marked(s.V().Hand.Single(c => c.Id == foreign)),
            "The last Yunzheng source's actual nonterminal death restores skills while the permanent original mark remains in another living Hand."); s.Cold();

        s = ZsScenario.Start("two-sources");
        s.Need(s.E<YunzhengInitializedEvent>().Select(e => e.Source.OwnerSeat).Order().SequenceEqual([0, 1]) &&
            s.V().Hand.All(Marked) && s.V(1).Hand.All(Marked) && s.V().Skills!.Any(k => k.ContentId == Yz) && s.V(1).Skills!.Any(k => k.ContentId == Yz),
            "Two genuine locked Yunzheng sources both initialize their own final opening entities without recursively invalidating each other.");
        s.Use("zero", targets: [2]); s.Play(); s.Use("incoming", targets: [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])); s.Play();
        s.Need(!s.E<HuoxinStartedEvent>().Any(), "Both actually issued native Uses remain unable to invoke either non-locked Huoxin while both sources hold opening marks.");
        s.Use("discard-many", s.V().Hand.Select(c => c.Id).ToArray()); s.Play();
        s.Need(s.V().HandCount == 0 && s.V(1).Hand.Any(Marked), "One source's last actual marked Hand entity really leaves while the other source still holds marks.");
        s.Use("zero", targets: [2]); var shown = s.Show(2); s.Play();
        s.Need(s.E<HuoxinStartedEvent>().ToArray() is [{ Source.OwnerSeat: 0 }] && s.E<HuoxinEntityShownEvent>().Single().CardId == shown,
            "The first source's real Huoxin resumes immediately after its last mark leaves; the still-marked other source cannot borrow that restoration."); s.Cold();
    }

    public static void HuoxinPrivateSlotsOriginalMarkAndEffectiveSuitObtainCold()
    {
        foreach (var scene in new[] { "match", "mismatch", "none" })
        {
            var s = ZsScenario.Start(scene); var material = s.Study(); var shown = s.Show(1);
            var start = s.E<HuoxinStartedEvent>().Single(); var fact = s.E<HuoxinEntityShownEvent>().Single();
            s.Need(!fact.WasMarked && fact.CardId == shown && fact.TargetSeat == 1 && fact.MayObtain == (scene == "match") &&
                (scene == "mismatch" ? start.EffectiveSuit == Suit.Spade && fact.EffectiveSuit == Suit.Heart :
                    scene == "none" ? start.EffectiveSuit == Suit.None && fact.EffectiveSuit == Suit.None : start.EffectiveSuit == fact.EffectiveSuit) &&
                s.E<HuoxinEntityMarkedEvent>().Count(e => e.FrameId == start.FrameId && e.CardId == shown) == 1 &&
                s.E<ProgramCardsRevealedEvent>().Any(e => e.FrameId == start.FrameId),
                "The real public show freezes previous membership and both effective suits before adding its new mark; adding a mark cannot make every first show obtainable.");
            if (scene == "match")
            {
                s.Reach(() => s.Branch("obtain")); s.Need(Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).PublicRevealedCards.Any(c => c.Id == shown)),
                    "After its actual public show, all viewers can see the same original entity without exposing the other Hand cards.");
                s.Private(); s.Cold(); s.Answer(c => s.IsBranch(c, "obtain")); s.Play();
                var paid = s.E<HuoxinObtainPaidEvent>().Single();
                s.Need(paid.CardId == shown && paid.From == CardLocation.Hand(1) && paid.To == CardLocation.Hand(0) &&
                    s.G.CardMovements.Count(m => m.Sequence == paid.MovementSequence && m.CardId == shown && m.From == paid.From && m.To == paid.To && m.Reason.Value == Zobtain) == 1 &&
                    Marked(s.V().Hand.Single(c => c.Id == shown)) && s.E<HuoxinCompletedEvent>().Single() is { Marked: true, Obtained: true },
                    "Matching nonempty effective suits obtain precisely the publicly shown original Hand entity once.");
            }
            else
            {
                s.Play(); s.Need(!s.E<HuoxinObtainPaidEvent>().Any() && Marked(s.V(1).Hand.Single(c => c.Id == shown)) &&
                    s.E<HuoxinCompletedEvent>().Single() is { Marked: true, Obtained: false },
                    "A first mismatching or no-suit show only marks its original entity; there is no forged obtain choice or movement.");
                _ = s.Study(); s.Show(1, shown); var second = s.E<HuoxinEntityShownEvent>().Last();
                s.Need(second is { WasMarked: true, MayObtain: true } && second.CardId == shown,
                    "The same real entity's earlier mark, rather than its just-created mark, authorizes a later obtain even when suits differ or are both None.");
                s.Reach(() => s.Branch("obtain")); s.Private(); s.Cold(); s.Answer(c => s.IsBranch(c, "obtain")); s.Play();
                s.Need(s.E<HuoxinObtainPaidEvent>().Single().CardId == shown && s.V().Hand.Any(c => c.Id == shown), "The later old-mark branch really transfers that exact entity once.");
            }
            s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.CardId == material && e.CardKind == CardKind.DrawTwo) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
                "The non-targeted native DrawTwo and its original conversion material remain one accepted true Use."); s.Cold();
        }
    }

    public static void HuoxinTrueResponseForcedAndNoTargetUsesExcludePurePlayedCold()
    {
        foreach (var scene in new[] { "dodge", "null", "rescue", "forced", "zero" })
        {
            var s = ZsScenario.Start(scene); int? payment = null;
            if (scene == "dodge")
            {
                s.Use("incoming", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true);
                s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0]));
                s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); payment = s.Respond(CardKind.Dodge);
            }
            else if (scene == "null") { s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); payment = s.Respond(CardKind.Nullification); }
            else if (scene == "rescue") { s.Use("dying"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); payment = s.Respond(CardKind.Peach); }
            else if (scene == "forced")
            {
                s.Use("draw-one"); s.Play(); s.Use("give", [s.V().Hand.First(c => !Marked(c)).Id], [1]);
                s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "assisted-physical-slash" && c.Parameters.GetValueOrDefault("request-option") == "target") == true);
                s.Answer(c => c.Parameters.GetValueOrDefault("request-option") == "target" && c.Targets.SequenceEqual([1]));
                s.Reach(() => s.P?.Choices.Any(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("request-option") == "use") == true);
                payment = s.Respond(CardKind.Slash);
            }
            else s.Use("zero", targets: [1]);
            var shown = s.Show(2); var start = s.E<HuoxinStartedEvent>().Single();
            var expected = scene == "dodge" ? CardKind.Dodge : scene == "null" ? CardKind.Nullification : scene == "rescue" ? CardKind.Peach : CardKind.Slash;
            s.Need(start.EffectiveKind == expected && (scene == "zero" ? start.EffectiveSuit == Suit.None : start.EffectiveSuit == Suit.Spade),
                "Every positive scene reaches Huoxin from the actual native true-Use actor and real effective kind, including targetless response uses and forced zero-material Slash.");
            if (s.Branch("obtain")) s.Answer(c => s.IsBranch(c, "obtain")); s.Play();
            s.Need(s.E<HuoxinCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.Marked) == 1 &&
                s.E<HuoxinEntityShownEvent>().Count(e => e.FrameId == start.FrameId && e.CardId == shown) == 1 &&
                (payment is null ? s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == start.ActionId && e.Action.PhysicalCards.Count == 0) :
                    s.G.CardMovements.Count(m => m.CardId == payment && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1) &&
                (scene is "dodge" or "null" ? s.E<CardRespondedEvent>().Count(e => e.CardId == payment && e.ResponderSeat == 0 && e.EffectiveCardKind == expected) == 1 &&
                    s.E<CardActionAcceptedEvent>().Count(e => e.Action.ActionId == start.ActionId && e.Action.PhysicalCards.Single().CardId == payment) == 1 &&
                    !s.G.ResolutionStack.Any(f => f.Id == start.UseId) :
                    s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == start.UseId)),
                "Cold return completes exactly one Huoxin child and its real native request without paying the accepted original material twice."); s.Cold();
        }
        foreach (var scene in new[] { "played-duel", "played-barbarian", "played-arrows" })
        {
            var s = ZsScenario.Start(scene);
            if (scene == "played-duel") s.Use("duel", targets: [1]); else s.End();
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge });
            s.Need(s.V().HandCount >= 2 && !s.E<HuoxinStartedEvent>().Any(), "A real native Duel/AOE asks the human to play a response while still holding usable physical Hand materials.");
            var card = s.Respond(scene == "played-arrows" ? CardKind.Dodge : CardKind.Slash); s.Play();
            s.Need(s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.CardId == card) &&
                !s.E<HuoxinStartedEvent>().Any() && !s.E<HuoxinEntityMarkedEvent>().Any(),
                "Pure Played Duel/AOE responses really pay their original card but cannot invoke the true-Use skill."); s.Cold();
        }
        var equipment = ZsScenario.Start("equipment"); equipment.Use("stock"); equipment.Play(); var gear = equipment.V().Hand.First(c => c.Kind == CardKind.SilverLion);
        equipment.Native(equipment.Actions().First(a => a.CardId == gear.Id && a.Kind == LegalActionKind.Equip)); equipment.Play();
        equipment.Need(equipment.V().Equipment.Any(c => c.Id == gear.Id) && !equipment.E<HuoxinStartedEvent>().Any(), "A genuine equipment Use remains outside the complete non-equipment trigger family."); equipment.Cold();
    }

    public static void HuoxinNativeEquipmentPaymentAndPaidSourceChildrenCold()
    {
        foreach (var scene in new[] { "lion", "ox", "obtain-loss", "obtain-death" })
        {
            var s = ZsScenario.Start(scene); s.Use("stock"); s.Play(); int? equipment = null; int? grain = null; int? shownBeforeCost = null;
            if (scene is "lion" or "ox")
            {
                var kind = scene == "lion" ? CardKind.SilverLion : CardKind.WoodenOx;
                equipment = s.V().Hand.First(c => c.Kind == kind).Id;
                s.Native(s.Actions().First(a => a.CardId == equipment && a.Kind == LegalActionKind.Equip)); s.Play();
                if (scene == "lion") { s.Use("wound"); s.Play(); }
                else
                {
                    grain = s.V().Hand.First().Id;
                    s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play();
                }
                s.Native(s.Actions().First(a => a.CardId == equipment && a.ConversionSource?.SkillId == Zc && a.PlayedCardKind == CardKind.DrawTwo));
                s.Reach(() => s.HuoxinOffer() || s.Hold(scene == "lion" ? "lion-recovery" : "ox-grain"));
                if (s.HuoxinOffer())
                {
                    shownBeforeCost = s.Show(1); s.Reach(() => s.Branch("obtain")); s.Private(); s.Cold(); s.Answer(c => s.IsBranch(c, "obtain"));
                    s.Reach(() => s.Hold(scene == "lion" ? "lion-recovery" : "ox-grain"));
                }
                s.Need(s.G.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
                    (scene == "lion" ? s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 1) :
                        s.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile) == 1),
                    "The original equipment conversion waits for the actual Lion recovery or separate Ox-grain spill child, keeping its single primary material.");
                s.Private(); s.Cold(); s.Continue();
            }
            else _ = s.Study();
            var shown = shownBeforeCost ?? s.Show(1); var start = s.E<HuoxinStartedEvent>().Single();
            if (shownBeforeCost is null) { s.Reach(() => s.Branch("obtain")); s.Private(); s.Cold(); s.Answer(c => s.IsBranch(c, "obtain")); }
            if (scene is "obtain-loss" or "obtain-death")
            {
                s.Reach(() => s.Hold("paid-obtain")); var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == start.FrameId);
                s.Need(f.Huoxin is { Stage: HuoxinStage.ObtainChildren, Paid: true } r && r.PaidSequence > 0 && f.PendingMovementContinuation is not null &&
                    s.V().Hand.Any(c => c.Id == shown) && !s.E<HuoxinCompletedEvent>().Any(),
                    "The real shown entity is already paid into Hand while the exact Huoxin parent awaits its genuine gain child.");
                s.Private(); s.Cold(); s.Continue();
                if (scene == "obtain-death") { s.Reach(() => s.Hold("death-entry")); s.Private(); s.Cold(); s.Continue(); }
                s.Reach(() => s.E<HuoxinCompletedEvent>().Any(e => e.FrameId == start.FrameId));
                s.Need(scene == "obtain-death" ? !s.V().IsAlive && s.G.State.Winner == Winner.None :
                    s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == Hx),
                    "The gain child genuinely removes the HP-conditional source or kills its non-Lord owner before returning the already-paid parent.");
            }
            else s.Play();
            s.Need(s.E<HuoxinObtainPaidEvent>().Count(e => e.FrameId == start.FrameId && e.CardId == shown) == 1 &&
                s.E<HuoxinCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.Marked && e.Obtained) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == shown && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == Zobtain) == 1 &&
                (equipment is null || s.G.CardMovements.Count(m => m.CardId == equipment && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1),
                "Native cost and obtain children finish once under their original signed source and parent, without repaying material or obtaining the shown card twice."); s.Cold();
        }
    }

    private sealed class ZsScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool IsBranch(PromptChoice choice, string branch) => choice.Parameters.GetValueOrDefault("program-action") == "le-zou-shi" && choice.Parameters.GetValueOrDefault("branch") == branch;
        public bool Branch(string branch) => P is { PlayerSeat: 0 } p && p.Choices.Any(c => IsBranch(c, branch));
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Zw && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Zd, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Skill(string id, string activation) => Accept(new UseProgramSkillCommand(0, id, activation, [], [], G.Revision, P!.PromptId));
        public void Native(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        public int Study()
        {
            var a = Actions().First(a => a.ConversionSource?.SkillId == Zc && a.PlayedCardKind == CardKind.DrawTwo && a.CardId is not null);
            Native(a); return a.CardId!.Value;
        }
        public void Take(int target, int card)
        {
            var slot = V(target).Hand.ToList().FindIndex(c => c.Id == card); Need(slot >= 0, "The original entity is in its real donor Hand before the request.");
            Use("take", targets: [target]); Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Private(); Cold();
            Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public int Show(int target, int? id = null)
        {
            Reach(() => Branch("target") || P?.SkillPrompt?.SkillId == Hx && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            if (!Branch("target")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(() => Branch("target")); Private(); RejectPrompt(); var retired = P!; Cold(); Answer(c => IsBranch(c, "target") && c.Targets.SequenceEqual([target]));
            Reach(() => Branch("card")); var f = G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.Huoxin is not null); var r = f.Huoxin!;
            Need(r.Stage == HuoxinStage.CardChoice && r.TargetSeat == target && r.Candidates is IList { IsReadOnly: true } &&
                P!.Choices.Where(c => IsBranch(c, "card")).All(c => c.Cards.Count == 0 && c.Targets.Count == 0 && c.Parameters.ContainsKey("slot")) &&
                Enumerable.Range(1, 3).All(viewer => G.CreateSnapshot(viewer).PendingDecision is null) &&
                G.CreateSnapshot(0).Players[target].Hand.Count == 0 && !E<HuoxinEntityShownEvent>().Any(e => e.FrameId == f.Id),
                "Before the actual show, the frozen original candidates live only on the trusted frame; every published choice is an opaque ordinal without a foreign identity or face.");
            var chosen = id ?? r.Candidates[0]; var index = r.Candidates.ToList().IndexOf(chosen); Need(index >= 0, "The requested known fixture entity belongs to this exact frozen foreign Hand cohort.");
            RejectOld(retired); Private(); Cold(); Answer(c => IsBranch(c, "card") && c.Parameters.GetValueOrDefault("slot") == index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Reach(() => E<HuoxinEntityShownEvent>().Any(e => e.FrameId == f.Id)); return chosen;
        }
        public int Respond(CardKind kind)
        {
            var p = P!; var c = p.Choices.First(c => c.Cards.Count == 1 && V().Hand.Any(card => card.Id == c.Cards[0]) &&
                c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die"));
            Need(p.PlayerSeat == 0 && (p.RequiredCardKind is null || p.RequiredCardKind == kind), "The answer pays an actual published original Hand card in the real required-kind native request.");
            var id = c.Cards[0]; Private(); Cold(); Answer(choice => choice.Id == c.Id); return id;
        }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> match)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The human answers a real exact published choice.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public bool HuoxinOffer() => Branch("target") || P?.SkillPrompt?.SkillId == Hx && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void PeerLimit(int seat, int expected)
        {
            Need(G.State.CurrentSeat != seat && V(seat).HandLimit is null, "Foreign non-current HandLimit remains absent from the player view.");
            End(); Reach(() => G.State.CurrentSeat == seat && V(seat).HandLimit is not null);
            Need(V(seat).HandLimit == expected, "The actual current participant's native Hand limit must equal " + expected + ".");
            Cold(); Play();
        }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Fixture ended before the real boundary at " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Zw && p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(viewer => G.CreateSnapshot(viewer).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)),
            "Only the actual chooser sees the private canonical choices; no other snapshot reveals foreign Hand faces.");
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves all prepared private views, original-entity marks, suppression sources, payments and native child cursors."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "An unpublished or suppressed action cannot change any view, payment, event or accepted command."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-zou-shi-choice"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, p.PromptId, p.RequiredCardKind, Choices = p.Choices.Take(5) } : null,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount, V(i).HandLimit }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step, Huoxin = (f as ProgramSkillFrame)?.Huoxin }),
            Started = E<HuoxinStartedEvent>().TakeLast(3), Shown = E<HuoxinEntityShownEvent>().TakeLast(3), Paid = E<HuoxinObtainPaidEvent>().TakeLast(3),
            Completed = E<HuoxinCompletedEvent>().TakeLast(3), Uses = E<CardUseDeclaredEvent>().TakeLast(3), Responses = E<CardRespondedEvent>().TakeLast(3) });
        public static ZsScenario Start(string scene, bool opening = false)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new ZsFixture(scene)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
                HumanRole = scene == "obtain-death" ? Role.Renegade : Role.Lord, ModeId = Zm, UseInteractiveSetup = true,
                ConfirmOpeningHand = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = scene == "opening" ? 32 : 12,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new ZsScenario(scene, g, registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Zo, g.Revision, s.P!.PromptId)); if (opening) s.Reach(() => s.Hold("opening")); else s.Play(); return s;
        }
    }

    private sealed class ZsFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-zou-shi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static void Production(IContentRegistryBuilder b, string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real program bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description)
                { Program = c.Programs[id], ProgramPresentation = c.Presentations[id], Tags = id is Yz or "classic:hongyan" ? SkillTag.Locked : SkillTag.None });
            }
            Production(b, "le-zou-shi", Yz, Hx); if (scene == "mismatch") Production(b, "passive-card-rules", "classic:hongyan");
            b.AddSkill(new(Zn, "中性替代来源", "真实技能替换的普通目标定义"));
            static JsonNode N(string t) => JsonNode.Parse(t)!;
            static JsonObject A(string id, string effects, int min = 0, int max = 0, int targets = 0) => new()
            { ["id"] = id, ["minCards"] = min, ["maxCards"] = max, ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null,
                ["effects"] = N("[" + effects + "]") };
            static string Choose(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var give = A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1, 1); give["sourceZones"] = N("[\"hand\"]");
            var discard = A("discard-many", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":4}", 4, 4); discard["sourceZones"] = N("[\"hand\"]");
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var acts = new JsonArray(A("draw-one", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"), A("draw-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"),
                A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":16}"), give, discard,
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("lose-yun", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + Yz + "\"],\"sourceBind\":\"" + Zn + "\"}"),
                A("grant-yun", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Yz + "\"]}"),
                A("grant-peer", "{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1),
                A("kill-peer", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}", targets: 1), A("wound", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),
                A("dying", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}"),
                A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", targets: 1),
                A("zero", "{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}", targets: 1), duel);
            var defs = new JsonArray(new JsonObject { ["id"] = Zd, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"" + Zp + "\",\"revision\":1,\"activations\":[{\"id\":\"probe\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}],\"modifiers\":[{\"id\":\"nonlocked-limit\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":3,\"priority\":0}]}"),
                N("{\"id\":\"" + Zl + "\",\"revision\":1,\"activations\":[{\"id\":\"probe\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}],\"modifiers\":[{\"id\":\"locked-limit\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":2,\"priority\":0}]}"),
                N("{\"id\":\"" + Zg + "\",\"revision\":1,\"modifiers\":[{\"id\":\"new-nonlocked-limit\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":4,\"priority\":0}]}"),
                N("{\"id\":\"" + Zq + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (scene == "opening") defs.Single(n => n!["id"]!.GetValue<string>() == Zq)!["triggers"]!.AsArray().Add(N("{\"id\":\"quiet-discard\",\"window\":\"discardPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"discard\"]}]}"));
            if (scene == "opening") defs.Single(n => n!["id"]!.GetValue<string>() == Zq)!["triggers"]!.AsArray().Add(N("{\"id\":\"real-new-skill-on-owned-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program." + Zd + ".Draw\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Zg + "\"]}]}"));
            if (scene is not ("opening" or "dynamic" or "two-sources")) defs.Add(N("{\"id\":\"" + Zc + "\",\"revision\":1,\"viewAs\":[{\"id\":\"study\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\",\"equipment\"],\"outputKind\":\"drawTwo\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true}," +
                "{\"id\":\"nullification\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"nullification\",\"forPlay\":false,\"forResponse\":true}," +
                "{\"id\":\"rescue\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"peach\",\"forPlay\":false,\"forResponse\":true,\"extendedUse\":true}]}"));
            if (scene == "obtain-loss") defs.Add(N("{\"id\":\"" + Zcond + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-huoxin\",\"skillId\":\"" + Hx + "\",\"minimumHp\":13}]}"));
            var owner = new List<string> { Zd };
            if (scene != "dynamic") owner.Add(Yz);
            if (scene == "dynamic") { owner.Add(Zp); owner.Add(Zl); }
            else if (scene != "opening") owner.Add(scene == "obtain-loss" ? Zcond : Hx);
            if (scene is not ("opening" or "dynamic" or "two-sources")) { owner.Add(Zc); owner.Add("standard:longdan"); }
            if (scene is "opening" or "lion" or "ox" or "obtain-loss" or "obtain-death")
            {
                var watches = new JsonArray();
                if (scene == "opening") watches.Add(N("{\"id\":\"opening-before-mark\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"priority\":1000,\"effects\":[" + Choose("opening") + "]}"));
                if (scene == "lion") watches.Add(N("{\"id\":\"real-lion-recovery\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choose("lion-recovery") + "]}"));
                if (scene == "ox") watches.Add(N("{\"id\":\"real-ox-grain\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Choose("ox-grain") + "]}"));
                if (scene is "obtain-loss" or "obtain-death") watches.Add(N("{\"id\":\"real-huoxin-obtain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + Zobtain + "\"],\"optional\":false,\"effects\":[" + Choose("paid-obtain") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "obtain-death" ? 20 : 1) + "}]}"));
                if (scene == "obtain-death") watches.Add(N("{\"id\":\"real-paid-dying\",\"window\":\"dyingEntering\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[" + Choose("death-entry") + "]}"));
                defs.Add(new JsonObject { ["id"] = Zw, ["revision"] = 1, ["triggers"] = watches }); owner.Add(Zw);
            }
            if (scene is "played-barbarian" or "played-arrows") defs.Add(N("{\"id\":\"" + Zaoe + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-peer-global-use\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"" + (scene == "played-barbarian" ? "barbarianAssault" : "arrowBarrage") + "\"}]}]}"));
            if (scene == "forced") defs.Add(N("{\"id\":\"" + Zforced + "\",\"revision\":1,\"triggers\":[{\"id\":\"genuine-foreign-request\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"requestSlashAgainstChosenTarget\",\"target\":\"selectedTarget\",\"chooserRef\":{\"kind\":\"selectedTarget\"},\"resultBind\":\"forced-use\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实原实体/动态失效/非装备Use行为",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description)
            { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id is Zd or Zl or Zq or Zw ? SkillTag.Locked : SkillTag.None });
            const string choose = "fixture:lzs-owner-choice";
            b.AddSkill(new(choose, "固定本人", "真实选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(Zo, "乐邹氏真实本人", "supporter", choose, "qun", scene is "obtain-loss" or "obtain-death" ? 12 : 3, owner));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lzs-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:lzs-peer-pick-" + i; b.AddSkill(new(pick, "固定他人", "真实身份候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string> { Zq, Zp, Zl };
                if (i == 1 && scene is "dynamic" or "two-sources") { skills.Add(Yz); if (scene == "two-sources") skills.Add(Hx); }
                if (i == 1 && scene == "mismatch") skills.Add("classic:hongyan");
                if (i == 1 && scene == "forced") skills.Add(Zforced);
                if (i == 1 && scene is "played-barbarian" or "played-arrows") skills.Add(Zaoe);
                b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", pick, "wei", 3, skills));
            }
            var kind = scene is "lion" or "equipment" ? CardKind.SilverLion : scene == "ox" ? CardKind.WoodenOx : CardKind.Dodge;
            const string entity = "fixture:lzs-physical"; var def = CardCatalog.Get(kind); b.AddCard(new(entity, def.DisplayName, def.CategoryName, "真实原牌", kind));
            b.AddDeck(new("fixture:lzs-deck", "固定小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard(entity, scene == "none" ? Suit.None : Suit.Spade, 7)).ToArray() });
            var roles = scene == "obtain-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } :
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Zm, "真实乐邹氏小局", 4, 4, roles, "fixture:lzs-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Zo, .. peers]));
        }
    }
}
