namespace CardGame.Core;

/// <summary>
/// Projects compiled skill programs onto the legacy passive-rule questions.
/// State mutation and active program execution remain owned by GameEngine.
/// </summary>
public sealed class SkillProgramRules : IPassiveSkill
{
    private readonly IReadOnlyList<SkillProgram> _programs;
    private readonly IReadOnlySet<int> _handCardIds;

    public SkillProgramRules(IReadOnlyList<SkillProgram> programs, IReadOnlySet<int> handCardIds)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(handCardIds);
        if (programs.Any(program => program is null))
            throw new ArgumentException("Configured skill programs cannot contain null entries.", nameof(programs));
        if (programs.Select(program => program.Id).Distinct(StringComparer.Ordinal).Count() != programs.Count)
            throw new ArgumentException("Configured skill program ids must be unique.", nameof(programs));

        _programs = Array.AsReadOnly(programs.OrderBy(program => program.Id, StringComparer.Ordinal).ToArray());
        _handCardIds = new HashSet<int>(handCardIds);
    }

    public SkillKind Kind => SkillKind.None;

    public string Name => "Configured skills";

    public int ModifyDrawCount(PlayerSkillContext owner, int currentCount) =>
        Modify(SkillRuleQuery.DrawCount, owner, currentCount, _programs);

    public int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) =>
        Modify(SkillRuleQuery.SlashLimit, owner, currentLimit, _programs);

    public int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) =>
        Modify(SkillRuleQuery.OutgoingDistance, owner, currentDistance, _programs);

    public int ModifyIncomingDistance(PlayerSkillContext owner, int currentDistance) =>
        Modify(SkillRuleQuery.IncomingDistance, owner, currentDistance, _programs);

    public bool CanUseAsSlash(PlayerSkillContext owner, Card card) =>
        MatchesViewAs(owner, card, CardKind.Slash, forResponse: false);

    public bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) =>
        requiredCardKind is CardKind.Slash or CardKind.Dodge &&
        MatchesViewAs(owner, card, requiredCardKind, forResponse: true);

    public static int Modify(
        SkillRuleQuery query,
        PlayerSkillContext context,
        int baseValue,
        IReadOnlyList<SkillProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(programs);

        var modifiers = programs
            .OrderBy(program => program.Id, StringComparer.Ordinal)
            .SelectMany(program => program.Modifiers.Select((modifier, index) => new { modifier, index }))
            .Where(item => item.modifier.Query == query && item.modifier.Condition.Evaluate(context))
            .ToArray();

        if (modifiers.Any(item => item.modifier.Operation == SkillRuleOperation.Unlimited))
            return int.MaxValue;

        var value = (long)baseValue;
        foreach (var item in modifiers.Where(item => item.modifier.Operation == SkillRuleOperation.Set))
            value = item.modifier.Value;
        foreach (var item in modifiers.Where(item => item.modifier.Operation == SkillRuleOperation.Add))
            value = Math.Clamp(value + item.modifier.Value, int.MinValue, int.MaxValue);
        return query == SkillRuleQuery.DrawCount
            ? (int)Math.Max(0, value)
            : (int)value;
    }

    private bool MatchesViewAs(PlayerSkillContext owner, Card card, CardKind outputKind, bool forResponse)
    {
        if (!_handCardIds.Contains(card.Id) || card.Kind == outputKind)
            return false;

        return _programs.SelectMany(program => program.ViewAs).Any(rule =>
            rule.OutputKind == outputKind &&
            (forResponse ? rule.ForResponse : rule.ForPlay) &&
            rule.Condition.Evaluate(owner) &&
            (rule.InputKinds.Count == 0 || rule.InputKinds.Contains(card.Kind)) &&
            (rule.InputSuits.Count == 0 || rule.InputSuits.Contains(card.Suit)));
    }
}
