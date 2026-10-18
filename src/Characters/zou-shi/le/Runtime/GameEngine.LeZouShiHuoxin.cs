using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HuoxinObtainReason = "skill-program.le-zou-shi.huoxin.obtain";
    private static CardConversionSource HuoxinSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool TryGetHuoxinUse(int owner, ProgramSkillWindowContext context, out ProgramCardTriggerWindowFrame window)
    {
        window = null!;
        if (context.Window != SkillProgramTriggerWindow.CardUseCommitted || context.CardUse is not { } use ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            GetCardActionWindow(w) != SkillProgramTriggerWindow.CardUseCommitted ||
            w.Action.ActionId != use.CardActionId || w.Action.ActorSeat != owner || use.ActorSeat != owner ||
            w.Action.EffectiveKind != use.EffectiveKind || GetProgramCardCategory(w.Action.EffectiveKind) == SkillProgramCardCategory.Equipment ||
            w.Action.Type == CardActionType.Response && w.CompletedResponseReturn is not { IsCommitted: true }) return false;
        if (w.Action.Type == CardActionType.Use && w.CompletedResponseReturn is not null) return false;
        window = w; return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28951)]
    private bool CanOfferHuoxin(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse)) return true;
        return candidate.SkillId == "le:huoxin" && candidate.BindingId == "non-equipment-use" &&
            _players[candidate.OwnerSeat].IsAlive && TryGetHuoxinUse(candidate.OwnerSeat, context, out var window) &&
            DesignatedExtraTargetCandidateMatches(candidate, context, window) &&
            _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat && GetHand(p).Count > 0) &&
            !ProgramEventHistory<HuoxinStartedEvent>().Any(e => e.ActionId == window.Action.ActionId && e.Source.OwnerSeat == candidate.OwnerSeat);
    }
    private SkillProgramStepOutcome BeginHuoxin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Huoxin is not null || f.InstructionIndex != 1 || f.TriggerId != "non-equipment-use" || f.SkillId != "le:huoxin" ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !CanOfferHuoxin(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), GetProgramTrigger(f), context) ||
            !TryGetHuoxinUse(f.OwnerSeat, context, out var window))
            throw new InvalidOperationException("Huoxin requires its unique accepted native non-equipment true Use and original actor.");
        var r = new HuoxinReceipt(HuoxinSource(f), f.GameplayHash, window.Id, window.ParentFrameId,
            window.Action.ActionId, window.Action.EffectiveKind, window.Action.EffectiveSuit ?? context.CardUse!.PublicSuit ?? Suit.None)
        { Stage = HuoxinStage.TargetChoice, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { Huoxin = r });
        AdvanceEventRulesAndQueueFact(new HuoxinStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.UseId, r.ActionId, r.EffectiveKind, r.EffectiveSuit));
        PublishHuoxin(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HuoxinChoices(ProgramSkillFrame f)
    {
        var r = f.Huoxin!; var result = new List<PromptChoice>();
        PromptChoice C(string branch, string label, int? target = null, int? slot = null, int? card = null) =>
            new(new($"huoxin.{f.Id}.{r.Stage}.{branch}.{target ?? -1}.{slot ?? -1}"), label,
                card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string>
                { ["program-action"] = "le-zou-shi", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                    ["slot"] = (slot ?? -1).ToString(CultureInfo.InvariantCulture) });
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return [];
        if (r.Stage == HuoxinStage.TargetChoice)
            result.AddRange(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && GetHand(p).Count > 0)
                .Select(p => C("target", $"展示 {p.Name} 的一张手牌", p.Seat)));
        else if (r.Stage == HuoxinStage.CardChoice && _players[r.TargetSeat].IsAlive)
            result.AddRange(r.Candidates.Select((id, index) => (id, index))
                .Where(x => _cardZones.GetLocation(x.id) == CardLocation.Hand(r.TargetSeat))
                .Select(x => C("card", $"展示第 {x.index + 1} 张手牌", slot: x.index)));
        else if (r.Stage == HuoxinStage.ObtainChoice && r.MayObtain && _players[r.TargetSeat].IsAlive &&
            r.CardId is { } shown && _cardZones.GetLocation(shown) == CardLocation.Hand(r.TargetSeat))
        { result.Add(C("obtain", $"获得展示的【{GetAdvancedCard(shown).DisplayName}】", card: shown)); result.Add(C("decline", "不获得此牌")); }
        if (!r.Paid && r.Stage is HuoxinStage.TargetChoice or HuoxinStage.CardChoice) result.Add(C("cancel", "取消惑心"));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishHuoxin(ProgramSkillFrame f)
    {
        var choices = HuoxinChoices(f);
        if (choices.Count == 0) { FinishHuoxin(f); return; }
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, f.Huoxin!.Stage == HuoxinStage.ObtainChoice ? "惑心：是否获得展示的牌？" : "惑心：选择一名其他角色，再展示其一张手牌。", f.Huoxin.TargetSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("le-zou-shi")]
    private void AnswerHuoxin(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Huoxin: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], HuoxinChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Huoxin cannot answer an unpublished target, hidden slot or stale material choice.");
        AssertHuoxin(f); ClearPendingDecision();
        switch (choice.Parameters["branch"])
        {
            case "target":
                var target = choice.Targets.Single();
                ReplaceRuntimeTop(f = f with { Huoxin = r with { Stage = HuoxinStage.CardChoice, TargetSeat = target,
                    Candidates = GetHand(_players[target]).Select(c => c.Id).ToArray() } });
                PublishHuoxin(f); return;
            case "card":
                var slot = int.Parse(choice.Parameters["slot"], CultureInfo.InvariantCulture); var id = r.Candidates[slot];
                var card = GetHand(_players[r.TargetSeat]).Single(c => c.Id == id);
                var suit = EffectiveSuit(_players[r.TargetSeat], card); var marked = HasYunzhengEntityMark(id);
                var may = marked || r.EffectiveSuit != Suit.None && r.EffectiveSuit == suit;
                ReplaceRuntimeTop(f = f with { Huoxin = r with { Stage = HuoxinStage.ObtainChoice, Paid = true,
                    CardId = id, PrintedKind = card.Kind, ShownEffectiveSuit = suit, WasMarked = marked, MayObtain = may } });
                SetProgramCardSet(f.Id, "huoxin-shown", [id], SkillProgramCardSetVisibility.Private, [CardLocation.Hand(r.TargetSeat)]);
                f = GetActiveProgramFrame(f.Id); RevealProgramBoundCards(f, "huoxin-shown"); f = GetActiveProgramFrame(f.Id);
                AdvanceEventRulesAndQueueFact(new HuoxinEntityShownEvent(f.Id, r.TargetSeat, id, card.Kind, suit, marked, may));
                AdvanceEventRulesAndQueueFact(new HuoxinEntityMarkedEvent(f.Id, r.Source, r.GameplayHash, r.TargetSeat, id, card.Kind));
                if (may) PublishHuoxin(GetActiveProgramFrame(f.Id)); else FinishHuoxin(GetActiveProgramFrame(f.Id)); return;
            case "obtain":
                var selected = r.CardId!.Value; var before = _movementSequence;
                ReplaceRuntimeTop(f = f with { Huoxin = r with { Stage = HuoxinStage.ObtainChildren, Before = before, After = before },
                    PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                MoveProgramCardsFromMultipleSources([selected], CardLocation.Hand(f.OwnerSeat), new(HuoxinObtainReason), (batch, moves) =>
                {
                    var m = moves.Single(); var current = GetActiveProgramFrame(f.Id);
                    if (m.CardId != selected || m.CardKind != r.PrintedKind || m.From != CardLocation.Hand(r.TargetSeat) || m.To != CardLocation.Hand(f.OwnerSeat))
                        throw new InvalidOperationException("Huoxin must obtain the same publicly shown Hand entity exactly once.");
                    ReplaceRuntimeTop(current with { Huoxin = current.Huoxin! with { BatchId = batch, After = _movementSequence, PaidSequence = m.Sequence } });
                    AdvanceEventRulesAndQueueFact(new HuoxinObtainPaidEvent(f.Id, batch, selected, m.CardKind, m.From, m.To, m.Sequence, before, _movementSequence));
                });
                ContinueHuoxin(f.Id); return;
            case "decline": case "cancel": FinishHuoxin(f); return;
            default: throw new InvalidOperationException("Huoxin cannot invent a branch.");
        }
    }
    private void ContinueHuoxin(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { Huoxin: { } r } f || f.Id != id) return;
        AssertHuoxin(f);
        if (r.Stage != HuoxinStage.ObtainChildren) { PublishHuoxin(f); return; }
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(f.Id) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id)) return;
        FinishHuoxin(GetActiveProgramFrame(id));
    }
    private void FinishHuoxin(ProgramSkillFrame f)
    {
        var r = f.Huoxin!;
        AdvanceEventRulesAndQueueFact(new HuoxinCompletedEvent(f.Id, r.Paid, r.PaidSequence > 0));
        ReplaceRuntimeTop(f = f with { Huoxin = null, PendingMovementContinuation = null }); FinishProgramSkill(f, r.Paid);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28951)]
    private bool ResumeHuoxin(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Huoxin: not null } f || f.Id != id) return false; ContinueHuoxin(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28951)]
    private bool ReturnHuoxinMovement(ProgramSkillFrame f)
    { if (f.Huoxin is null || f.PendingMovementContinuation is null) return false; ContinueHuoxin(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28951)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28951)]
    private bool PermitPaidHuoxin(ProgramSkillFrame f) => f.Huoxin is { Paid: true } && ValidHuoxin(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHuoxinNonEquipmentUse)]
    private PromptChoice HuoxinAi(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.Huoxin?.Stage == HuoxinStage.TargetChoice)
        {
            var view = CreateSnapshot(f.OwnerSeat);
            return p.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "target")
                .OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets.Single(), new(0, 0, 0, 0, 0, 1, false, false)))
                .ThenBy(c => c.Targets.Single()).FirstOrDefault() ?? p.Choices.Last();
        }
        return p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "obtain") ??
            p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") != "cancel") ?? p.Choices.First();
    }
}
