namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void InitializeStructuredConversionSkills()
    {
        foreach (var player in _players)
            foreach (var skillId in EnabledContentSkillIds(player))
                RegisterTaggedConversionSkill(player, skillId);
    }

    private void RegisterTaggedConversionSkill(CharacterState player, string skillId)
    {
        var definition = _contentRegistry.GetSkill(skillId);
        // Programs own their declared per-instance states. Only unmigrated modules
        // use the older generic polarity store.
        if (definition.Program is null && (definition.Tags & SkillTag.Conversion) != 0)
            _skillRuntimeState.RegisterConversionSkill(player.Seat, skillId, SkillPolarity.Yang);
    }
}
