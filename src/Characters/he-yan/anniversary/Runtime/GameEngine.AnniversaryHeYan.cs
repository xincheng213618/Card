using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HeYanRevealReturn = "anniversary-he-yan-reveal";
    private static CardConversionSource HeYanSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private static CardMoveReason HeYanMoveReason(string kind) => new("skill.anniversary-he-yan." + kind);
    private HeYanHandPlan HeYanFreezeHand(int seat) => new(seat, GetHand(_players[seat]).Select(c => new HeYanHandCard(c.Id, c.Kind, EffectiveSuit(_players[seat], c))).ToArray());
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44200, SkillProgramEffectOp.ResolveAnniversaryHeYanYachai)]
    private bool CanOfferHeYan(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, DamageFrameId: not null, Amount: > 0 } &&
        context.TargetSeat == c.OwnerSeat && IsValidPlayerSeat(source) && _players[source].IsAlive;
    [ProgramCapability] private sealed class HeYanHost(GameEngine engine) : IAnniversaryHeYanHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginHeYan(f, op); }
    private SkillProgramStepOutcome BeginHeYan(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryHeYan is not null || f.InstructionIndex != 1) throw new InvalidOperationException("He Yan cannot restart an issued operation.");
        var yachai = op == SkillProgramEffectOp.ResolveAnniversaryHeYanYachai;
        var source = f.OwnerSeat;
        if (yachai)
        {
            if (f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } actor, DamageFrameId: { } damage, Amount: > 0 } context ||
                context.TargetSeat != f.OwnerSeat || !IsValidPlayerSeat(actor) || !_players[actor].IsAlive ||
                _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId && w.ParentFrameId == damage) is not { } window ||
                !GetDamageTriggerAttack(window).DamageWasApplied)
                throw new InvalidOperationException("Yachai requires the actual sourced damage and its living source, including self damage.");
            source = actor;
        }
        else if (op != SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan || f.SkillId != AnniversaryHeYanComposition.Qingtan || f.TriggerId is not null ||
            f.ActivationId != "simultaneous-suit-hands" || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Qingtan requires its own accepted zero-input Play activation.");
        var plans = yachai ? new[] { HeYanFreezeHand(source) } : _players.Where(p => p.IsAlive && GetHand(p).Count > 0).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).Select(p => HeYanFreezeHand(p.Seat)).ToArray();
        var r = new AnniversaryHeYanReceipt(HeYanSource(f), f.GameplayHash, op, _turnNumber, _currentSeat, f.WindowContext?.ParentFrameId,
            f.WindowContext?.DamageFrameId, source, yachai ? AnniversaryHeYanStage.SourceChoice : AnniversaryHeYanStage.SealedChoice) { Plans = plans };
        ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryHeYanStartedEvent(f.Id, r.Source, r.Hash, op, r.ActualTurn, r.TurnOwner, r.ParentId, r.DamageId, source, plans.Length));
        if (plans.Length == 0) FinishHeYan(f); else PublishHeYan(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private int HeYanChooser(AnniversaryHeYanReceipt r, int owner) => r.Stage is AnniversaryHeYanStage.SourceChoice or AnniversaryHeYanStage.DiscardChoice ? r.SourceSeat :
        r.Stage == AnniversaryHeYanStage.SealedChoice ? r.Plans[r.ChooserIndex].Seat : r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanYachai ? r.SourceSeat : owner;
    private PromptChoice HeYanChoice(ProgramSkillFrame f, string branch, string label, IReadOnlyList<int>? cards = null) =>
        new(new($"anniversary-he-yan.{f.Id}.{f.AnniversaryHeYan!.Stage}.{branch}"), label, cards ?? [], [],
            new Dictionary<string, string> { ["program-action"] = "anniversary-he-yan", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> HeYanDiscardChoices(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.AnniversaryHeYan!.SourceSeat,
        f.AnniversaryHeYan.SourceSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard, canSelect: (_, c) => !f.AnniversaryHeYan.Selected.Contains(c.Id));
    private IReadOnlyList<PromptChoice> HeYanChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryHeYan!;
        if (r.Stage == AnniversaryHeYanStage.SourceChoice)
        {
            var choices = new List<PromptChoice> { HeYanChoice(f, "ban", "本回合不能再使用手牌，然后令何晏摸两张牌") };
            if (GetHand(_players[r.SourceSeat]).Count > 0)
            { choices.Add(HeYanChoice(f, "discard", "弃置一半手牌（向上取整）")); choices.Add(HeYanChoice(f, "reveal", "展示所有手牌并交给何晏其中一种花色的所有牌")); }
            return Array.AsReadOnly(choices.ToArray());
        }
        if (r.Stage == AnniversaryHeYanStage.DiscardChoice) return Array.AsReadOnly(HeYanDiscardChoices(f).Select(c => HeYanChoice(f, "card-" + c.Cards.Single(), c.Label, c.Cards)).ToArray());
        if (r.Stage == AnniversaryHeYanStage.SealedChoice)
            return Array.AsReadOnly(r.Plans[r.ChooserIndex].Cards.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Hand(r.Plans[r.ChooserIndex].Seat))
                .Select(c => HeYanChoice(f, "card-" + c.Id, "选择【" + GetAdvancedCard(c.Id).DisplayName + "】", [c.Id])).ToArray());
        if (r.Stage != AnniversaryHeYanStage.SuitChoice) return [];
        var shown = r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanYachai ? r.Plans.SelectMany(p => p.Cards) : r.Plans.Select(p => p.Cards.Single(c => c.Id == p.SelectedId));
        var result = shown.Select(c => c.Suit).Distinct().Order().Select(s => HeYanChoice(f, "suit-" + (int)s, "选择" + s + "花色的所有展示牌")).ToList();
        if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan) result.Add(HeYanChoice(f, "decline", "不获得牌（不弃置展示牌）"));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishHeYan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryHeYan!; var chooser = HeYanChooser(r, f.OwnerSeat); var choices = HeYanChoices(f);
        if (!_players[chooser].IsAlive || choices.Count == 0) { FinishHeYan(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse, PendingMovementContinuation = null });
        PublishParticipantHandChoice(f, chooser, choices, r.Stage == AnniversaryHeYanStage.SealedChoice ? "清谈：私下选择一张手牌，所有角色选定后同时展示。" : "请选择【" + _contentRegistry.GetSkill(f.SkillId).Name + "】的结算。", chooser);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-he-yan")] private void AnswerHeYan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHeYan: { } r } f ||
            _pendingDecision?.PlayerSeat != HeYanChooser(r, f.OwnerSeat) || !HeYanChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("He Yan rejects a stale or foreign private choice.");
        RequireHeYan(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == AnniversaryHeYanStage.SourceChoice)
        {
            AdvanceEventRulesAndQueueFact(new AnniversaryHeYanBranchEvent(f.Id, r.SourceSeat, branch));
            r = r with { Branch = branch };
            if (branch == "ban")
            {
                AdvanceEventRulesAndQueueFact(new AnniversaryHeYanHandUseBannedEvent(f.Id, r.Source, r.ActualTurn, r.TurnOwner, r.SourceSeat));
                ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.DrawReady, Recipients = [f.OwnerSeat], DrawAmount = 2 } });
                ContinueHeYan(f.Id); return;
            }
            if (branch == "discard")
            {
                var count = Math.Min((GetHand(_players[r.SourceSeat]).Count + 1) / 2, HeYanDiscardChoices(f).Count);
                ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.DiscardChoice, RequiredDiscard = count } });
                if (count == 0) FinishHeYan(f); else PublishHeYan(f); return;
            }
            ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Plans = [HeYanFreezeHand(r.SourceSeat)] } }); RevealHeYan(f); return;
        }
        if (r.Stage == AnniversaryHeYanStage.DiscardChoice)
        {
            r = r with { Selected = r.Selected.Append(choice.Cards.Single()).ToArray() }; ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r });
            if (r.Selected.Count < r.RequiredDiscard) PublishHeYan(f);
            else PayHeYan(f, "yachai-discard", r.SourceSeat, r.Selected, CardLocation.DiscardPile, AnniversaryHeYanStage.Complete, discard: true);
            return;
        }
        if (r.Stage == AnniversaryHeYanStage.SealedChoice)
        {
            var plans = r.Plans.ToArray(); var p = plans[r.ChooserIndex]; plans[r.ChooserIndex] = p with { SelectedId = choice.Cards.Single() };
            AdvanceEventRulesAndQueueFact(new AnniversaryHeYanChoiceCommittedEvent(f.Id, p.Seat));
            ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Plans = plans, ChooserIndex = r.ChooserIndex + 1 } });
            if (r.ChooserIndex + 1 == plans.Length) RevealHeYan(f); else PublishHeYan(f); return;
        }
        if (r.Stage != AnniversaryHeYanStage.SuitChoice) throw new InvalidOperationException("He Yan has no unissued suit decision.");
        if (branch == "decline") { FinishHeYan(f); return; }
        var suit = (Suit)int.Parse(branch.AsSpan(5), CultureInfo.InvariantCulture);
        var y = r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanYachai;
        var matches = y ? r.Plans.SelectMany(p => p.Cards.Where(c => c.Suit == suit).Select(c => (p.Seat, c.Id))) :
            r.Plans.Where(p => p.Cards.Single(c => c.Id == p.SelectedId).Suit == suit).Select(p => (p.Seat, Id: p.SelectedId!.Value));
        var matching = matches.ToArray();
        r = r with { ChosenSuit = suit, Recipients = y ? [] : matching.Select(m => m.Seat).ToArray(), DrawAmount = 1 };
        ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r });
        var ids = matching.Where(m => m.Seat != f.OwnerSeat && _players[m.Seat].IsAlive && _cardZones.GetLocation(m.Id) == CardLocation.Hand(m.Seat)).Select(m => m.Id).ToArray();
        PayHeYan(f, y ? "yachai-give" : "qingtan-obtain", y ? r.SourceSeat : f.OwnerSeat, ids, CardLocation.Hand(f.OwnerSeat),
            y ? AnniversaryHeYanStage.Complete : AnniversaryHeYanStage.DrawReady, discard: false);
    }
    private void RevealHeYan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryHeYan!;
        ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.Revealing } });
        var y = r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanYachai;
        var ids = r.Plans.SelectMany(p => (y ? p.Cards : p.Cards.Where(c => c.Id == p.SelectedId)).Select(c => c.Id)).ToArray();
        SetProgramCardSet(f.Id, "he-yan-shown", ids, SkillProgramCardSetVisibility.Public);
        foreach (var p in r.Plans)
        {
            var shown = (y ? p.Cards : p.Cards.Where(c => c.Id == p.SelectedId)).Select(c => ProjectHandCard(_players[p.Seat], GetAdvancedCard(c.Id))).ToArray();
            AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, GetProgramBindingId(f), p.Seat, "he-yan-shown", Array.AsReadOnly(shown)));
        }
        if (!TryBeginPublicHandRevealWindow(f.Id, HeYanRevealReturn)) ReturnHeYanReveal(GetActiveProgramFrame(f.Id));
    }
    [PublicHandRevealReturn(HeYanRevealReturn)] private void ReturnHeYanReveal(ProgramSkillFrame f)
    {
        RequireHeYan(f);
        if (f.AnniversaryHeYan is not { Stage: AnniversaryHeYanStage.Revealing } r) throw new InvalidOperationException("He Yan lost his complete public reveal return.");
        ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.SuitChoice } }); PublishHeYan(f);
    }
    private void PayHeYan(ProgramSkillFrame supplied, string kind, int executor, IReadOnlyList<int> ids, CardLocation destination, AnniversaryHeYanStage following, bool discard)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.AnniversaryHeYan!; var before = _movementSequence;
        if (ids.Any(id => _cardZones.GetLocation(id).Zone != CardZoneKind.Hand)) throw new InvalidOperationException("He Yan cannot substitute a card that has left its original Hand.");
        var payment = new HeYanPayment(kind, executor, before, before, ids, []);
        ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.MovementChildren, Following = following, Payment = payment }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        void Paid(long _, IReadOnlyList<CardMovementRecord> records)
        {
            var active = GetActiveProgramFrame(f.Id); var paid = payment with { After = _movementSequence, Movements = records };
            ReplaceRuntimeTop(active with { AnniversaryHeYan = active.AnniversaryHeYan! with { Payment = paid } });
            AdvanceEventRulesAndQueueFact(new AnniversaryHeYanPaidEvent(f.Id, kind, executor, before, paid.After, ids.Count));
        }
        if (ids.Count == 0) Paid(0, []);
        else if (discard) MoveProgramLogicalDiscard(f, executor, ids, HeYanMoveReason(kind), Paid);
        else if (kind == "yachai-give") MoveProgramCardsFromMultipleSources(ids, destination, HeYanMoveReason(kind), NativeGiveCompletionRecords(f.Id, executor, f.OwnerSeat, ids, before, Paid), disclosure: CardMovementDisclosure.FaceUp);
        else MoveProgramCardsFromMultipleSources(ids, destination, HeYanMoveReason(kind), Paid, disclosure: CardMovementDisclosure.FaceUp);
        ContinueHeYan(f.Id);
    }
    private bool DrainHeYan(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 44200)] private bool ContinueHeYan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryHeYan: { } r } f || f.Id != id) return false;
        RequireHeYan(f);
        if (r.Stage is AnniversaryHeYanStage.SourceChoice or AnniversaryHeYanStage.DiscardChoice or AnniversaryHeYanStage.SealedChoice or AnniversaryHeYanStage.SuitChoice)
        { if (_pendingDecision is null) PublishHeYan(f); return true; }
        if (r.Stage is AnniversaryHeYanStage.Revealing or AnniversaryHeYanStage.Drawing) return true;
        if (DrainHeYan(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversaryHeYan!;
        if (r.Stage == AnniversaryHeYanStage.MovementChildren)
        { ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r = r with { Stage = r.Following }, PendingMovementContinuation = null }); }
        if (r.Stage == AnniversaryHeYanStage.DrawChildren)
        { ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r = r with { Stage = AnniversaryHeYanStage.DrawReady }, PendingMovementContinuation = null }); }
        if (r.Stage == AnniversaryHeYanStage.DrawReady)
        {
            while (r.DrawIndex < r.Recipients.Count)
            {
                var target = r.Recipients[r.DrawIndex];
                r = r with { DrawIndex = r.DrawIndex + 1, DrawTarget = target };
                if (!_players[target].IsAlive) { ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r }); continue; }
                ReplaceRuntimeTop(f = f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.Drawing } });
                BeginOperationProgramDraw(f, target, r.DrawAmount, HeYanMoveReason("draw")); return true;
            }
            if (r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan)
            {
                var rest = r.Plans.Where(p => p.Cards.Single(c => c.Id == p.SelectedId).Suit != r.ChosenSuit && p.SelectedId is { } card && _cardZones.GetLocation(card) == CardLocation.Hand(p.Seat))
                    .Select(p => p.SelectedId!.Value).Where(card => !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, GetAdvancedCard(card), _cardZones.GetLocation(card), OwnedCardMoveIntent.Discard)).ToArray();
                PayHeYan(f, "qingtan-rest", f.OwnerSeat, rest, CardLocation.DiscardPile, AnniversaryHeYanStage.Complete, true); return true;
            }
            FinishHeYan(f); return true;
        }
        if (r.Stage == AnniversaryHeYanStage.Complete) FinishHeYan(f);
        return true;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveAnniversaryHeYanYachai, SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan)]
    private void ReturnHeYanDraw(ProgramSkillFrame f, NativeDrawOutcome draw)
    {
        if (f.AnniversaryHeYan is not { Stage: AnniversaryHeYanStage.Drawing } r || draw.RecipientSeat != r.DrawTarget || draw.RequestedCount != r.DrawAmount || draw.SequenceAfter != _movementSequence ||
            draw.AttemptFrameId is { } attempt && r.Draws.Any(d => d.AttemptFrameId == attempt)) throw new InvalidOperationException("He Yan lost his once-issued native draw return.");
        ReplaceRuntimeTop(f with { AnniversaryHeYan = r with { Stage = AnniversaryHeYanStage.DrawChildren, Draws = r.Draws.Append(draw).ToArray() }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new AnniversaryHeYanDrawReturnedEvent(f.Id, r.DrawIndex, draw.RecipientSeat, draw.RequestedCount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId));
        ContinueHeYan(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44200)] private bool ReturnHeYanMovement(ProgramSkillFrame f) => f.AnniversaryHeYan is not null && ContinueHeYan(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryHeYanYachai, SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan)]
    private PromptChoice ChooseHeYan(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.AnniversaryHeYan!;
        if (r.Stage == AnniversaryHeYanStage.SourceChoice) return p.Choices.First(c => c.Parameters["branch"] == "ban");
        if (r.Stage == AnniversaryHeYanStage.SuitChoice && r.Operation == SkillProgramEffectOp.ResolveAnniversaryHeYanQingtan)
            return p.Choices.First(c => c.Parameters["branch"] != "decline");
        return p.Choices.OrderBy(c => c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[p.PlayerSeat]) : 0).First();
    }
    private void FinishHeYan(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new AnniversaryHeYanCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { AnniversaryHeYan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44200)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44200)]
    private bool RetainIssuedHeYan(ProgramSkillFrame f) => f.AnniversaryHeYan is not null;
}
