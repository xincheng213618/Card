namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly IReadOnlyList<ProgramBooleanStateSnapshot> EmptyProgramBooleanStateSnapshots =
        Array.AsReadOnly(Array.Empty<ProgramBooleanStateSnapshot>());
    private static readonly IReadOnlyList<DirectedTurnCardPolicy> EmptyDirectedPolicySnapshots =
        Array.AsReadOnly(Array.Empty<DirectedTurnCardPolicy>());
    private static readonly IReadOnlyList<TurnCardActionProhibition> EmptyActionProhibitionSnapshots =
        Array.AsReadOnly(Array.Empty<TurnCardActionProhibition>());

    private bool HasProgramPersistentZone(CharacterState owner, CardZoneKind zone)
    {
        var programs = EnabledSkillPrograms(owner);
        for (var index = 0; index < programs.Count; index++)
        {
            var program = programs[index];
            for (var activationIndex = 0; activationIndex < program.Activations.Count; activationIndex++)
                if (WritesZone(program.Activations[activationIndex].Effects)) return true;
            for (var triggerIndex = 0; triggerIndex < program.Triggers.Count; triggerIndex++)
                if (WritesZone(program.Triggers[triggerIndex].Effects)) return true;
        }
        return false;

        bool WritesZone(IReadOnlyList<SkillProgramEffect> effects)
        {
            for (var index = 0; index < effects.Count; index++)
                if (effects[index].Destination == SkillProgramCardDestination.OwnerPersistentZone &&
                    effects[index].DestinationZone == zone) return true;
            return false;
        }
    }

    private bool IsRuntimeAcquiredSkill(CharacterState player, string skillId) =>
        player.AcquiredSkillIds.Contains(skillId) ||
        player.TurnGrantedSkillIds.Contains(skillId, StringComparer.Ordinal) ||
        player.PhaseGrantedSkillIds.Contains(skillId, StringComparer.Ordinal);

    private SkillRuntimeStateSnapshot CreateProgramAwareSkillStateSnapshot(CharacterState owner, string skillId)
    {
        var snapshot = _skillRuntimeState.CreateSnapshot(owner.Seat, skillId,
            IsRuntimeAcquiredSkill(owner, skillId));
        List<ProgramBooleanStateSnapshot>? states = null;
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
        {
            if (instance.SkillId != skillId) continue;
            foreach (var definition in instance.Program.BooleanStates)
            {
                if (definition.Visibility != SkillProgramStateVisibility.Public) continue;
                var value = GetProgramBooleanState(owner.Seat, skillId, instance.SkillInstanceId, definition.Id);
                var presentation = instance.Definition.ProgramPresentation?.BooleanStates.GetValueOrDefault(definition.Id);
                (states ??= []).Add(new(instance.SkillInstanceId, definition.Id, value,
                    presentation is null ? $"{definition.Id}：{(value ? "是" : "否")}" :
                    value ? presentation.TrueText : presentation.FalseText));
            }
        }
        return snapshot with
        {
            PublicRuleStates = GetHandComparisonPublicRuleStates(owner, skillId),
            BooleanStates = states is null ? EmptyProgramBooleanStateSnapshots : Array.AsReadOnly(states.ToArray()),
            DirectedPolicies = _directedTurnCardPolicies.Count == 0
                ? EmptyDirectedPolicySnapshots : CreateNonemptyDirectedPolicySnapshots(owner, skillId),
            ActionProhibitions = _turnCardUseEffects.ActionProhibitions.Count == 0
                ? EmptyActionProhibitionSnapshots : CreateNonemptyActionProhibitionSnapshots(owner, skillId)
        };
    }

    private IReadOnlyList<DirectedTurnCardPolicy> CreateNonemptyDirectedPolicySnapshots(CharacterState owner, string skillId) =>
        Array.AsReadOnly(_directedTurnCardPolicies.Where(item =>
            item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat &&
            item.Source.OwnerSeat == owner.Seat && item.Source.SkillId == skillId &&
            HasRuntimeSkillInstance(owner, skillId, item.Source.SkillInstanceId)).ToArray());

    private IReadOnlyList<TurnCardActionProhibition> CreateNonemptyActionProhibitionSnapshots(CharacterState owner, string skillId) =>
        Array.AsReadOnly(_turnCardUseEffects.ActionProhibitions.Where(item =>
            item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat &&
            item.Source.OwnerSeat == owner.Seat && item.Source.SkillId == skillId &&
            HasRuntimeSkillInstance(owner, skillId, item.Source.SkillInstanceId)).ToArray());
}
