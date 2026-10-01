namespace CardGame.Core;

/// <summary>
/// Operation parameters compiled from the existing rules definition. These are
/// definition data; cursors, payments and match state remain on the frame/host.
/// </summary>
internal abstract record ProgramSkillInstruction;

internal abstract record ProgramAmount;
internal sealed record FixedProgramAmount(int Value) : ProgramAmount;
internal sealed record ExpressionProgramAmount(SkillProgramNumberExpression Expression) : ProgramAmount;
internal sealed record BoundCardCountProgramAmount(string SourceBind) : ProgramAmount;

internal sealed record DrawProgramInstruction(
    SkillProgramEffectTarget Target,
    ProgramAmount Amount,
    string? ResultBind,
    SkillProgramCardSetVisibility Visibility,
    ProgramParticipantReference? TargetReference) : ProgramSkillInstruction;

internal sealed record RecoverProgramInstruction(
    SkillProgramEffectTarget Target,
    ProgramAmount Amount,
    ProgramParticipantReference? TargetReference) : ProgramSkillInstruction;

internal sealed record LoseHpProgramInstruction(int Amount) : ProgramSkillInstruction;

internal sealed record DamageProgramInstruction(
    int Amount,
    ProgramParticipantReference? SourceReference,
    ProgramParticipantReference? TargetReference,
    DamageNature? Nature) : ProgramSkillInstruction;

internal sealed record ChangeMaximumHpProgramInstruction(int Delta) : ProgramSkillInstruction;

internal sealed record GrowMaximumHpAndHpProgramInstruction(
    SkillProgramNumberExpression PopulationExpression) : ProgramSkillInstruction;
