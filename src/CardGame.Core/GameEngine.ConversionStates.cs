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
        var program = definition.Program;
        var optIn = program is not null && (program.Activations.Any(binding => ProgramInstructionResolver.Default.Features(binding).UsesConversionPolarity) ||
            program.Triggers.Any(binding => ProgramInstructionResolver.Default.Features(binding).UsesConversionPolarity));
        if (optIn || program is null && (definition.Tags & SkillTag.Conversion) != 0)
            _skillRuntimeState.RegisterConversionSkill(player.Seat, skillId, SkillPolarity.Yang);
    }
}
