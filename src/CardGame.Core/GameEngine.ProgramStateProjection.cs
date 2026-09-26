namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasProgramPersistentZone(CharacterState owner, CardZoneKind zone) =>
        EnabledSkillPrograms(owner).Any(program =>
            program.Activations.SelectMany(activation => activation.Effects).Any(effect =>
                effect.Destination == SkillProgramCardDestination.OwnerPersistentZone &&
                effect.DestinationZone == zone) ||
            program.Triggers.SelectMany(trigger => trigger.Effects).Any(effect =>
                effect.Destination == SkillProgramCardDestination.OwnerPersistentZone &&
                effect.DestinationZone == zone));

    private bool IsRuntimeAcquiredSkill(CharacterState player, string skillId) =>
        player.AcquiredSkillIds.Contains(skillId) ||
        player.TurnGrantedSkillIds.Contains(skillId, StringComparer.Ordinal);

    private SkillRuntimeStateSnapshot CreateProgramAwareSkillStateSnapshot(CharacterState owner, string skillId)
    {
        var snapshot = _skillRuntimeState.CreateSnapshot(owner.Seat, skillId,
            IsRuntimeAcquiredSkill(owner, skillId));
        var states = new List<ProgramBooleanStateSnapshot>();
        foreach (var instance in GetSkillBindingShard(owner)?.ProgramInstances ?? [])
        {
            if (instance.SkillId != skillId) continue;
            foreach (var definition in instance.Program.BooleanStates)
            {
                if (definition.Visibility != SkillProgramStateVisibility.Public) continue;
                var value = GetProgramBooleanState(owner.Seat, skillId, instance.SkillInstanceId, definition.Id);
                var presentation = instance.Definition.ProgramPresentation?.BooleanStates.GetValueOrDefault(definition.Id);
                states.Add(new(instance.SkillInstanceId, definition.Id, value,
                    presentation is null ? $"{definition.Id}：{(value ? "是" : "否")}" :
                    value ? presentation.TrueText : presentation.FalseText));
            }
        }
        bool IsCurrentSource(CardUseEffectSource source) =>
            source.OwnerSeat == owner.Seat && source.SkillId == skillId &&
            HasRuntimeSkillInstance(owner, skillId, source.SkillInstanceId);
        return snapshot with
        {
            BooleanStates = Array.AsReadOnly(states.ToArray()),
            DirectedPolicies = Array.AsReadOnly(_directedTurnCardPolicies.Where(item =>
                item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat &&
                IsCurrentSource(item.Source)).ToArray()),
            ActionProhibitions = Array.AsReadOnly(_turnCardUseEffects.ActionProhibitions.Where(item =>
                item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat &&
                IsCurrentSource(item.Source)).ToArray())
        };
    }
}
