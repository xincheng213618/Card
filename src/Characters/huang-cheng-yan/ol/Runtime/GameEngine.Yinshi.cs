namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 隐世 ①: the first damage each turn that no colored game card caused is
    // prevented. In this ruleset every game card carries a suit, so the
    // expressible scope is "damage without a source game card".
    private SkillProgramStepOutcome YinshiProgramPreventSourcelessDamage(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var attempt = CurrentDamageAttempt;
        if (attempt is null || attempt.TargetSeat != active.OwnerSeat ||
            attempt.IsSourceLess == false && attempt.Card is not null)
            return SkillProgramStepOutcome.Continue;
        if (CompleteProgramEventHistory().OfType<ProgramYinshiDamagePreventedEvent>()
                .Any(fact => fact.OwnerSeat == active.OwnerSeat && fact.TurnNumber == _turnNumber))
            return SkillProgramStepOutcome.Continue;
        if (_resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame parent ||
            parent.Id != frame.WindowContext?.ParentFrameId || parent.Prevented)
            return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramYinshiDamagePreventedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _turnNumber, frame.WindowContext?.Amount ?? 0));
        AddLog("SkillTriggered",
            $"【隐世】防止了 {_players[active.OwnerSeat].Name} 本回合首次受到的无来源伤害。",
            active.OwnerSeat, active.OwnerSeat);
        PreventProgramCurrentDamage(frame);
        return SkillProgramStepOutcome.Continue;
    }

    // 隐世 ②: when any character resolves a 八卦阵 judgment, the owner claims the
    // effective judgment card.
    private SkillProgramStepOutcome YinshiProgramClaimBaguaJudgmentCard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var context = frame.WindowContext;
        var judgment = context?.Judgment;
        var pending = ActiveJudgment;
        var active = GetActiveProgramFrame(frame.Id);
        if (context is null || judgment is null || pending is null ||
            context.Window != SkillProgramTriggerWindow.JudgmentFinalized ||
            pending.Id != judgment.JudgmentFrameId ||
            GetJudgmentCard(pending) is not { } card || card.Id != judgment.CardId ||
            active.PendingMovementContinuation is not null || !_players[active.OwnerSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var from = CardLocation.Judgment(judgment.SubjectSeat);
        if (_cardZones.GetLocation(card.Id) != from) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(active with
        {
            PendingMovementContinuation = new ProgramMovementContinuation(active.OwnerSeat, 0, null)
        });
        MoveCard(card, from, CardLocation.Hand(active.OwnerSeat),
            new CardMoveReason($"skill-program.{active.SkillId}.yinshi-claim"));
        AddLog("CardGained",
            $"{_players[active.OwnerSeat].Name} 获得【八卦阵】生效的判定牌【{card.DisplayName}】。",
            active.OwnerSeat, judgment.SubjectSeat);
        AdvanceEventRulesAndQueueFact(new ProgramYinshiJudgmentClaimedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, judgment.SubjectSeat, card.Id));
        if (!TryBeginCardsMovedProgramWindow())
            ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
