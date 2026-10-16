namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QingyuanTakeReason = "skill-program.hu-jin-ding.qingyuan.take";
    [ProgramCapability]
    private sealed class HuJinDingCapability(GameEngine engine) : IHuJinDingProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginQingyuan(f, op); }
    private IReadOnlyList<int> QingyuanNewPartnerChoices(int owner, string skill, string instance) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != owner &&
        !QingyuanPartners(owner, skill, instance).Any(mark => mark.PartnerSeat == p.Seat)).Select(p => p.Seat).ToArray());
    private int[] QingyuanTakeHolders(int owner, string skill, string instance) => QingyuanPartners(owner, skill, instance).Where(IsLiveQingyuanPartner)
        .Select(p => p.PartnerSeat).Distinct().Where(s => GetHand(_players[s]).Count > 0).Order().ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26820)]
    private bool CanOfferHuJinDing(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects is not [{ } e] || !HuJinDingComposition.Handles(e.Op)) return true;
        if (e.Op == SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain)
            return QingyuanGainQualification(c, context) is not null && QingyuanTakeHolders(c.OwnerSeat, c.SkillId, c.SkillInstanceId).Length > 0;
        if (QingyuanNewPartnerChoices(c.OwnerSeat, c.SkillId, c.SkillInstanceId).Count == 0) return false;
        var second = e.Op == SkillProgramEffectOp.SelectQingyuanFirstDamagePartner;
        if (QingyuanPartners(c.OwnerSeat, c.SkillId, c.SkillInstanceId).Any(p => p.FirstDamage == second)) return false;
        return second ? context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.TargetSeat == c.OwnerSeat &&
            ProgramEventHistory<QingyuanFirstDamageEvent>().Any(d => d.OwnerSeat == c.OwnerSeat && d.DamageFrameId == context.DamageFrameId && d.Amount == context.Amount) :
            context.Window == SkillProgramTriggerWindow.GameStarting;
    }
    private SkillProgramStepOutcome BeginQingyuan(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Qingyuan is not null || f.InstructionIndex != 1 || f.WindowContext is not { } context || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !CanOfferHuJinDing(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), GetProgramTrigger(f), context))
            throw new InvalidOperationException("Qingyuan requires its exact unissued lifecycle or qualified hand-gain candidate.");
        var receipt = new QingyuanReceipt(HuJinDingSource(f), f.GameplayHash, op, f.InstructionIndex, context.ParentFrameId, _turnNumber, QingyuanStage.ChoosingPartner,
            Damage: op == SkillProgramEffectOp.SelectQingyuanFirstDamagePartner ? ProgramEventHistory<QingyuanFirstDamageEvent>().Single(d => d.OwnerSeat == f.OwnerSeat) : null);
        if (op == SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain)
            receipt = receipt with { BatchId = context.MovementBatch!.Id, MovementIndex = context.MovementIndex!.Value };
        ReplaceRuntimeTop(f = f with { Qingyuan = receipt });
        AdvanceEventRulesAndQueueFact(new QingyuanStartedEvent(f.Id, receipt.Source, receipt.GameplayHash, op, receipt.ParentId, receipt.ActualTurn, receipt.BatchId, receipt.MovementIndex));
        if (op == SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain) { PayQingyuanTake(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishQingyuanPartner(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> QingyuanPartnerChoices(ProgramSkillFrame f) => Array.AsReadOnly(QingyuanNewPartnerChoices(f.OwnerSeat, f.SkillId, f.SkillInstanceId)
        .Select(s => new PromptChoice(new($"qingyuan.{f.Id}.partner.{s}"), $"选择{_players[s].Name}为轻缘角色", [], [s],
            new Dictionary<string, string> { ["program-action"] = "qingyuan-partner" })).ToArray());
    private void PublishQingyuanPartner(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = QingyuanPartnerChoices(f);
        if (choices.Count == 0) { FinishQingyuan(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.Qingyuan!.Damage is null ? "轻缘：选择一名其他角色。" : "轻缘：首次受到真实伤害后，选择另一名未被选择的其他角色。",
            [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("qingyuan-partner")]
    private void AnswerQingyuanPartner(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Qingyuan: { Stage: QingyuanStage.ChoosingPartner } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !QingyuanPartnerChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Qingyuan lost its exact mandatory public partner choice.");
        AssertHuJinDing(f); ClearPendingDecision(); var partner = choice.Targets.Single();
        AdvanceEventRulesAndQueueFact(new QingyuanPartnerSelectedEvent(f.Id, r.Source, r.GameplayHash, partner, r.Damage is not null, r.ParentId,
            r.Damage?.DamageFrameId, r.Damage?.Amount ?? 0, _movementSequence));
        SynchronizeQingyuanPublicMarkers(); FinishQingyuan(f);
    }
    private void PayQingyuanTake(ProgramSkillFrame f)
    {
        var r = f.Qingyuan!; var candidates = QingyuanTakeHolders(f.OwnerSeat, f.SkillId, f.SkillInstanceId);
        if (candidates.Length == 0) throw new InvalidOperationException("A Qingyuan take lost all actual nonempty marked hands before its random payment.");
        var holderIndex = _random.Next(candidates.Length); var holder = candidates[holderIndex]; var hand = GetHand(_players[holder]).ToArray();
        var cardIndex = _random.Next(hand.Length); var card = hand[cardIndex]; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Qingyuan = r with { Stage = QingyuanStage.TakeChildren, HolderCandidates = candidates, HandCandidates = hand.Select(c => c.Id).ToArray(),
            HolderRandomIndex = holderIndex, CardRandomIndex = cardIndex, HolderSeat = holder, CardId = card.Id, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, CardLocation.Hand(holder), CardLocation.Hand(f.OwnerSeat), new(QingyuanTakeReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { Qingyuan = f.Qingyuan! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new QingyuanTakePaidEvent(f.Id, holder, card.Id, before, _movementSequence, r.ActualTurn));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool DrainHuJinDingChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26820)]
    private bool ResumeQingyuan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Qingyuan: { } r } f || f.Id != id) return false;
        AssertHuJinDing(f);
        if (r.Stage == QingyuanStage.ChoosingPartner) { if (_pendingDecision is null) PublishQingyuanPartner(f); return true; }
        if (!DrainHuJinDingChildren(id)) FinishQingyuan(f); return true;
    }
    private void FinishQingyuan(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new QingyuanCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { Qingyuan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26820)]
    private bool ReturnHuJinDingMovement(ProgramSkillFrame f) => f.Qingyuan is not null && ResumeQingyuan(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.SelectQingyuanOpeningPartner, SkillProgramEffectOp.SelectQingyuanFirstDamagePartner)]
    private PromptChoice AiQingyuan(PendingDecision prompt, ProgramSkillFrame f) => prompt.Choices.OrderByDescending(c => GetHand(_players[c.Targets[0]]).Count).First();
}
