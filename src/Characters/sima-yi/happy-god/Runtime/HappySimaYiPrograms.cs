using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IHappySimaYiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class HappySimaYiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new HappySimaYiHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
}
internal sealed class HappySimaFirstDrawDescriptor : HappySimaYiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferHappySimaJilueFirstDraw;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition", "replacementSuits", "minimumReplacementRank", "maximumReplacementRank");
        var suits = r.OptionalEnumArray<Suit>("replacementSuits") ?? [];
        var minimum = r.Has("minimumReplacementRank") ? r.RequiredInt("minimumReplacementRank") : 0;
        var maximum = r.Has("maximumReplacementRank") ? r.RequiredInt("maximumReplacementRank") : 0;
        if (suits.Count == 0 ? minimum != 0 || maximum != 0 : suits.Count != 4 || !suits.ToHashSet().SetEquals([Suit.Spade, Suit.Club, Suit.Heart, Suit.Diamond]) || minimum != 1 || maximum != 13)
            throw new InvalidOperationException(r.Path + ": the unconditional Guicai reward must accept every real replacement suit and rank.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            replacementSuits: suits, minimumReplacementRank: minimum, maximumReplacementRank: maximum);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class HappySimaRenjieDescriptor : HappySimaYiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GainHappySimaRenjie;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.GameStarting, SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.CardsMoved])];
}
internal sealed class HappySimaYiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IHappySimaYiHost>().Execute(f, Op);
}
public enum HappySimaYiStage { Choice, Drawing, Children }
public sealed record HappySimaYiReceipt(CardConversionSource Source, string Hash, long Parent, int Turn, int Instruction, long Payment, HappySimaYiStage Stage)
{ public NativeDrawOutcome? Draw { get; init; } }
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HappySimaYiReceipt? HappySimaYi { get; init; } }
public sealed record HappySimaJilueFirstOfferedEvent(long FrameId, CardConversionSource Source, string Hash, long Parent, int Turn, int Instruction, long Payment) : IGameEvent;
public sealed record HappySimaJilueFirstChosenEvent(long FrameId, bool Draw) : IGameEvent;
public sealed record HappySimaJilueDrawReturnedEvent(long FrameId, int Actual, long Before, long After, long? Attempt) : IGameEvent;

