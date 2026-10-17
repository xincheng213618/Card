namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 27900)]
    private void ChangeNianFormForRound(RoundStartedEvent started)
    {
        if (_modeDefinition.NianActivity is not { } nian || !_setupComplete || started.RoundNumber < 2 ||
            !_players[BossActivitySeat].IsAlive) return;
        var position = (started.RoundNumber - 2) % 4;
        var cycleNumber = (started.RoundNumber - 2) / 4 + 1;
        if (position == 0)
        {
            var forms = nian.FormGeneralIds.Skip(1).ToList();
            _random.Shuffle(forms);
            AdvanceEventRulesAndQueueFact(new NianFormCycleStartedEvent(cycleNumber, forms.ToArray()));
        }
        var cycle = CompleteProgramEventHistory().OfType<NianFormCycleStartedEvent>()
            .Single(entry => entry.CycleNumber == cycleNumber);
        var boss = _players[BossActivitySeat];
        var previous = boss.General.Id;
        boss.General = _generalPool.Single(general => general.Id == cycle.GeneralIds[position]);
        _skillBindingIndex.Invalidate(boss.Seat);
        foreach (var id in EnabledContentSkillIds(boss)) RegisterTaggedConversionSkill(boss, id);
        AdvanceEventRulesAndQueueFact(new NianFormChangedEvent(started.RoundNumber, boss.Seat,
            previous, boss.General.Id, cycleNumber, position));
        AddLog("NianFormChanged", $"第 {started.RoundNumber} 轮，年兽变为{boss.General.Name}，保留当前体力和区域状态。", boss.Seat);
    }
}
