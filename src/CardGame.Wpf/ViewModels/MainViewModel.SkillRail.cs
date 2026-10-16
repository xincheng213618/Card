using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<HumanSkillViewModel> BuildHumanSkillCards() =>
        BuildOwnedHumanSkillCards().Select(skill => skill with { ProgressText = SkillActivationProgressText(skill.ContentId) })
            .Concat(BuildForeignHumanSkillCards()).ToArray();

    private string SkillActivationProgressText(string? skillId)
    {
        var states = _snapshot?.Players.Single(player => player.IsHuman).SkillRuntimeStates?
            .SingleOrDefault(state => state.SkillId == skillId)?.PublicRuleStates ?? [];
        var progress = states.FirstOrDefault(state => state.Kind == ProgramPublicRuleStateKind.GameActivationProgress) ??
            states.FirstOrDefault(state => state.Kind == ProgramPublicRuleStateKind.ActivationLimit);
        if (progress is not null) return $"{progress.Used ?? 0}/{progress.Value}";
        var handLimit = states.Where(state => state.Kind == ProgramPublicRuleStateKind.PersistentHandLimit).Sum(state => state.Value);
        return handLimit == 0 ? string.Empty : $"{handLimit:+0;-0}";
    }

    private IEnumerable<HumanSkillViewModel> BuildForeignHumanSkillCards()
    {
        if (_snapshot?.Players.SingleOrDefault(player => player.IsHuman) is not { IsAlive: true } human)
            yield break;

        var actions = HumanActiveSkillActions;
        var faction = human.FactionId ?? _contentRegistry.Generals.GetValueOrDefault(human.GeneralId)?.FactionId;
        var emitted = new HashSet<(string SkillId, int OwnerSeat)>();
        // Public skill metadata keeps shared entries visible when their cost or
        // phase quota is unavailable. Only the viewer's legal actions enable them.
        foreach (var owner in _snapshot.Players.Where(player => player.IsAlive && player.Seat != human.Seat &&
                     !player.IdentityConcealed).OrderBy(player => player.Seat))
        foreach (var visible in (owner.IsGeneralPublic ? owner.Skills ?? [] : [])
                     .Concat(owner.IsSecondaryGeneralPublic ? owner.SecondarySkills ?? [] : []))
        {
            if (visible.ContentId is not { } id || !_contentRegistry.Skills.TryGetValue(id, out var skill) ||
                skill.Program is not { } program) continue;
            var shared = program.Contributions.Any(contribution => faction is not null &&
                    contribution.ProviderFactions.Contains(faction, StringComparer.Ordinal)) ||
                program.Activations.Any(activation => activation.Effects.Any(effect =>
                    effect.Op == SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd)) ||
                program.Activations.Any(activation => activation.OtherCharactersOnly) ||
                program.CardPolicies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.ForeignPublicPileSlash);
            if (shared && emitted.Add((id, owner.Seat))) yield return CreateForeignSkillCard(skill, owner, actions);
        }
        // New shared mechanisms can publish their entry without adding UI rules.
        foreach (var action in actions)
        {
            if (action.ProgramSkillId is not { } id || action.ProgramSkillOwnerSeat is not { } seat ||
                seat == human.Seat || !_contentRegistry.Skills.TryGetValue(id, out var skill) ||
                _snapshot.Players.SingleOrDefault(player => player.Seat == seat) is not { } owner ||
                !emitted.Add((id, seat))) continue;
            yield return CreateForeignSkillCard(skill, owner, actions);
        }
    }

    private HumanSkillViewModel CreateForeignSkillCard(ContentSkillDefinition skill, PlayerSnapshot owner,
        IReadOnlyList<LegalAction> actions)
    {
        var available = actions.Any(action => action.ProgramSkillId == skill.Id &&
            action.ProgramSkillOwnerSeat == owner.Seat);
        return new(skill.Name, GetVisibleSkillDescription(skill), "主动技 · 其他角色技能",
            available ? "当前可发动" : "当前不可发动",
            $"{owner.Seat + 1:00} 号位 · {owner.GeneralName} 提供", available, false)
        { ContentId = skill.Id, SkillOwnerSeat = owner.Seat };
    }
}
