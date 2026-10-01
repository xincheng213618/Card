namespace CardGame.Core;

internal sealed class DamageProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.Damage;
    public override ISkillProgramEffectHandler Handler { get; } = new DamageSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.Damage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition", "sourceRef", "targetRef", "skipIfNoTarget", "nature");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target is not (SkillProgramEffectTarget.SelectedTarget or SkillProgramEffectTarget.Owner))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: damage requires selectedTarget or owner.");
        var sourceRef = r.Has("sourceRef") ? r.RequiredParticipantReference("sourceRef") : null;
        var targetRef = r.Has("targetRef") ? r.RequiredParticipantReference("targetRef") : null;
        if (targetRef is not null && (target != SkillProgramEffectTarget.Owner ||
                                      targetRef.Kind is not
                                          (ProgramParticipantRef.EventTarget or ProgramParticipantRef.EventSource)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetRef: event damage participant requires owner placeholder.");
        var nature = r.Has("nature") ? r.RequiredEnum<DamageNature>("nature") : (DamageNature?)null;
        if (sourceRef?.Kind is not null and not (ProgramParticipantRef.Owner or ProgramParticipantRef.SelectedTarget or
            ProgramParticipantRef.ResultSource or ProgramParticipantRef.ResultOpponent))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.sourceRef: unsupported damage source.");
        var skip = r.Has("skipIfNoTarget") && r.RequiredBool("skipIfNoTarget");
        if (skip && target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.skipIfNoTarget: requires selectedTarget.");
        return new(Op, target, DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(),
            actorReference: sourceRef, targetReference: targetRef, skipIfNoTarget: skip,
            damageNature: nature);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        (effect.Target == SkillProgramEffectTarget.SelectedTarget
            ? [new ReadSelectedTarget()] : Array.Empty<ProgramResourceOperation>())
        .Concat(effect.ActorReference?.Kind == ProgramParticipantRef.SelectedTarget ? [new ReadSelectedTarget()] : Array.Empty<ProgramResourceOperation>())
        .Concat(effect.ActorReference is { Kind: ProgramParticipantRef.ResultOpponent or ProgramParticipantRef.ResultSource } reference
            ? [new ReadPindianResult(reference.ResultBind!)] : Array.Empty<ProgramResourceOperation>())
        .Concat(effect.TargetReference is null ? [] :
            [new RequireAnyContext(ProgramContextCapability.Judgment | ProgramContextCapability.Damage |
                                   ProgramContextCapability.CardAction)]).ToArray();
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
            SkillProgramNumberExpression.LivingPlayersMinHp or
            SkillProgramNumberExpression.IntegerConstant))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.numberExpression: recoverTo supports livingFactionCount, livingPlayersMinHp or integerConstant.");
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
        r.AllowOnly("op", "target", "targetKind", "minimumTargets", "maximumTargets", "numberExpression", "targetAiOrder", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind is not (SkillProgramTargetKind.OtherLivingWithHand or SkillProgramTargetKind.OtherLivingUnequalHandPair or
            SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.LivingWhoseAttackRangeIncludesLord or SkillProgramTargetKind.OtherLivingHandAtLeastOwner or
            SkillProgramTargetKind.CurrentCardUseTargets or SkillProgramTargetKind.OtherLivingMale or
            SkillProgramTargetKind.AnyWounded or SkillProgramTargetKind.OtherLivingPair or
            SkillProgramTargetKind.LivingPairDistinct or SkillProgramTargetKind.EquipmentExchangePair))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.targetKind: unsupported target set.");
        var minimum = r.RequiredInt("minimumTargets");
        var maximum = r.RequiredInt("maximumTargets");
        var numberExpression = r.Has("numberExpression")
            ? r.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        if (numberExpression is not null and not (SkillProgramNumberExpression.CurrentHandCount or
            SkillProgramNumberExpression.PlannedNormalDrawCount or SkillProgramNumberExpression.BoundCardCount or SkillProgramNumberExpression.CurrentHp))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: unsupported target maximum expression.");
        if (minimum < 1 || maximum < minimum || maximum > (kind == SkillProgramTargetKind.CurrentCardUseTargets
            ? 64 : kind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.LivingWhoseAttackRangeIncludesLord or SkillProgramTargetKind.AnyWounded or
                SkillProgramTargetKind.OtherLivingHandAtLeastOwner ? 8 : 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: target bounds exceed the supported participant count.");
        if (kind == SkillProgramTargetKind.OtherLivingUnequalHandPair && (minimum != 2 || maximum != 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: unequal-hand pairs require exactly two targets.");
        if (kind == SkillProgramTargetKind.OtherLivingPair && (minimum != 2 || maximum != 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: hand-ordered pairs require exactly two targets.");
        if (kind == SkillProgramTargetKind.OtherLivingMale && (minimum != 2 || maximum != 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: ordered male pairs require exactly two targets.");
        if (kind == SkillProgramTargetKind.LivingPairDistinct && (minimum != 2 || maximum != 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: ordered living pairs require exactly two targets.");
        if (kind == SkillProgramTargetKind.EquipmentExchangePair && (minimum != 2 || maximum != 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: equipment exchange requires exactly two targets.");
        var aiOrder = r.RequiredEnum<SkillProgramTargetAiOrder>("targetAiOrder");
        if (aiOrder == SkillProgramTargetAiOrder.CardEffectIntervention &&
            kind != SkillProgramTargetKind.CurrentCardUseTargets)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: card-effect intervention requires current card-use targets.");
        if (aiOrder == SkillProgramTargetAiOrder.SupportFirstThenOpposeSecond &&
            (kind is not (SkillProgramTargetKind.OtherLivingUnequalHandPair or
                SkillProgramTargetKind.OtherLivingMale) || minimum != 2 || maximum != 2))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: support-first order requires an eligible pair.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), numberExpression: numberExpression, targetKind: kind,
            minimumTargets: minimum, maximumTargets: maximum,
            targetAiOrder: aiOrder);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.TargetKind == SkillProgramTargetKind.CurrentCardUseTargets
            ? [new RequireContext(ProgramContextCapability.CardAction),
                new SelectTargetSet(effect.MinimumTargets, effect.MaximumTargets)]
            : effect.NumberExpression == SkillProgramNumberExpression.PlannedNormalDrawCount
            ? [new RequireContext(ProgramContextCapability.DrawPlan),
                new SelectTargetSet(effect.MinimumTargets, effect.MaximumTargets)]
            : [new SelectTargetSet(effect.MinimumTargets, effect.MaximumTargets)];
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
        r.AllowOnly("op", "target", "cardSource", "zones", "resultBind", "condition",
            "equipmentSlots", "skipIfNoCards", "allowSameSource");
        var cardSource = r.Has("cardSource")
            ? r.RequiredEnum<SkillProgramCardSource>("cardSource")
            : SkillProgramCardSource.DamageSource;
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Any(zone => zone is not
            (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain or
             CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao)) ||
            cardSource != SkillProgramCardSource.Owner && zones.Any(zone => zone is not
                (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.zones: requires hand/equipment or an owned persistent pile.");
        var equipmentSlots = r.Has("equipmentSlots")
            ? r.RequiredEnumArray<EquipmentSlot>("equipmentSlots") : [];
        if (r.Has("equipmentSlots") && (equipmentSlots.Count == 0 ||
            equipmentSlots.Distinct().Count() != equipmentSlots.Count ||
            !zones.Contains(CardZoneKind.Equipment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.equipmentSlots: requires distinct slots and an equipment zone.");
        var skipIfNoCards = r.Has("skipIfNoCards") && r.RequiredBool("skipIfNoCards");
        var allowSameSource = r.Has("allowSameSource") && r.RequiredBool("allowSameSource");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), resultBind: r.RequiredIdentifier("resultBind"), zones: zones,
            cardSource: cardSource, equipmentSlots: equipmentSlots,
            skipIfNoCards: skipIfNoCards, allowSameSource: allowSameSource);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        (effect.CardSource is SkillProgramCardSource.DamageSource or SkillProgramCardSource.EventTarget)
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

internal sealed class TakeRandomCardFromEveryOtherCharacterProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomCardFromEveryOtherCharacter;
    public override ISkillProgramEffectHandler Handler { get; } = new TakeRandomCardFromEveryOtherCharacterSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TakeRandomCardFromEveryOtherCharacter,
        static (effect, context) => context.TakeRandomCardFromEveryOtherCharacter(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Distinct().Count() != zones.Count ||
            zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.zones: requires distinct hand/equipment/judgment areas.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), zones: zones);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
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
        r.AllowOnly("op", "target", "amount", "numberExpression", "condition");
        var expression = r.Has("numberExpression")
            ? r.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        if (expression is not null and not SkillProgramNumberExpression.SelectedTargetCount)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.numberExpression: only selectedTargetCount is supported.");
        var amount = r.Has("amount") ? r.RequiredInt("amount") : 0;
        if (expression is null && amount is < -20 or > 20 || expression is null && amount == 0 ||
            expression is not null && (r.Has("amount") || amount != 0))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: specify a non-zero fixed amount or selectedTargetCount without amount.");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition(),
            numberExpression: expression);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.NumberExpression == SkillProgramNumberExpression.SelectedTargetCount
            ? [new ReadTargetSet(1)] : [];
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
        r.AllowOnly("op", "target", "amount", "cardKinds", "expires", "sourceScope", "condition");
        var amount = DrawProgramOperationDescriptor.Amount(r, 20);
        var kinds = r.RequiredEnumArray<CardKind>("cardKinds");
        if (kinds.Count == 0 || kinds.Any(kind => kind is not
                (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or
                 CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.FireAttack)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardKinds: contains a kind that cannot directly cause card-use damage.");
        return new(Op, Owner(r), amount, r.Condition(), cardKinds: kinds,
            damageModifierExpiration: r.Has("expires")
                ? r.RequiredEnum<SkillProgramDamageModifierExpiration>("expires")
                : SkillProgramDamageModifierExpiration.CurrentTurnEnd,
            damageModifierSourceScope: r.Has("sourceScope")
                ? r.RequiredEnum<SkillProgramDamageModifierSourceScope>("sourceScope")
                : SkillProgramDamageModifierSourceScope.OwnerUsed);
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

internal sealed class GrantTurnHandColorRestrictionProgramOperationDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnHandColorRestriction;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnHandColorRestrictionSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnHandColorRestriction,
        static (effect, context) => context.GrantTurnHandColorRestriction(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "useFrozenSuit", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: hand-color restriction requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"),
            useFrozenSuit: r.Has("useFrozenSuit") && r.RequiredBool("useFrozenSuit"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        WithSelectedTarget(effect, [new ReadSingleCardSet(effect.SourceBind!)]);
}

internal sealed class PreventCurrentDamageProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new PreventCurrentDamageSkillProgramEffectHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
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
        if (query == SkillRuleQuery.OutgoingDistance && operation == SkillRuleOperation.Add)
        {
            r.AllowOnly("op", "target", "ruleQuery", "ruleOperation", "amount", "condition");
            var amount = r.RequiredInt("amount");
            if (amount is < -20 or > 20 || amount == 0)
                throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: outgoing distance requires a nonzero value in -20..20.");
            return new(Op, Owner(r), amount, r.Condition(), ruleQuery: query, ruleOperation: operation);
        }
        if (query == SkillRuleQuery.CardTargetCount && operation == SkillRuleOperation.Add)
        {
            r.AllowOnly("op", "target", "ruleQuery", "ruleOperation", "amount", "cardKinds", "condition");
            var kinds = r.RequiredEnumArray<CardKind>("cardKinds");
            if (kinds.Count == 0 || kinds.Distinct().Count() != kinds.Count)
                throw new InvalidOperationException($"Invalid skill program at {r.Path}.cardKinds: requires distinct card kinds.");
            return new(Op, Owner(r), DrawProgramOperationDescriptor.Amount(r, 20), r.Condition(),
                cardKinds: kinds, ruleQuery: query, ruleOperation: operation);
        }
        if (query is (SkillRuleQuery.SlashLimit or SkillRuleQuery.HandLimit) &&
            operation == SkillRuleOperation.Add)
        {
            r.AllowOnly("op", "target", "ruleQuery", "ruleOperation", "amount", "condition");
            var amount = r.RequiredInt("amount");
            if (amount is < -20 or > 20 || amount == 0 || query == SkillRuleQuery.SlashLimit && amount < 0)
                throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: unsupported rule modifier amount.");
            return new(Op, Owner(r), amount, r.Condition(),
                ruleQuery: query, ruleOperation: operation);
        }
        r.AllowOnly("op", "target", "ruleQuery", "ruleOperation", "condition");
        if (query is not (SkillRuleQuery.SlashDistanceLimit or SkillRuleQuery.AttackRange) ||
            operation != SkillRuleOperation.Unlimited)
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}: supports slashLimit/handLimit add, slashDistanceLimit unlimited or attackRange unlimited.");
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
        r.AllowOnly("op", "target", "judgmentReason", "resultBind", "visibility", "sourceRef", "condition");
        var visibility = r.RequiredEnum<SkillProgramCardSetVisibility>("visibility");
        if (visibility != SkillProgramCardSetVisibility.Public)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.visibility: judgment results must remain public.");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target is not (SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.SelectedTarget))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.target: judgments support owner or selectedTarget.");
        var sourceRef = r.Has("sourceRef") ? r.RequiredParticipantReference("sourceRef") : null;
        if (sourceRef is not null && sourceRef.Kind is not
            (ProgramParticipantRef.EventSource or ProgramParticipantRef.EventTarget))
            throw new InvalidOperationException(
                $"Invalid skill program at {r.Path}.sourceRef: judgment source must be eventSource or eventTarget.");
        var effect = new SkillProgramEffect(Op, target, 0,
            r.Condition(), resultBind: r.RequiredIdentifier("resultBind"), visibility: visibility,
            judgmentReason: r.RequiredIdentifier("judgmentReason"), sourceRef: sourceRef);
        // A named-choice branch may gate the judgment itself (Baonve: the damage
        // source chooses whether to judge); resource cleanup of the conditional
        // binding is validated at the composition level.
        if (effect.Condition.Kind is not (SkillProgramConditionKind.Always or SkillProgramConditionKind.ChoiceIs))
            RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Array.AsReadOnly((effect.Target == SkillProgramEffectTarget.SelectedTarget
            ? new ProgramResourceOperation[] { new ReadSelectedTarget(), new CreateCardSet(effect.ResultBind!, 1, true) }
            : [new CreateCardSet(effect.ResultBind!, 1, true)]));
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
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => effect.Target == SkillProgramEffectTarget.SelectedTargets ? [new ReadTargetSet(1)] : WithSelectedTarget(effect);
}
