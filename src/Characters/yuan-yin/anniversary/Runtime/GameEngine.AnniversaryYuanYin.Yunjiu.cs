using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool IsAnniversaryYuanYinYunjiu(SkillProgramTrigger trigger) => trigger.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu }];
    private static CardMoveReason YuanYinGiftReason => new("skill-program.anniversary:yunjiu.give");
    private IEnumerable<int> YuanYinRecipients(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner).Select(p => p.Seat);
    private bool YuanYinDeathHasCards(int victim) => GetHand(_players[victim]).Concat(GetEquipment(_players[victim])).Any(c =>
        !IsForeignEquipmentDiscardPrevented(victim, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Transfer));
    private bool TryBeginAnniversaryYuanYinDeathBeforeCleanup(long deathId)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu) || _pendingDecision is not null ||
            _resolutionStack.LastOrDefault() is not DeathFrame death || death.Id != deathId || _players[death.VictimSeat].IsAlive ||
            death.CleanedUpCardIds.Count != 0 || !YuanYinDeathHasCards(death.VictimSeat) ||
            ProgramEventHistory<AnniversaryYuanYinDeathWindowIssuedEvent>().Any(e => e.DeathId == deathId)) return false;
        var issued = _players.Where(p => p.IsAlive && YuanYinRecipients(p.Seat).Any()).SelectMany(p =>
            CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.CharacterDied).Where(c => IsAnniversaryYuanYinYunjiu(GetProgramTrigger(c)))
                .Select(c => (Candidate: c, Facts: CaptureProgramTriggerFacts(p))))
            .OrderBy(x => RelativeTableOrder(_currentSeat, x.Candidate.OwnerSeat)).ThenByDescending(x => x.Candidate.Priority)
            .ThenBy(x => x.Candidate.SkillId, StringComparer.Ordinal).ThenBy(x => x.Candidate.SkillInstanceId, StringComparer.Ordinal).ToArray();
        if (issued.Length == 0) return false;
        var id = ++_resolutionSequence;
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinDeathWindowIssuedEvent(id, death.Id, death.VictimSeat, death.KillerSeat, issued.Length));
        PushRuntimeFrame(new ProgramKillTriggerWindowFrame(id, death.Id, death.VictimSeat, death.KillerSeat,
            issued.Select(x => x.Candidate).ToArray(), issued.Select(x => new ProgramSkillWindowContext(SkillProgramTriggerWindow.CharacterDied, id, x.Candidate.OwnerSeat,
                SourceSeat: death.KillerSeat, TargetSeat: death.VictimSeat, OccurrenceIndex: x.Candidate.OccurrenceIndex, Facts: x.Facts)).ToArray()) { BeforeDeathCleanup = true });
        AdvanceRuntimeTop<ProgramKillTriggerWindowFrame>(); return true;
    }
    private bool YuanYinIssuedDeathWindow(ProgramKillTriggerWindowFrame window, DeathFrame death) => window.BeforeDeathCleanup &&
        window.DeathFrameId == death.Id && window.VictimSeat == death.VictimSeat && window.KillerSeat == death.KillerSeat && !_players[death.VictimSeat].IsAlive &&
        death.CleanedUpCardIds.Count == 0 && window.Candidates.Count > 0 && window.Contexts.Count == window.Candidates.Count &&
        ProgramEventHistory<AnniversaryYuanYinDeathWindowIssuedEvent>().Count(e => e == new AnniversaryYuanYinDeathWindowIssuedEvent(window.Id, death.Id, death.VictimSeat, death.KillerSeat, window.Candidates.Count)) == 1 &&
        !ProgramEventHistory<AnniversaryYuanYinDeathWindowCompletedEvent>().Any(e => e.WindowId == window.Id || e.DeathId == death.Id) &&
        window.Candidates.Select((c, i) => (c, Context: window.Contexts[i])).All(x => IsAnniversaryYuanYinYunjiu(GetProgramTrigger(x.c)) &&
            x.Context.Window == SkillProgramTriggerWindow.CharacterDied && x.Context.ParentFrameId == window.Id && x.Context.OwnerSeat == x.c.OwnerSeat &&
            x.Context.TargetSeat == death.VictimSeat && x.Context.SourceSeat == death.KillerSeat);
    private void CompleteAnniversaryYuanYinDeathBeforeCleanupWindow(ProgramKillTriggerWindowFrame window, DeathFrame death)
    {
        var i = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (_resolutionStack.LastOrDefault() != window || i < 1 || _resolutionStack[i - 1].Id != death.Id ||
            window.CandidateIndex != window.Candidates.Count || !YuanYinIssuedDeathWindow(window, death))
            throw new InvalidOperationException("Yunjiu cannot repeat cleanup or return a forged normal post-cleanup death window.");
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinDeathWindowCompletedEvent(window.Id, death.Id));
    }
    private bool CanOfferYuanYinYunjiu(ProgramTriggerCandidate c, ProgramSkillWindowContext w) =>
        _players[c.OwnerSeat].IsAlive && _players[c.OwnerSeat].HasHpValue && w is { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: { } victim } &&
        IsValidPlayerSeat(victim) && !_players[victim].IsAlive && YuanYinRecipients(c.OwnerSeat).Any() && YuanYinDeathHasCards(victim) &&
        _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is ProgramKillTriggerWindowFrame { BeforeDeathCleanup: true } parent &&
        parent.VictimSeat == victim && parent.KillerSeat == w.SourceSeat && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == c &&
        _resolutionStack.SingleOrDefault(f => f.Id == parent.DeathFrameId) is DeathFrame death && YuanYinIssuedDeathWindow(parent, death);
    private SkillProgramStepOutcome BeginYuanYinYunjiu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryYuanYin is not null || f.InstructionIndex != 1 || f.SkillId != AnniversaryYuanYinComposition.Yunjiu || f.TriggerId != "dead-owned-card-gift" ||
            f.WindowContext is not { } w || !CanOfferYuanYinYunjiu(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w))
            throw new InvalidOperationException("Yunjiu requires the original dead owner's actual HE before native cleanup.");
        var parent = (ProgramKillTriggerWindowFrame)_resolutionStack.Single(p => p.Id == w.ParentFrameId);
        var r = new AnniversaryYuanYinReceipt(YuanYinSource(f), f.GameplayHash, parent.Id, _turnNumber, "yunjiu", "recipient") { DeathId = parent.DeathFrameId, Victim = parent.VictimSeat };
        ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.Mode));
        PublishYuanYin(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> YuanYinChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYuanYin!;
        if (r.Stage == "recipient") return Array.AsReadOnly(YuanYinRecipients(f.OwnerSeat).Select(seat => new PromptChoice(new($"yuan-yin.{f.Id}.recipient.{seat}"),
            "交给" + _players[seat].Name, [], [seat], new Dictionary<string, string> { ["program-action"] = "anniversary-yuan-yin", ["branch"] = "recipient" })).ToArray());
        if (r.Stage != "card" || !_players[r.Recipient].IsAlive) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.Victim, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer)
            .Select(c => c with { Id = new($"yuan-yin.{f.Id}.{c.Id.Value}"), Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "anniversary-yuan-yin", ["branch"] = "card" } }).ToArray());
    }
    private void PublishYuanYin(ProgramSkillFrame f)
    {
        var choices = YuanYinChoices(f);
        if (!_players[f.OwnerSeat].IsAlive || choices.Count == 0) { FinishYuanYin(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.AnniversaryYuanYin!.Stage == "recipient" ? "运柩：选择获得死者牌的其他角色。" : "运柩：交给其一张死者的牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-yuan-yin")] private void AnswerYuanYin(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryYuanYin: { Mode: "yunjiu" } r } f || !ValidYuanYin(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !YuanYinChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Yunjiu rejects a changed native death, another recipient or a stale opaque card slot.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == "recipient") { ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = r with { Recipient = selected.Targets.Single(), Stage = "card" } }); PublishYuanYin(f); return; }
        var from = new CardLocation(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), r.Victim);
        var id = _cardZones.CardsAt(from)[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)].Id;
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = r with { Stage = "gift-children", CardId = id, From = from, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(r.Recipient), YuanYinGiftReason, afterRequestSettled: settlement =>
        {
            ValidateHappyWeiYanGainRequestSettlement(settlement, f.Id, [id], CardLocation.Hand(r.Recipient), before);
            var live = GetActiveProgramFrame(f.Id); var paid = live.AnniversaryYuanYin! with { Gain = settlement, After = _movementSequence, Delivered = settlement.Movements.Any(m => m.CardId == id && m.From == from && m.To == CardLocation.Hand(r.Recipient)) };
            ReplaceRuntimeTop(live with { AnniversaryYuanYin = paid });
            RecordNativeGivenCards(f.Id, r.Victim, r.Recipient, [id], before, settlement);
        }, producerFrameId: f.Id);
        var settled = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(settled = settled with { AnniversaryYuanYin = settled.AnniversaryYuanYin! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinGiftPaidEvent(f.Id, r.DeathId, r.Victim, r.Recipient, settled.AnniversaryYuanYin!.Delivered, before, _movementSequence));
        ContinueYuanYin(f.Id);
    }
    private void ContinueYuanYinYunjiu(ProgramSkillFrame supplied)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryYuanYin: { Mode: "yunjiu" } r } f && f.Id == supplied.Id)
        {
            if (!ValidYuanYin(f)) throw new InvalidOperationException("Yunjiu lost its original pre-cleanup death, real gift or once-issued benefit.");
            if (r.Stage is "recipient" or "card") { PublishYuanYin(f); return; }
            if (DrainYuanYin(f)) return;
            f = GetActiveProgramFrame(f.Id); r = f.AnniversaryYuanYin!;
            if (r.Stage == "recovery-children" || !r.Delivered || !_players[f.OwnerSeat].IsAlive) { FinishYuanYin(f); return; }
            if (r.Stage == "gift-children")
            {
                ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, AnniversaryYuanYin = r with { Stage = "maximum-children" } });
                AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinBenefitIssuedEvent(f.Id, "maximum-hp", f.OwnerSeat, 1));
                ChangeProgramMaximumHp(f, 1); continue;
            }
            if (r.Stage != "maximum-children") throw new InvalidOperationException("Yunjiu cannot issue recovery before its actual native maximum-HP child returns.");
            ReplaceRuntimeTop(f = f with { AnniversaryYuanYin = r with { Stage = "recovery-children" } });
            AdvanceEventRulesAndQueueFact(new AnniversaryYuanYinBenefitIssuedEvent(f.Id, "recovery", f.OwnerSeat, 1));
            new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
        }
    }
    private bool HasAnniversaryYuanYinPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryYuanYin: { Mode: "yunjiu", Stage: "recipient" or "card" }, Step: ResolutionFrameStep.AwaitingResponse } f &&
        ValidYuanYin(f) && p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && AssistedChoicesEqual(p.Choices, YuanYinChoices(f));
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu)] private PromptChoice ChooseYuanYin(PendingDecision p) => p.Choices.First();
}
