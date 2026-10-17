namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private (double Score, string Reason) ScoreContextualGeneral(GameSnapshot view, Role role,
        GeneralDefinition candidate, ContentRegistry? registry)
    {
        var (score, reason) = ScoreGeneral(role, candidate, registry);
        if (registry is null || view.ModeKind != ContentModeKind.Identity) return (score, reason);
        var traits = ReadGeneralDraftTraits(candidate.Skills, role, registry);
        var details = new List<string>();
        void Add(double value, string why)
        {
            if (value == 0) return;
            score += value;
            details.Add($"{why} {value:+0.#;-0.#} 分");
        }
        var health = Math.Min(candidate.BaseHp, candidate.InitialHp ?? candidate.BaseHp);
        switch (role)
        {
            case Role.Lord:
                Add((health - 3) * 3 + traits.Defense * 6 + traits.Sustain * 3,
                    "主公重视初始生存、回合外防御与续航");
                if (_policyVersion >= AiDecisionPolicy.RuleBasedGeneralSelectionVersion &&
                    health <= 3 && traits.Defense < .5 && traits.Recovery < .75)
                    Add(-8 - (3 - health) * 4, "低体力且缺少独立回合外防御，当主公有集火风险");
                Add(-traits.Attrition * 8, "强制失血或减上限削弱主公的持续生存");
                break;
            case Role.Loyalist:
                Add(traits.Support * 7 + traits.Offense * 2, "忠臣重视支援主公与协助清反");
                break;
            case Role.Rebel:
                Add(traits.Offense * 5, "反贼重视对主公的有效输出");
                break;
            case Role.Renegade:
                Add((health - 3) * 2 + traits.Defense * 6 + traits.Sustain * 4,
                    "内奸重视独立生存、续航与后期能力");
                Add(-traits.Attrition * 6, "强制损耗不利于内奸独立存活");
                break;
        }

        // Only the publicly revealed Lord is matchup evidence. Hidden seats,
        // hands and uncommitted drafts are never consulted, even in diagnostics.
        var lord = view.Players.FirstOrDefault(player => player.IsRoleRevealed &&
            player.Role == Role.Lord && player.IsGeneralPublic);
        if (lord is not null && role != Role.Lord)
        {
            var lordSkills = lord.Skills ?? [];
            var lordTraits = ReadGeneralDraftTraits(lordSkills, Role.Lord, registry);
            if (role == Role.Rebel)
            {
                if (traits.HandLock)
                    Add(lordTraits.Defense == 0 ? 22 : 8,
                        lordTraits.Defense == 0 ? "公开主公缺少回合外防御，封手爆发更容易集火" : "封手爆发仍需考虑主公的回合外防御");
                if (traits.SkillSuppression)
                    Add(Math.Min(12, lordSkills.Count(skill => !skill.Tags.HasFlag(SkillTag.Locked)) * 3) +
                        (lordTraits.FactionResponse ? 12 : 0),
                        lordTraits.FactionResponse ? "压制非锁定技可切断主公的护驾类势力响应" : "压制公开主公的非锁定技能");
                var distance = Math.Min((Seat - lord.Seat + view.Players.Count) % view.Players.Count,
                    (lord.Seat - Seat + view.Players.Count) % view.Players.Count);
                if (distance > 1) Add(traits.Reach * 4, "远位反贼需要稳定接近主公的距离能力");
            }
            else if (role == Role.Loyalist)
            {
                if (lordTraits.Defense == 0) Add(traits.Support * 3, "缺少回合外防御的主公更需要支援");
                foreach (var program in lordTraits.Programs)
                {
                    foreach (var policy in program.CardPolicies.Where(policy =>
                        policy.OwnerRole == Role.Lord && policy.FactionId == candidate.FactionId))
                    {
                        if (policy.Kind == SkillProgramCardPolicyKind.FactionResponseRequest)
                            Add(7 + (candidate.Skills.Any(skill => skill.ViewAsOpportunities?.Any(conversion =>
                                conversion.ForResponse && policy.RequiredCardKinds.Contains(conversion.OutputKind)) == true) ? 6 : 0),
                                "同势力可配合主公的请求响应，响应转化进一步提高供给");
                        if (policy.Kind == SkillProgramCardPolicyKind.RescueRecoveryBonus)
                            Add(6 + traits.Recovery * 3, "同势力救援可配合主公的回复收益");
                    }
                    if (program.Contributions.Any(contribution => contribution.OwnerRole == Role.Lord &&
                        contribution.ProviderFactions.Contains(candidate.FactionId ?? string.Empty)))
                        Add(7, "候选势力可供给主公的贡献技能");
                }
            }
        }
        return (score, details.Count == 0 ? reason : reason + "；" + string.Join("；", details));
    }

    private static bool TryScoreDraftRuleSkill(Role role, GeneralSkillDefinition skill, ContentRegistry? registry,
        out double score, out string reason)
    {
        score = 0;
        reason = string.Empty;
        if (registry is null || skill.ContentId is not { } id || !registry.Skills.TryGetValue(id, out var definition) ||
            definition.Program is not { } program) return false;

        // Compare rule capabilities, not edition labels or sparse historical
        // overrides which accidentally made the older printed skill stronger.
        var replacements = program.Triggers.Where(trigger => trigger.Window == SkillProgramTriggerWindow.JudgmentReplacing)
            .SelectMany(trigger => trigger.Effects.Where(effect => effect.Op == SkillProgramEffectOp.ReplaceJudgment)
                .Select(effect => (Trigger: trigger, Effect: effect))).ToArray();
        if (replacements.Length > 0)
        {
            score = replacements.Max(item => (role == Role.Loyalist ? 19d : role == Role.Rebel ? 15d : 17d) +
                (item.Trigger.Subject == SkillProgramTriggerSubject.Any ? 3 : 0) +
                (item.Effect.Zones.Contains(CardZoneKind.Equipment) ? 3 : 0) +
                (item.Effect.OldCardDestination == SkillProgramOldJudgmentCardDestination.OwnerHand ? 3 : 0));
            reason = $"【{skill.Name}】按实际改判范围评估" +
                (replacements.Any(item => item.Effect.Zones.Contains(CardZoneKind.Equipment))
                    ? "，手牌与装备均可作为改判资源。" : "，需要手牌作为改判资源。");
            return true;
        }

        var feedback = program.Triggers.Where(trigger => trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
            trigger.Subject == SkillProgramTriggerSubject.Owner &&
            trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.SelectSourceCard &&
                effect.CardSource == SkillProgramCardSource.DamageSource) &&
            trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.MoveBoundCards &&
                effect.Destination == SkillProgramCardDestination.OwnerHand)).ToArray();
        if (feedback.Length > 0)
        {
            var perPoint = feedback.Any(trigger => trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint);
            score = perPoint ? 19 : 16;
            reason = $"【{skill.Name}】受伤后获得来源牌，" + (perPoint ? "按每点伤害触发。" : "按每次伤害触发。") +
                "这是受伤收益，不等同于防止伤害。";
            return true;
        }

        var benefit = ReadDraftSkillBenefits(program, registry);
        var active = program.Activations.Count > 0;
        var activationValue = active ? program.Activations.Max(activation =>
            (14d - Math.Min(4, DraftActivationCardCost(activation)) * 1.5) * (activation.UsesPerGame is not null ? .45 : 1)) : 0;
        var conversion = program.ViewAs.Count > 0 || program.CardIdentities.Count > 0;
        score = 12 + Math.Max(activationValue, conversion ? 15 : 0) + Math.Min(20,
            benefit.Draw * 8 + benefit.Recovery * 8 + benefit.Defense * 6 + benefit.Support * 4 +
            benefit.Offense * (role == Role.Rebel ? 7 : 4) + benefit.Control * 3 + benefit.Reach * 2 + benefit.Growth * 8) -
            benefit.PaidHp * (role == Role.Lord ? 6 : 3) - benefit.Attrition * 3;
        var evidence = new List<string>();
        if (active) evidence.Add("按规则中的主动入口、费用与次数评估");
        if (conversion) evidence.Add("牌型转换");
        if (benefit.Draw > 0) evidence.Add("可供自己的补牌或牌质收益");
        if (benefit.Defense > 0) evidence.Add("有条件的防伤或响应能力");
        if (benefit.Recovery > 0) evidence.Add("可供自己的回复");
        if (benefit.Support > 0) evidence.Add("支援其他角色");
        if (benefit.Growth > 0) evidence.Add("条件满足后获得后续技能");
        if (benefit.Offense + benefit.Control > 0) evidence.Add("输出或控制收益");
        if (benefit.PaidHp > 0) evidence.Add("主动收益需要支付体力");
        if (benefit.Attrition > 0) evidence.Add("存在强制体力损耗");
        reason = $"【{skill.Name}】" + (evidence.Count == 0 ? skill.Description : string.Join("，", evidence) + "。");
        return true;
    }

    private sealed record GeneralDraftTraits(double Offense, double Defense, double Sustain,
        double Support, double Recovery, double Reach, bool HandLock, bool SkillSuppression,
        bool FactionResponse, double Attrition, IReadOnlyList<SkillProgram> Programs);

    private GeneralDraftTraits ReadGeneralDraftTraits(IReadOnlyList<GeneralSkillDefinition> skills,
        Role role, ContentRegistry registry)
    {
        if (_policyVersion < AiDecisionPolicy.RuleBasedGeneralSelectionVersion)
            return ReadHistoricalGeneralDraftTraits(skills, role, registry);
        var enabled = skills.Where(skill => !skill.Tags.HasFlag(SkillTag.Lord) || role == Role.Lord).ToArray();
        var programs = enabled.Select(skill => skill.ContentId is { } id && registry.Skills.TryGetValue(id, out var definition)
            ? definition.Program : null).OfType<SkillProgram>().ToArray();
        var effects = programs.SelectMany(program => program.Activations.SelectMany(activation => activation.Effects)
            .Concat(program.Triggers.SelectMany(trigger => trigger.Effects))).ToArray();
        var policies = programs.SelectMany(program => program.CardPolicies).ToArray();
        var benefits = programs.Select(program => ReadDraftSkillBenefits(program, registry)).ToArray();
        return new(Math.Min(3, benefits.Sum(benefit => benefit.Offense)),
            Math.Min(2, benefits.Sum(benefit => benefit.Defense)),
            Math.Min(3, benefits.Sum(benefit => benefit.Draw + benefit.Recovery)),
            Math.Min(2, benefits.Sum(benefit => benefit.Support)),
            Math.Min(2, benefits.Sum(benefit => benefit.Recovery)),
            Math.Min(2, benefits.Sum(benefit => benefit.Reach)),
            effects.Any(effect => effect.Op == SkillProgramEffectOp.GrantTurnHandCardProhibition),
            effects.Any(effect => effect.Op == SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard),
            policies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.FactionResponseRequest),
            benefits.Sum(benefit => benefit.Attrition), programs);
    }
}
