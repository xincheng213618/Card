namespace CardGame.Core;

// 诗怨 evidence: one scalar per accepted draw option each turn; the per-option
// limit and the 余威 doubling are both derived from the committed ledger, so
// cold recovery replays the accepted commands into the same state.
public sealed record ProgramShiYuanTargetDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, int SourceSeat, int DrawCount, long CardActionId) : IGameEvent;
public sealed record ProgramYuWeiActiveTurnEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber) : IGameEvent;

// 毒逝 evidence: the dead owner's chosen heir for the transferred skill.
public sealed record ProgramDuShiGrantEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TracksShiYuanLedger =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ShiYuanTargetDraw) == true;

    // 诗怨 draws three, two or one cards by the targeting character's health
    // compared with the owner's; equal health draws two.
    private int ShiYuanDrawCountFor(int ownerHp, int sourceHp) =>
        sourceHp > ownerHp ? 3 : sourceHp == ownerHp ? 2 : 1;

    // 余威 is active while its turn-start evidence exists for the running turn:
    // the trigger only commits on other living Qun-faction characters' turns.
    private bool IsYuWeiLimitDoubled(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramYuWeiActiveTurnEvent>()
            .Any(e => e.OwnerSeat == ownerSeat && e.TurnNumber == _turnNumber);

    private int ShiYuanTurnLimit(int ownerSeat) => IsYuWeiLimitDoubled(ownerSeat) ? 2 : 1;

    private int ShiYuanOptionUsesThisTurn(int ownerSeat, int drawCount) =>
        CompleteProgramEventHistory().OfType<ProgramShiYuanTargetDrawEvent>()
            .Count(e => e.OwnerSeat == ownerSeat && e.TurnNumber == _turnNumber && e.DrawCount == drawCount);

    private bool ShiYuanDrawAvailable(int ownerSeat, int sourceHp) =>
        ShiYuanOptionUsesThisTurn(ownerSeat, ShiYuanDrawCountFor(_players[ownerSeat].Hp, sourceHp)) <
        ShiYuanTurnLimit(ownerSeat);

    // The optional prompt only appears while the matching option still has a
    // remaining use this turn; an exhausted option never reaches the chooser.
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

    // 毒逝: the chosen living character acquires 毒逝 with both of its clauses.
    private SkillProgramStepOutcome DuShiProgramGrantSkill(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var recipient] || !IsValidPlayerSeat(recipient) ||
            recipient == active.OwnerSeat || !_players[recipient].IsAlive || _winner != Winner.None)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "毒逝的继承角色已失效，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var recipientState = _players[recipient];
        AcquireRuntimeSkills(recipientState, active.SkillId, [active.SkillId]);
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】由 {recipientState.Name} 获得。",
            active.OwnerSeat, recipient);
        AdvanceEventRulesAndQueueFact(new ProgramDuShiGrantEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, recipient));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    // 余威: commit the per-turn evidence consumed by 诗怨's limit query.
    private SkillProgramStepOutcome YuWeiProgramMarkActiveTurn(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】生效：其他群势力角色的回合内，诗怨改为每回合每项限两次。",
            owner.Seat);
        AdvanceEventRulesAndQueueFact(new ProgramYuWeiActiveTurnEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, _turnNumber));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    private sealed partial class ProgramSkillHost : ILiuBianProgramHost
    {
        public SkillProgramStepOutcome ShiYuanTargetDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ShiYuanProgramTargetDraw(frame, effect);
        public SkillProgramStepOutcome DuShiGrantSkill(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.DuShiProgramGrantSkill(frame, effect);
        public SkillProgramStepOutcome YuWeiMarkActiveTurn(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YuWeiProgramMarkActiveTurn(frame, effect);
    }
}
