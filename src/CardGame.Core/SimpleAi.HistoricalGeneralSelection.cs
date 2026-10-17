namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    // Recorded policies must keep the scoring that produced their automatic
    // general choices, including its historical ordering of presentation hints.
    private static (double Score, string Reason) ScoreHistoricalGeneralSkill(Role role, GeneralSkillDefinition skill)
    {
        if (skill.ActionForms.HasFlag(SkillActionForm.Active))
            return (32d, $"【{skill.Name}】提供主动行动，实战收益由其发布的 Program 效果摘要评估。");
        if (skill.ViewAsOpportunities is { Count: > 0 } conversions)
        {
            var attack = conversions.Any(rule => rule.ForPlay && rule.OutputKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
            var response = conversions.Any(rule => rule.ForResponse);
            return (30d + (attack && role == Role.Rebel ? 7d : 0d) + (response ? 3d : 0d),
                $"【{skill.Name}】提供公开的牌型转换能力，可扩展可用牌或响应来源。");
        }
        return (skill.SelectionWeights?.GetValueOrDefault(role) ?? 12d, $"【{skill.Name}】{skill.Description}");
    }

    private static GeneralDraftTraits ReadHistoricalGeneralDraftTraits(IReadOnlyList<GeneralSkillDefinition> skills,
        Role role, ContentRegistry registry)
    {
        var enabled = skills.Where(skill => !skill.Tags.HasFlag(SkillTag.Lord) || role == Role.Lord).ToArray();
        var programs = enabled.Select(skill => skill.ContentId is { } id && registry.Skills.TryGetValue(id, out var definition)
            ? definition.Program : null).OfType<SkillProgram>().ToArray();
        var effects = programs.SelectMany(program => program.Activations.SelectMany(activation => activation.Effects)
            .Concat(program.Triggers.SelectMany(trigger => trigger.Effects))).ToArray();
        var policies = programs.SelectMany(program => program.CardPolicies).ToArray();
        var offense = enabled.Count(skill => skill.ViewAsOpportunities?.Any(conversion => conversion.ForPlay &&
            conversion.OutputKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == true);
        offense += programs.Count(program => program.Modifiers.Any(modifier => modifier.Query == SkillRuleQuery.SlashLimit) ||
            program.DamageModifiers.Count > 0 || program.Triggers.Any(trigger => trigger.Window == SkillProgramTriggerWindow.SlashBeforeResponse));
        offense += effects.Any(effect => effect.Op is SkillProgramEffectOp.Damage or SkillProgramEffectOp.StartVirtualDuel &&
            effect.Target != SkillProgramEffectTarget.Owner) ? 1 : 0;
        var defense = policies.Count(policy => policy.Kind is SkillProgramCardPolicyKind.VirtualEquipment or
            SkillProgramCardPolicyKind.FactionResponseRequest or SkillProgramCardPolicyKind.NullifyBlackSlashWithoutArmor or
            SkillProgramCardPolicyKind.PreventIncomingTrickDamage or SkillProgramCardPolicyKind.PreventTrickDamage or
            SkillProgramCardPolicyKind.ProhibitTarget);
        defense += programs.Count(program => program.Triggers.Any(trigger =>
            trigger.Window == SkillProgramTriggerWindow.SlashTargetRedirecting ||
            trigger.Window == SkillProgramTriggerWindow.BeforeDamageApplied && trigger.Effects.Any(effect =>
                effect.Op == SkillProgramEffectOp.PreventCurrentDamage)));
        var recovery = effects.Any(effect => effect.Op is SkillProgramEffectOp.Recover or SkillProgramEffectOp.RecoverTo or
            SkillProgramEffectOp.RecoverToMaximum) ? 1 : 0;
        var sustain = recovery + programs.Count(program => program.Modifiers.Any(modifier => modifier.Query == SkillRuleQuery.DrawCount) ||
            program.Triggers.Any(trigger => trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.Draw &&
                effect.Target == SkillProgramEffectTarget.Owner)));
        var support = effects.Any(effect => effect.Target != SkillProgramEffectTarget.Owner && effect.Op is
            SkillProgramEffectOp.Draw or SkillProgramEffectOp.Recover or SkillProgramEffectOp.RecoverTo or
            SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.GiveBoundCard or SkillProgramEffectOp.DistributeOwnedCards) ? 1 : 0;
        var reach = programs.Any(program => program.Modifiers.Any(modifier => modifier.Query is
            SkillRuleQuery.OutgoingDistance or SkillRuleQuery.SlashDistanceLimit)) ? 1 : 0;
        return new(offense, defense, sustain, support, recovery, reach,
            effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantTurnHandCardProhibition),
            effects.Any(effect => effect.Op == SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard),
            policies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.FactionResponseRequest), 0, programs);
    }
}
