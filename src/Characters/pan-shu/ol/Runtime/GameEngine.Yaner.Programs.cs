namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 燕尔: both pair draws settle first (owner first), then the shared-category
    // follow-up grants the paired skill its boolean modification and recovers the
    // last-hand loser by one.
    private SkillProgramStepOutcome YanerProgramResolvePairBenefit(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var loserSeat = active.WindowContext?.SourceSeat ??
            throw new InvalidOperationException("The yaner pair lost its last-hand loser.");
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive)
            return SkillProgramStepOutcome.Continue;
        DrawProgramCards(active.Id, active.OwnerSeat, 2, null, "yaner-own",
            SkillProgramCardSetVisibility.Private, new($"skill-program.{active.SkillId}.yaner-draw"));
        IReadOnlyList<int> otherDrawn = [];
        if (IsValidPlayerSeat(loserSeat) && loserSeat != active.OwnerSeat && _players[loserSeat].IsAlive)
        {
            DrawProgramCards(active.Id, loserSeat, 2, null, "yaner-other",
                SkillProgramCardSetVisibility.Private, new($"skill-program.{active.SkillId}.yaner-draw"));
            otherDrawn = GetProgramCardSet(GetActiveProgramFrame(active.Id), "yaner-other").CardIds;
        }
        if (otherDrawn.Count != 2)
        {
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var ownDrawn = GetProgramCardSet(GetActiveProgramFrame(active.Id), "yaner-own").CardIds;
        if (ownDrawn.Count != 2)
        {
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (CardIdsCategorySet(ownDrawn).SetEquals(CardIdsCategorySet(otherDrawn)))
        {
            var targetSkillId = effect.SkillIds.Single();
            SetForeignProgramBooleanState(active, targetSkillId, effect.StateId!, value: true);
            AddLog("SkillTriggered",
                $"{owner.Name} 以【燕尔】摸到的牌与 {_players[loserSeat].Name} 摸到的牌类别相同，" +
                $"【{_contentRegistry!.GetSkill(targetSkillId).Name}】直到其下回合开始删去“你的回合内”。",
                active.OwnerSeat, loserSeat);
            ApplyProgramSkillRecovery(active.Id, active.OwnerSeat, loserSeat);
        }
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