internal static class HappySimaYiComposition
{
    [ProgramCatalogValidation(41840)]
    internal static void Validate(string path, SkillProgram p)
    {
        var native = p.Id is "happy:renjie" or "happy:jilue" or "happy:lianpo" or "happy:jilue-wansha";
        if (!native)
        {
            if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is
                SkillProgramEffectOp.OfferHappySimaJilueFirstDraw or SkillProgramEffectOp.GainHappySimaRenjie))
                throw new InvalidOperationException(path + ": Happy Sima Yi operations require the complete paid four-skill edition.");
            return;
        }
        if (p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0)
            throw new InvalidOperationException(path + ": Happy Sima Yi cannot contain unrelated version abilities.");
        if (p.Id == "happy:renjie")
        {
            if (p.Activations.Count != 0 || p.CardPolicies.Count != 0 || !p.Triggers.Select(t => t.Id).SequenceEqual(["initial-ren", "damage-ren", "discard-ren"]) ||
                p.Triggers.Any(t => t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.MarkerCost is not null || t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                    t.Effects is not [{ Op: SkillProgramEffectOp.GainHappySimaRenjie, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]) ||
                p.Triggers[0].Window != SkillProgramTriggerWindow.GameStarting || !p.Triggers[0].IsSkillInvocation ||
                p.Triggers[1] is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage, Priority: 10 } ||
                p.Triggers[2] is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementOccurrence: SkillProgramMovementOccurrence.PerBatch, MovementDiscardOnly: true } discard ||
                !discard.SourceZones.SequenceEqual([CardZoneKind.Hand]) || discard.MovementReasons.Count != 0 || discard.ExcludedMovementReasons.Count != 0)
                throw new InvalidOperationException(path + ": Happy Renjie requires its initial one Ren and one whole actual damage/discard-batch gain.");
            return;
        }
        if (p.Id == "happy:jilue-wansha")
        {
            if (p.Activations.Count != 0 || p.Triggers.Count != 0 || p.CardPolicies is not [{ Kind: SkillProgramCardPolicyKind.ExclusiveDyingPeachRescue } only] ||
                !only.CardKinds.SequenceEqual([CardKind.Peach]))
                throw new InvalidOperationException(path + ": Happy Wansha requires its original turn lease and strict native Peach-user restriction.");
            return;
        }
        if (p.Id == "happy:lianpo")
        {
            if (p.Activations.Count != 0 || p.CardPolicies.Count != 0 || p.Triggers is not [{ Id: "kill-extra-turn", Window: SkillProgramTriggerWindow.CharacterDied,
                Subject: SkillProgramTriggerSubject.Owner, Optional: true, UsageScope: null, MarkerCost: null, Condition.Kind: SkillProgramTriggerConditionKind.DeathKillerIsOwner,
                Effects: [{ Op: SkillProgramEffectOp.PendExtraTurn, Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] }])
                throw new InvalidOperationException(path + ": Happy Lianpo requires the actual killer and native end-of-current-turn progression.");
            return;
        }
        if (p.CardPolicies.Count != 0 || p.Activations is not [{ Id: "turn-wansha", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                UsesPerTurn: null, MarkerCost: { Marker: PlayerMarkerKind.Ren, Amount: 1 } } activation] || !WanshaEffects(activation.Effects) ||
            !p.Triggers.Select(t => t.Id).SequenceEqual(["replace-judgment", "injury-exile", "trick-used", "nullification-used", "dying-wansha"]) ||
            p.Triggers.Any(t => !t.Optional || !t.IsSkillInvocation || t.MarkerCost is not { Marker: PlayerMarkerKind.Ren, Amount: 1 } ||
                t.UsageScope is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path + ": Happy Jilue requires all four real one-Ren entry points and one accepted-first-use reward.");
        var judgment = p.Triggers[0]; var exile = p.Triggers[1]; var trick = p.Triggers[2]; var counter = p.Triggers[3]; var dying = p.Triggers[4];
        if (judgment.Window != SkillProgramTriggerWindow.JudgmentReplacing || judgment.Subject != SkillProgramTriggerSubject.Any ||
            judgment.ExcludedReasons.Count != 0 || judgment.Effects is not [{ Op: SkillProgramEffectOp.ReplaceJudgment, Target: SkillProgramEffectTarget.Owner,
                OldCardDestination: SkillProgramOldJudgmentCardDestination.DiscardPile } replacement, { Op: SkillProgramEffectOp.OfferHappySimaJilueFirstDraw, Target: SkillProgramEffectTarget.Owner }] ||
            !replacement.Zones.ToHashSet().SetEquals([CardZoneKind.Hand, CardZoneKind.Equipment]) || !replacement.Suits.ToHashSet().SetEquals([Suit.Spade, Suit.Club, Suit.Heart, Suit.Diamond]))
            throw new InvalidOperationException(path + ": Happy Guicai requires the real HE judgment replacement before its first-use reward continuation.");
        var judgmentReward = judgment.Effects[1];
        if (judgmentReward.ReplacementSuits.Count != 4 || !judgmentReward.ReplacementSuits.ToHashSet().SetEquals([Suit.Spade, Suit.Club, Suit.Heart, Suit.Diamond]) ||
            judgmentReward.MinimumReplacementRank != 1 || judgmentReward.MaximumReplacementRank != 13 ||
            p.Triggers.Skip(1).SelectMany(t => t.Effects).Concat(activation.Effects).Any(e => e.ReplacementSuits.Count != 0))
            throw new InvalidOperationException(path + ": the accepted Guicai tail requires the complete native replacement-result gate, while other entries have none.");
        if (exile.Window != SkillProgramTriggerWindow.AfterDamageApplied || exile.Subject != SkillProgramTriggerSubject.Owner || exile.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage ||
            exile.Effects is not [{ Op: SkillProgramEffectOp.OfferHappySimaJilueFirstDraw, Target: SkillProgramEffectTarget.Owner },
                { Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.OtherLiving },
                { Op: SkillProgramEffectOp.TurnOver, Target: SkillProgramEffectTarget.SelectedTarget },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1, NumberExpression: null }])
            throw new InvalidOperationException(path + ": Happy Fangzhu must flip one other actual character then draw exactly one, independent of lost HP.");
        if (trick.Window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects || trick.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            !trick.CardCategories.SequenceEqual([SkillProgramCardCategory.InstantTrick]) || trick.CardKinds.Count != 0 || !JizhiEffects(trick.Effects) ||
            counter.Window != SkillProgramTriggerWindow.CardResponseAccepted || counter.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            !counter.CardKinds.SequenceEqual([CardKind.Nullification]) || !JizhiEffects(counter.Effects) ||
            dying.Window != SkillProgramTriggerWindow.DyingEntering || dying.Subject != SkillProgramTriggerSubject.Any || !WanshaEffects(dying.Effects))
            throw new InvalidOperationException(path + ": Happy Jilue requires actual ordinary-trick/Nullification uses and an own-turn paid Wansha entry.");
    }
    private static bool WanshaEffects(IReadOnlyList<SkillProgramEffect> effects) => effects is
        [{ Op: SkillProgramEffectOp.OfferHappySimaJilueFirstDraw, Target: SkillProgramEffectTarget.Owner },
            { Op: SkillProgramEffectOp.GrantTurnSkills, Target: SkillProgramEffectTarget.Owner, SkillIds: ["happy:jilue-wansha"] }];
    private static bool JizhiEffects(IReadOnlyList<SkillProgramEffect> effects) => effects is
        [{ Op: SkillProgramEffectOp.OfferHappySimaJilueFirstDraw, Target: SkillProgramEffectTarget.Owner },
            { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1, NumberExpression: null }];
}
