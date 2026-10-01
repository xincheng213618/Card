namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string NamedUseLedgerId(SkillProgramViewAs rule) =>
        $"view-as-name:{rule.NameLedgerId}:{ProgramBasicCardName(rule.OutputKind)}";

    private bool IsNamedUseConversionAvailable(CharacterState owner,
        IndexedSkillProgramInstance instance, SkillProgramViewAs rule) =>
        (!rule.NoDying || ActiveDying is null && !_players.Any(player => player.IsAlive && player.Hp <= 0)) &&
        (!rule.UnusedOutputNameThisGame || _skillRuntimeState.GetUsage(owner.Seat, instance.SkillId,
            NamedUseLedgerId(rule), SkillUsageScope.Game) == 0);

    private void ConsumeNamedUseConversion(CardConversionSource source)
    {
        if (ViewAsRule(source) is not { UnusedOutputNameThisGame: true } rule) return;
        if (!_skillRuntimeState.TryConsumeUsage(source.OwnerSeat, source.SkillId,
                NamedUseLedgerId(rule), SkillUsageScope.Game, 1))
            throw new InvalidOperationException("The converted output name was already used this game.");
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(source.OwnerSeat, source.SkillId,
            NamedUseLedgerId(rule), SkillUsageScope.Game, 1));
    }

    private void ResetTurnProgramBooleanStates()
    {
        foreach (var key in _programBooleanStates.Keys.ToArray())
        {
            var definition = GetProgramBooleanStateDefinition(key.SkillId, key.StateId);
            if (definition.ResetScope != SkillProgramStateResetScope.Turn ||
                _programBooleanStates[key] == definition.InitialValue) continue;
            _programBooleanStates[key] = definition.InitialValue;
            if (definition.Visibility == SkillProgramStateVisibility.Public)
                AdvanceEventRulesAndQueueFact(new ProgramBooleanStateChangedEvent(key.OwnerSeat, key.SkillId,
                    key.SkillInstanceId, key.StateId, definition.InitialValue, definition.Visibility));
        }
    }
}
