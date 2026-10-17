namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private sealed record DraftSkillBenefits(double Draw, double Recovery, double Defense, double Support,
        double Offense, double Control, double Reach, double PaidHp, double Attrition, double Growth);

    private static int DraftActivationCardCost(SkillProgramActivation activation) => activation.MinCards +
        activation.Effects.Where(effect => effect.Target == SkillProgramEffectTarget.Owner &&
            effect.CardOwnerRef?.Kind is null or ProgramParticipantRef.Owner &&
            (effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard && effect.Destination == SkillProgramCardDestination.DiscardPile ||
             effect.Op == SkillProgramEffectOp.SelectOwnedCards && effect.ResultBind is { } bind &&
             activation.Effects.Any(move => move.Op == SkillProgramEffectOp.MoveBoundCards &&
                 move.SourceBind == bind && move.Destination == SkillProgramCardDestination.DiscardPile)))
            .Sum(effect => Math.Max(1, Math.Max(effect.MinimumCards, effect.Amount)));

    // These are bounded draft heuristics, not a simulation of an unknown opening
    // hand. Inspect executable capabilities, including activations whose printed
    // metadata does not publish ActionForms, rather than comparing edition names.
    private static DraftSkillBenefits ReadDraftSkillBenefits(SkillProgram program, ContentRegistry? registry,
        HashSet<string>? ancestors = null)
    {
        ancestors = ancestors is null ? new(StringComparer.Ordinal) : new(ancestors, StringComparer.Ordinal);
        ancestors.Add(program.Id);
        double draw = 0, recovery = 0, defense = 0, support = 0, offense = 0, control = 0, reach = 0;
        double paidHp = 0, attrition = 0, growth = 0;
        foreach (var modifier in program.Modifiers)
        {
            var weight = modifier.Condition.Kind == SkillProgramConditionKind.Always ? 1 : .65;
            if (modifier.Query == SkillRuleQuery.DrawCount && modifier.Operation == SkillRuleOperation.Add)
                draw += Math.Max(0, modifier.Value) * weight;
            if (modifier.Query == SkillRuleQuery.HandLimit && (modifier.Value > 0 || modifier.ValueExpression is not null))
                draw += .2 * weight;
            if (modifier.Query == SkillRuleQuery.SlashLimit && modifier.Value > 0) offense += .8 * weight;
            if (modifier.Query is SkillRuleQuery.OutgoingDistance or SkillRuleQuery.SlashDistanceLimit)
                reach += .6 * weight;
        }
        foreach (var policy in program.CardPolicies)
        {
            var weight = policy.Condition.Kind == SkillProgramConditionKind.Always ? 1 : .5;
            switch (policy.Kind)
            {
                case SkillProgramCardPolicyKind.ProhibitTarget:
                case SkillProgramCardPolicyKind.ProhibitTargetBySuit:
                case SkillProgramCardPolicyKind.ProhibitBlackTrickTarget:
                case SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget:
                    defense += .7 * weight;
                    break;
                case SkillProgramCardPolicyKind.VirtualEquipment:
                case SkillProgramCardPolicyKind.NullifyBlackSlashWithoutArmor:
                case SkillProgramCardPolicyKind.NullifyBlackSlashByCurrentHp:
                case SkillProgramCardPolicyKind.PreventIncomingTrickDamage:
                case SkillProgramCardPolicyKind.PreventTrickDamage:
                    defense += .8 * weight;
                    break;
                case SkillProgramCardPolicyKind.FactionResponseRequest:
                    defense += .7 * weight;
                    break;
                case SkillProgramCardPolicyKind.RescueRecoveryBonus:
                    recovery += .35 * weight;
                    break;
                case SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard:
                case SkillProgramCardPolicyKind.OfferSkipDiscard:
                    draw += .4 * weight;
                    break;
                case SkillProgramCardPolicyKind.IgnoreUseDistance:
                case SkillProgramCardPolicyKind.SlashRangeFromEffectiveRank:
                    reach += .6 * weight;
                    break;
                case SkillProgramCardPolicyKind.MinimumResponseCount:
                case SkillProgramCardPolicyKind.ProhibitTargetSlashResponseBySuit:
                case SkillProgramCardPolicyKind.ProhibitNearbyTargetResponse:
                case SkillProgramCardPolicyKind.ProhibitBlackSlashResponseByCurrentHand:
                    offense += .6 * weight;
                    break;
            }
        }
        foreach (var rule in program.ViewAs)
        {
            var weight = (rule.Condition.Kind == SkillProgramConditionKind.Always ? 1 : .6) /
                Math.Max(1, rule.InputCount);
            if (rule.ForResponse && rule.OutputKind is CardKind.Dodge or CardKind.Nullification) defense += .7 * weight;
            if (rule.ForPlay && rule.OutputKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) offense += .7 * weight;
            if (rule.ForPlay && rule.OutputKind == CardKind.Peach) recovery += .7 * weight;
        }
        offense += Math.Min(1, program.DamageModifiers.Count * .5);
        support += Math.Min(.7, program.Contributions.Count * .7);
        foreach (var group in program.Activations.GroupBy(activation => activation.UsageGroup))
        {
            var before = Capture();
            var alternatives = new List<DraftSkillBenefits>();
            foreach (var activation in group)
            {
                Restore(before);
                ReadEffects(activation.Effects, activation.UsesPerGame is not null ? .45 : 1, activation.TargetKind, false, false, true);
                alternatives.Add(Capture());
            }
            // Alternative payments for one shared use do not grant the benefit twice.
            Restore(new(alternatives.Max(value => value.Draw), alternatives.Max(value => value.Recovery),
                alternatives.Max(value => value.Defense), alternatives.Max(value => value.Support),
                alternatives.Max(value => value.Offense), alternatives.Max(value => value.Control),
                alternatives.Max(value => value.Reach), alternatives.Min(value => value.PaidHp),
                alternatives.Max(value => value.Attrition), alternatives.Max(value => value.Growth)));
        }
        foreach (var trigger in program.Triggers)
        {
            // Injury rewards cannot prevent the first damage or guarantee survival
            // until the next turn. Multiple points increase benefit, not defense.
            var afterInjury = trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.Window is
                SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                SkillProgramTriggerWindow.AfterHpLost;
            var weight = afterInjury ? .55 : trigger.Window is SkillProgramTriggerWindow.SelfDyingResponse or
                SkillProgramTriggerWindow.DyingEntering or SkillProgramTriggerWindow.DyingResponse ? .45 : 1;
            if (trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint) weight *= 1.25;
            if (trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always) weight *= .8;
            if (trigger.Window == SkillProgramTriggerWindow.SlashTargetRedirecting) defense += .7 * weight;
            ReadEffects(trigger.Effects, weight, trigger.Subject == SkillProgramTriggerSubject.Owner
                ? SkillProgramTargetKind.AnyLiving : SkillProgramTargetKind.EventTarget,
                !trigger.Optional && trigger.UsageScope != SkillUsageScope.Game, afterInjury, false);
        }
        return new(Math.Min(2.5, draw), Math.Min(1.5, recovery), Math.Min(1.5, defense), Math.Min(1.5, support),
            Math.Min(2, offense), Math.Min(2, control), Math.Min(1.5, reach), Math.Min(2, paidHp), Math.Min(2, attrition), Math.Min(1.5, growth));

        DraftSkillBenefits Capture() => new(draw, recovery, defense, support, offense, control, reach, paidHp, attrition, growth);
        void Restore(DraftSkillBenefits value) => (draw, recovery, defense, support, offense, control, reach, paidHp, attrition, growth) =
            (value.Draw, value.Recovery, value.Defense, value.Support, value.Offense, value.Control, value.Reach,
                value.PaidHp, value.Attrition, value.Growth);

        void ReadEffects(IReadOnlyList<SkillProgramEffect> effects, double timing, SkillProgramTargetKind selectedKind,
            bool mandatory, bool afterInjury, bool paidActivation)
        {
            var foreignBinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in effects)
            {
                if (effect.Op is SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets && effect.TargetKind is { } kind)
                    selectedKind = kind;
                var self = effect.TargetReference is { } reference ? reference.Kind == ProgramParticipantRef.Owner ? 1d : 0 :
                    effect.Target == SkillProgramEffectTarget.Owner ? 1d :
                    effect.Target is SkillProgramEffectTarget.HpPairHigher or SkillProgramEffectTarget.HpPairLower ? .5 :
                    effect.Target is SkillProgramEffectTarget.SelectedTarget or SkillProgramEffectTarget.SelectedTargets &&
                    selectedKind is SkillProgramTargetKind.AnyLiving or SkillProgramTargetKind.AnyWounded or
                        SkillProgramTargetKind.AnyLivingHandBelowMaxHp or SkillProgramTargetKind.AnyLivingWithHand ? .65 : 0;
                var weight = timing * (effect.Condition.Kind == SkillProgramConditionKind.Always ? 1 : .65);
                var amount = effect.NumberExpression switch
                {
                    SkillProgramNumberExpression.CurrentTurnUsedCardCategoryCount => 1.8,
                    SkillProgramNumberExpression.LivingFactionCount => 1.5,
                    SkillProgramNumberExpression.OwnerLostHp => 1.3,
                    not null => 1,
                    _ => Math.Clamp(effect.Amount, 1, 3)
                };
                if (effect.ResultBind is { } bind && (effect.Op == SkillProgramEffectOp.SelectSourceCard &&
                    effect.CardSource == SkillProgramCardSource.DamageSource ||
                    effect.Op is SkillProgramEffectOp.SelectOwnedCards or SkillProgramEffectOp.SelectAndMoveOwnedCard && self == 0))
                    foreignBinds.Add(bind);
                switch (effect.Op)
                {
                    case SkillProgramEffectOp.MouCaoPiXingshang:
                        draw += 1.5 * weight;
                        recovery += .8 * weight;
                        growth += .8 * weight;
                        support += weight;
                        break;
                    case SkillProgramEffectOp.MouCaoPiFangzhu:
                        control += 1.8 * weight;
                        break;
                    case SkillProgramEffectOp.MouCaoPiSongwei:
                        draw += .5 * weight;
                        break;
                    case SkillProgramEffectOp.MouCaoPiRemoveWeiSkills:
                        control += .6 * weight;
                        break;
                    case SkillProgramEffectOp.Draw:
                        draw += amount * self * weight;
                        if (self < 1) support += .7 * amount * weight;
                        break;
                    case SkillProgramEffectOp.Recover:
                    case SkillProgramEffectOp.RecoverTo:
                    case SkillProgramEffectOp.RecoverToMaximum:
                        recovery += self * weight;
                        if (self < 1) support += weight;
                        break;
                    case SkillProgramEffectOp.LoseHp when self > 0:
                    case SkillProgramEffectOp.ChangeMaximumHp when self > 0 && effect.Amount < 0:
                        if (mandatory && !afterInjury) attrition = Math.Max(attrition, timing);
                        else if (paidActivation) paidHp = Math.Max(paidHp, amount * weight);
                        break;
                    case SkillProgramEffectOp.Damage when self == 0:
                    case SkillProgramEffectOp.LoseHp when self == 0:
                    case SkillProgramEffectOp.StartVirtualDuel:
                        offense += weight;
                        break;
                    case SkillProgramEffectOp.PreventCurrentDamage:
                    case SkillProgramEffectOp.NullifyCurrentCardEffect:
                        if (!afterInjury) defense += weight;
                        break;
                    case SkillProgramEffectOp.PreventDamageAndConsumeSourceFaction:
                    case SkillProgramEffectOp.OfferBoundCardsForDamagePrevention:
                        defense += .6 * weight;
                        break;
                    case SkillProgramEffectOp.RevealUniqueRankForDying:
                        recovery += .5 * weight;
                        break;
                    case SkillProgramEffectOp.PayHpThenNullifyOwnActualUseTarget:
                        defense += .25 * weight;
                        paidHp = Math.Max(paidHp, weight);
                        break;
                    case SkillProgramEffectOp.MoveBoundCards when effect.SourceBind is { } source && foreignBinds.Contains(source):
                        if (effect.Destination == SkillProgramCardDestination.OwnerHand) draw += .7 * weight;
                        else if (effect.Destination == SkillProgramCardDestination.DiscardPile) control += .7 * weight;
                        break;
                    case SkillProgramEffectOp.MoveBoundCards when effect.Destination == SkillProgramCardDestination.SelectedTargetHand:
                        support += .5 * weight;
                        break;
                    case SkillProgramEffectOp.GiveSelected:
                    case SkillProgramEffectOp.GiveBoundCard:
                    case SkillProgramEffectOp.DistributeOwnedCards:
                    case SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit:
                    case SkillProgramEffectOp.RecoverOtherDyingVictimTo:
                        support += weight;
                        break;
                    case SkillProgramEffectOp.ReorderTopCards:
                        draw += .6 * weight;
                        control += .3 * weight;
                        break;
                    case SkillProgramEffectOp.ClaimDamageCards:
                    case SkillProgramEffectOp.RepeatJudgment:
                        draw += .6 * weight;
                        break;
                    case SkillProgramEffectOp.ObtainBoundCardsAndArmNextRevealBonus:
                        draw += 1.5 * weight;
                        break;
                    case SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd:
                        defense += .5 * weight;
                        break;
                    case SkillProgramEffectOp.OfferVirtualBasicCard:
                        recovery += .7 * weight;
                        offense += .5 * weight;
                        break;
                    case SkillProgramEffectOp.OfferRedDiscardRecoveryChoice:
                        recovery += .7 * weight;
                        support += .4 * weight;
                        break;
                    case SkillProgramEffectOp.DrawExtraAndArmTurnDamageUseDebt:
                        draw += weight; // Extra cards incur an ending payment, not pure card advantage.
                        break;
                    case SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport:
                        draw += weight;
                        support += .6 * weight;
                        break;
                    case SkillProgramEffectOp.RequestAttackRangeAid:
                        draw += .65 * weight;
                        support += .5 * weight;
                        break;
                    case SkillProgramEffectOp.DrawPrepDiscardEnding:
                        draw += .6 * weight;
                        break;
                    case SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal:
                    case SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo:
                        draw += .8 * weight;
                        support += .5 * weight;
                        break;
                    case SkillProgramEffectOp.RecastSelectedPhysicalSlash:
                    case SkillProgramEffectOp.RecastSelectedCards:
                        draw += .35 * weight;
                        break;
                    case SkillProgramEffectOp.GrantTurnHandCardProhibition:
                    case SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard:
                    case SkillProgramEffectOp.IssueShownEntityTurnPolicy:
                    case SkillProgramEffectOp.GrantDirectedTurnCardPolicy:
                    case SkillProgramEffectOp.DiscardBoundCardForTurnSlashBenefits:
                        offense += .7 * weight;
                        control += .5 * weight;
                        break;
                    case SkillProgramEffectOp.ProhibitCurrentResponse:
                    case SkillProgramEffectOp.PreventCurrentTargetSlashCancellation:
                    case SkillProgramEffectOp.AddCurrentTargetSlashDamage:
                    case SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner:
                    case SkillProgramEffectOp.InsertGrantedEntityPlayPhase:
                        offense += .6 * weight;
                        break;
                    case SkillProgramEffectOp.ClaimGrantedPhaseSlash:
                    case SkillProgramEffectOp.DrawToFrozenFactionCount:
                        draw += weight;
                        break;
                    case SkillProgramEffectOp.FreezeLivingFactionRecovery:
                        recovery += weight;
                        break;
                    case SkillProgramEffectOp.DiscardDistinctFactionParticipants:
                        control += weight;
                        break;
                    case SkillProgramEffectOp.ViewAndTakeSelectedTargetHand:
                        draw += .8 * weight;
                        control += .6 * weight;
                        break;
                    case SkillProgramEffectOp.RequestHandBySuitsOrLoseHp:
                        draw += .4 * weight;
                        offense += .5 * weight;
                        break;
                    case SkillProgramEffectOp.RequestSlashAgainstChosenTarget:
                        offense += .7 * weight;
                        support += .3 * weight;
                        break;
                    case SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard:
                        recovery += .2 * weight;
                        support += .6 * weight;
                        control += .4 * weight;
                        break;
                    case SkillProgramEffectOp.GrantSkills when registry is not null && self > 0:
                        foreach (var id in effect.SkillIds.Where(id => !ancestors.Contains(id)))
                        {
                            if (!registry.Skills.TryGetValue(id, out var granted) || granted.Program is not { } next) continue;
                            var future = ReadDraftSkillBenefits(next, registry, ancestors);
                            var discount = .5 * weight;
                            draw += future.Draw * discount;
                            recovery += future.Recovery * discount;
                            defense += future.Defense * discount;
                            support += future.Support * discount;
                            offense += future.Offense * discount;
                            growth += discount;
                        }
                        break;
                }
            }
        }
    }
}
