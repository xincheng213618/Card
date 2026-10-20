namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 图兴: the locked companion resolves inside the abolish flow. Clause one
    // fires on every abolished slot; clause two fires once, when the last slot
    // goes (minus four maximum HP, then the game-long damage bonus arms).
    private void ResolveTuxingAfterSlotAbolish(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!EnabledContentSkillIds(owner).Contains("ol:tuxing", StringComparer.Ordinal))
            return;
        var tuxingPresentation = _contentRegistry!.GetSkill("ol:tuxing");
        ChangeProgramMaximumHp(GetActiveProgramFrame(frame.Id), 1, "ol:tuxing");
        AddLog("SkillTriggered",
            $"{owner.Name} 的【{tuxingPresentation.Name}】生效：增加1点体力上限并回复1点体力。",
            active.OwnerSeat);
        new ProgramSkillHost(this).Recover(active.Id, active.OwnerSeat, active.OwnerSeat, 1, null, null);
        if (!IsTuxingDamageArmPending(active.OwnerSeat))
            return;
        AddLog("SkillTriggered",
            $"{owner.Name} 的【{tuxingPresentation.Name}】生效：所有装备栏均已废除，减少4点体力上限，本局游戏接下来造成的伤害+1。",
            active.OwnerSeat);
        ChangeProgramMaximumHp(GetActiveProgramFrame(frame.Id), -4, "ol:tuxing");
        ArmProgramGameDamageBonus(GetActiveProgramFrame(frame.Id), "ol:tuxing", active.OwnerSeat, 1);
    }

    // 图兴 catch-up: the locked turn-start sweep arms the game-long damage
    // bonus when every slot stands abolished and the bonus has not armed yet;
    // the abolish-time resolution already armed it in the ordinary flow, so
    // this only covers slots abolished outside that flow.
    private SkillProgramStepOutcome TuxingProgramArmGameDamage(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            !IsTuxingDamageArmPending(active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        AddLog("SkillTriggered",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】生效：所有装备栏均已废除，本局游戏接下来造成的伤害+1。",
            active.OwnerSeat);
        ArmProgramGameDamageBonus(active, active.SkillId, active.OwnerSeat, 1);
        return SkillProgramStepOutcome.Continue;
    }

    private bool IsTuxingDamageArmPending(int ownerSeat)
    {
        var owner = _players[ownerSeat];
        if (!YujueSlotOrder.All(slot => owner.EquipmentSlotCapacity(slot) == 0))
            return false;
        return !CompleteProgramEventHistory().OfType<ProgramGameDamageBonusArmedEvent>()
            .Any(fact => fact.OwnerSeat == ownerSeat);
    }
}
