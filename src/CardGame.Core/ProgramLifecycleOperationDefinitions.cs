namespace CardGame.Core;

internal sealed class DamageProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.Damage;
    public override ISkillProgramEffectHandler Handler { get; } = new DamageSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.Damage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: damage requires selectedTarget.");
        return new(Op, target, DrawProgramOperationDescriptor.Amount(r, 20), r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

internal sealed class PindianProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.Pindian;
    public override ISkillProgramEffectHandler Handler { get; } = new PindianSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Pindian,
        static (effect, context) => context.Pindian(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget || r.RequiredInt("amount") != 1)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: pindian requires selectedTarget and amount 1.");
        var effect = new SkillProgramEffect(Op, target, 1, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new ConsumeSelectedCards(1), new CreatePindianResult("__active-pindian-result")];
}

internal sealed class ChangeMaximumHpProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChangeMaximumHp;
    public override ISkillProgramEffectHandler Handler { get; } = new ChangeMaximumHpSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeMaximumHp,
        static (effect, context) => context.ChangeMaximumHp(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var amount = r.RequiredInt("amount");
        if (amount is < -20 or > 20 || amount == 0)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: must be a non-zero value between -20 and 20.");
        return new(Op, target, amount, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class GrantSkillsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantSkills;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantSkillsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "skillIds", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var ids = r.RequiredIdentifierArray("skillIds");
        if (ids.Count == 0) throw new InvalidOperationException($"Invalid skill program at {r.Path}.skillIds: must not be empty.");
        return new(Op, target, 0, r.Condition(), skillIds: ids);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class InsertPhaseProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InsertPhase;
    public override ISkillProgramEffectHandler Handler { get; } = new InsertPhaseSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.PhaseInsertion;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.InsertPhase,
        static (effect, context) => context.InsertPhase(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "phase", "phaseContinuation", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var phase = r.RequiredEnum<TurnPhase>("phase");
        if (phase != TurnPhase.Play)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.phase: insertPhase currently supports play only.");
        var continuation = r.RequiredEnum<SkillProgramPhaseContinuation>("phaseContinuation");
        if (continuation != SkillProgramPhaseContinuation.BeforeNormalPreparation)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.phaseContinuation: insertPhase requires beforeNormalPreparation.");
        return new(Op, target, 0, r.Condition(), phase: phase, phaseContinuation: continuation);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect);
}

internal sealed class RecoverToProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverTo;
    public override ISkillProgramEffectHandler Handler { get; } = new RecoverToSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RecoverTo,
        static (effect, context) => context.RecoverTo(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "numberExpression", "minimumValue", "clampToMaxHp", "condition");
        var expression = r.RequiredEnum<SkillProgramNumberExpression>("numberExpression");
        if (expression is not (SkillProgramNumberExpression.LivingFactionCount or
            SkillProgramNumberExpression.IntegerConstant))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.numberExpression: recoverTo supports livingFactionCount or integerConstant.");
        var minimum = r.RequiredInt("minimumValue");
        if (minimum < 0 || expression == SkillProgramNumberExpression.IntegerConstant && minimum is not (>= 1 and <= 20))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.minimumValue: must be non-negative, or 1..20 for integerConstant.");
        var clamp = r.RequiredBool("clampToMaxHp");
        if (!clamp)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.clampToMaxHp: recoverTo must clamp to max HP.");
        return new(Op, r.RequiredEnum<SkillProgramEffectTarget>("target"), 0, r.Condition(),
            numberExpression: expression, minimumValue: minimum,
            clampToMaxHp: clamp);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect);
}

internal sealed class SelectTargetsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectTargetsSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTargets,
        static (effect, context) => context.SelectTargets(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetKind", "minimumTargets", "maximumTargets", "targetAiOrder", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind != SkillProgramTargetKind.OtherLivingWithHand)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetKind: selectTargets requires otherLivingWithHand.");
        var minimum = r.RequiredInt("minimumTargets");
        var maximum = r.RequiredInt("maximumTargets");
        if (minimum < 1 || maximum < minimum || maximum > 2)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: target bounds must satisfy 1 <= minimumTargets <= maximumTargets <= 2.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), targetKind: kind,
            minimumTargets: minimum, maximumTargets: maximum,
            targetAiOrder: r.RequiredEnum<SkillProgramTargetAiOrder>("targetAiOrder"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new SelectTargetSet(effect.MinimumTargets, effect.MaximumTargets)];
}

internal sealed class SelectSourceCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectSourceCard;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectSourceCardSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectSourceCard,
        static (effect, context) => context.SelectSourceCard(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "cardSource", "zones", "resultBind", "condition");
        var cardSource = r.Has("cardSource")
            ? r.RequiredEnum<SkillProgramCardSource>("cardSource")
            : SkillProgramCardSource.DamageSource;
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: requires hand and/or equipment.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), resultBind: r.RequiredIdentifier("resultBind"), zones: zones,
            cardSource: cardSource);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.CardSource == SkillProgramCardSource.DamageSource
            ? [new RequireContext(ProgramContextCapability.Damage), new CaptureSourceCard(effect.ResultBind!)]
            : [new CaptureSourceCard(effect.ResultBind!)];
}

internal sealed class ClaimDamageCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDamageCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimDamageCardsSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ClaimDamageCards,
        static (effect, context) => context.ClaimDamageCards(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class TakeRandomHandCardFromSelectedTargetsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new TakeRandomHandCardFromSelectedTargetsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TakeRandomHandCards,
        static (effect, context) => context.TakeRandomHandCards(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var amount = r.RequiredInt("amount");
        if (amount != 1)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: must be exactly 1 per selected target.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ConsumeTargetSet()];
}

