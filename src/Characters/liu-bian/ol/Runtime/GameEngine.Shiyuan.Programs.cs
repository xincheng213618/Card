namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksShiYuanLedger =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ShiYuanTargetDraw) == true;

    // 诗怨 draws three, two or one cards by the targeting character's health
    // compared with the owner's; equal health draws two.
    private int ShiYuanDrawCountFor(int ownerHp, int sourceHp) =>
        sourceHp > ownerHp ? 3 : sourceHp == ownerHp ? 2 : 1;

    private int ShiYuanTurnLimit(int ownerSeat) => IsYuWeiLimitDoubled(ownerSeat) ? 2 : 1;

    private int ShiYuanOptionUsesThisTurn(int ownerSeat, int drawCount) =>
        CompleteProgramEventHistory().OfType<ProgramShiYuanTargetDrawEvent>()
            .Count(e => e.OwnerSeat == ownerSeat && e.TurnNumber == _turnNumber && e.DrawCount == drawCount);

    private bool ShiYuanDrawAvailable(int ownerSeat, int sourceHp) =>
        ShiYuanOptionUsesThisTurn(ownerSeat, ShiYuanDrawCountFor(_players[ownerSeat].Hp, sourceHp)) <
        ShiYuanTurnLimit(ownerSeat);

    // The optional prompt only appears while the matching option still has a
    // remaining use this turn; an exhausted option never reaches the chooser.
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 130)]
    private bool CanRunShiYuanTargetDraw(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ShiYuanTargetDraw)) return true;
        if (!TracksShiYuanLedger || _winner != Winner.None || context.SourceSeat is not { } sourceSeat ||
            sourceSeat == candidate.OwnerSeat || !_players[sourceSeat].IsAlive ||
            !IsValidPlayerSeat(candidate.OwnerSeat) || _players[candidate.OwnerSeat].Hp <= 0) return false;
        return ShiYuanDrawAvailable(candidate.OwnerSeat, _players[sourceSeat].Hp);
    }

    // 诗怨 draw: the window prompt is the optional choice; the option limit was
    // checked at candidate time and is re-verified here before the draw.
    private SkillProgramStepOutcome ShiYuanProgramTargetDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None ||
            frame.WindowContext?.SourceSeat is not { } sourceSeat ||
            sourceSeat == owner.Seat || !_players[sourceSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        var drawCount = ShiYuanDrawCountFor(owner.Hp, _players[sourceSeat].Hp);
        if (ShiYuanOptionUsesThisTurn(owner.Seat, drawCount) >= ShiYuanTurnLimit(owner.Seat))
        {
            AddLog("SkillEffect", $"{owner.Name} 诗怨：本回合摸{drawCount}张牌的机会已用完。", owner.Seat);
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.Continue;
        }
        var actionId = frame.WindowContext?.CardUse?.CardActionId ?? 0;
        var drawn = DrawCards(owner, drawCount, true, new($"skill-program.{active.SkillId}.target-draw"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，" +
            $"因成为 {_players[sourceSeat].Name} 使用牌的目标（体力值{(drawCount == 3 ? "大于" : drawCount == 2 ? "等于" : "小于")}你），摸 {drawn.Count} 张牌。",
            owner.Seat, sourceSeat);
        AdvanceEventRulesAndQueueFact(new ProgramShiYuanTargetDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, _turnNumber, sourceSeat, drawCount, actionId));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
