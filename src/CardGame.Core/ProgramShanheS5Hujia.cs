namespace CardGame.Core;

internal interface IShanheS5JianxiongHost { void ClaimShanheS5JianxiongCards(ProgramSkillFrame frame); }

// Jianxiong's draw is independently useful when actual damage has no physical
// cards. Its later native claim must not be an admission prerequisite for Draw.
internal sealed class ClaimShanheS5JianxiongCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimShanheS5JianxiongCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimShanheS5JianxiongCardsHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ClaimDamageCards,
        static (effect, context) => context.ClaimDamageCards(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

internal sealed class ClaimShanheS5JianxiongCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimShanheS5JianxiongCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    { host.GetCapability<IShanheS5JianxiongHost>().ClaimShanheS5JianxiongCards(frame); return SkillProgramStepOutcome.Continue; }
}

internal static class ShanheS5JianxiongComposition
{
    [ProgramCatalogValidation(59601)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimShanheS5JianxiongCards)) &&
            !p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimShanheS5JianxiongCards))) return;
        if (p.Id != "shanhe:s5-jianxiong-14198" || p.Activations.Count != 0 || p.ViewAs.Count != 0 ||
            p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 ||
            p.Triggers is not [{ Id: "lost-hp-draw-then-claim", Window: SkillProgramTriggerWindow.AfterDamageApplied,
                Subject: SkillProgramTriggerSubject.Owner, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                Optional: true, UsageLimit: null, MarkerCost: null, Condition.Kind: SkillProgramTriggerConditionKind.Always } trigger] ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner,
                    NumberExpression: SkillProgramNumberExpression.LiveOwnerLostHp, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.ClaimShanheS5JianxiongCards, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": S5 Jianxiong requires a real per-damage optional lost-HP Draw followed by its original native causal-card claim.");
    }
}

public sealed record ShanheS5HujiaRewardReceipt(CardConversionSource Source, string GameplayHash,
    long CardActionId, long WindowFrameId, int ChooserSeat, int ActualTurnNumber, int ActualTurnOwnerSeat);
public sealed record ShanheS5HujiaRewardIssuedEvent(long FrameId, ShanheS5HujiaRewardReceipt Receipt) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5HujiaRewardReceipt? ShanheS5Hujia { get; init; }
}

internal interface IShanheS5HujiaHost { void BindShanheS5HujiaReward(ProgramSkillFrame frame); }

internal sealed class RewardOutOfTurnFactionDodgeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardOutOfTurnFactionDodge;
    public override ISkillProgramEffectHandler Handler { get; } = new RewardOutOfTurnFactionDodgeHandler();
    // The next ordinary Draw node owns the actual draw, its AI value and its children.
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "providerFactionId", "condition");
        var faction = r.RequiredIdentifier("providerFactionId");
        if (faction != "wei") throw new InvalidOperationException(r.Path + ": S5 Hujia requires a Wei provider.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), providerFactionId: faction);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardResponseAccepted)];
}

internal sealed class RewardOutOfTurnFactionDodgeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RewardOutOfTurnFactionDodge;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    { host.GetCapability<IShanheS5HujiaHost>().BindShanheS5HujiaReward(frame); return SkillProgramStepOutcome.Continue; }
}

internal static class ShanheS5HujiaComposition
{
    [ProgramCatalogValidation(59600)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardOutOfTurnFactionDodge)) &&
            !p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardOutOfTurnFactionDodge))) return;
        if (p.Id != "shanhe:s5-hujia-995" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Triggers.Count != 1 ||
            p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 ||
            p.CardPolicies is not [{ Kind: SkillProgramCardPolicyKind.FactionResponseRequest, FactionId: "wei", OwnerRole: Role.Lord } policy] ||
            !policy.RequiredCardKinds.SequenceEqual([CardKind.Dodge]))
            throw new InvalidOperationException(path + ": S5 Hujia requires native Wei Dodge assistance and its shared once-per-turn accepted-response reward.");
        foreach (var t in p.Triggers)
        {
            // Both a normal Slash-defense Dodge Use and a faction provider's
            // played Dodge pass the original CardResponseAccepted boundary.
            if (t.Id != "outside-used-or-played" || t.Window != SkillProgramTriggerWindow.CardResponseAccepted ||
                t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !t.Optional ||
                !t.CardKinds.SequenceEqual([CardKind.Dodge]) || t.UsageScope != SkillUsageScope.Turn || t.UsageLimit != 1 ||
                t.NamedUsageGroup != "faction-dodge-reward" || t.MarkerCost is not null ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.Effects is not [{ Op: SkillProgramEffectOp.RewardOutOfTurnFactionDodge, ProviderFactionId: "wei" },
                    { Op: SkillProgramEffectOp.Draw, Amount: 1, NumberExpression: null }] ||
                t.Effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always))
                throw new InvalidOperationException(path + ": S5 Hujia must bind one real off-turn actor/provider choice then perform ordinary Draw1 with one shared actual-turn quota.");
        }
    }
}