internal sealed class AdjustNormalDrawProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustNormalDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new AdjustNormalDrawSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.DrawPlan;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.AdjustNormalDraw,
        static (effect, context) => context.AdjustNormalDraw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var amount = r.RequiredInt("amount");
        if (amount is < -20 or > 20 || amount == 0)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: must be a non-zero value from -20 through 20.");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal abstract class TurnEffectProgramOperationDescriptorBase : ProgramOperationDescriptorBase
{
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.TurnEffects;
    protected static SkillProgramEffectTarget Owner(ProgramOperationNodeReader reader) =>
        FilterBoundCardsProgramOperationDescriptor.Owner(reader);
}

internal sealed class GrantTurnCardDamageModifierProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardDamageModifier;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnCardDamageModifierSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardDamageModifier,
        static (effect, context) => context.GrantTurnCardDamageModifier(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "cardKinds", "condition");
        var amount = DrawProgramOperationDescriptor.Amount(r, 20);
        var kinds = r.RequiredEnumArray<CardKind>("cardKinds");
        if (kinds.Count == 0 || kinds.Any(kind => kind is not
                (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or
                 CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.FireAttack)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardKinds: contains a kind that cannot directly cause card-use damage.");
        return new(Op, Owner(r), amount, r.Condition(), cardKinds: kinds);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class GrantTurnCardActionProhibitionProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardActionProhibition;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnCardActionProhibitionSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardActionProhibition,
        static (effect, context) => context.GrantTurnCardActionProhibition(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "cardKinds", "actionTypes", "condition");
        var kinds = r.RequiredEnumArray<CardKind>("cardKinds");
        var actions = r.RequiredEnumArray<CardActionType>("actionTypes");
        if (kinds.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardKinds: must not be empty.");
        if (actions.Count == 0 || actions.Any(action => action is not (CardActionType.Use or CardActionType.Response)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.actionTypes: requires use and/or response.");
        return new(Op, Owner(r), 0, r.Condition(), cardKinds: kinds, actionTypes: actions);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class GrantTurnRuleModifierProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnRuleModifier;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnRuleModifierSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (effect, context) => context.GrantTurnRuleModifier(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        var query = r.RequiredEnum<SkillRuleQuery>("ruleQuery");
        var operation = r.RequiredEnum<SkillRuleOperation>("ruleOperation");
        if (query == SkillRuleQuery.SlashLimit && operation == SkillRuleOperation.Add)
        {
            r.AllowOnly("op", "target", "ruleQuery", "ruleOperation", "amount", "condition");
            return new(Op, Owner(r), DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(),
                ruleQuery: query, ruleOperation: operation);
        }
        r.AllowOnly("op", "target", "ruleQuery", "ruleOperation", "condition");
        if (query != SkillRuleQuery.SlashDistanceLimit || operation != SkillRuleOperation.Unlimited)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: supports slashLimit add or slashDistanceLimit unlimited.");
        return new(Op, Owner(r), 0, r.Condition(), ruleQuery: query, ruleOperation: operation);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class GrantTurnCardTargetRestrictionProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardTargetRestriction;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnCardTargetRestrictionSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (effect, context) => context.GrantTurnCardTargetRestriction(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "targetRestriction", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target is not (SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.SelectedTarget))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: requires owner or selectedTarget.");
        if (r.Has("amount") && r.RequiredInt("amount") != 1)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: must be 1 when present.");
        return new(Op, target, 1, r.Condition(),
            targetRestriction: r.RequiredEnum<SkillProgramCardTargetRestriction>("targetRestriction"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect);
}

internal sealed class StartJudgmentProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StartJudgment;
    public override ISkillProgramEffectHandler Handler { get; } = new StartJudgmentSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartJudgment,
        static (effect, context) => context.StartJudgment(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "judgmentReason", "resultBind", "visibility", "condition");
        var visibility = r.RequiredEnum<SkillProgramCardSetVisibility>("visibility");
        if (visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.visibility: judgment results must remain public.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), resultBind: r.RequiredIdentifier("resultBind"), visibility: visibility,
            judgmentReason: r.RequiredIdentifier("judgmentReason"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new CreateCardSet(effect.ResultBind!, 1, true)];
}

internal sealed class GrantTurnCardConversionProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardConversion;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnCardConversionSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardConversion,
        static (effect, context) => context.GrantTurnCardConversion(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "colorRelation", "outputKind", "condition");
        var relation = r.RequiredEnum<SkillProgramCardColorRelation>("colorRelation");
        var output = r.RequiredEnum<CardKind>("outputKind");
        if (relation != SkillProgramCardColorRelation.OppositeBoundCard || output != CardKind.Duel)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: supports oppositeBoundCard as duel only.");
        var effect = new SkillProgramEffect(Op, Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), colorRelation: relation, outputKind: output);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSingleCardSet(effect.SourceBind!)];
}

internal sealed class DiscardOwnedZoneCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardOwnedZoneCards;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardOwnedZoneCardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardOwnedZoneCards,
        static (effect, context) => context.DiscardOwnedZoneCards(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "condition");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not
                (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: requires hand, equipment and/or judgment.");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), zones: zones);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class SetChainedStateProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetChainedState;
    public override ISkillProgramEffectHandler Handler { get; } = new SetChainedStateSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SetChainedState,
        static (effect, context) => context.SetChainedState(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "chained", "condition");
        return new(Op, r.RequiredEnum<SkillProgramEffectTarget>("target"), 0, r.Condition(),
            chained: r.RequiredBool("chained"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => WithSelectedTarget(effect);
}
