namespace CardGame.Core;

internal interface IWeiZiProgramHost
{
    SkillProgramStepOutcome YuanziResolve(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YuanziDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome LiejieSourceDiscard(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 援资 arm: the accepted whole-hand gift rides the owner's own preparation-less
// turn-start window; the estimate prices the recipient's gain and the owner's
// whole-hand loss through the shared target scoring path.
internal sealed class YuanziResolveDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new YuanziResolveHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.AllHandTurnOwnerGift());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class YuanziResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IWeiZiProgramHost)host).YuanziResolve(f, e);
}

// 援资 payoff: the locked after-damage sweep turns into an optional draw prompt
// only when the armed recipient dealt the damage inside the armed turn; the
// estimate stays neutral because the prompt carries its own decline.
internal sealed class YuanziDamageDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziDamageDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new YuanziDamageDrawHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class YuanziDamageDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziDamageDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IWeiZiProgramHost)host).YuanziDamageDraw(f, e);
}

// 烈节 second clause: the already discarded set prices a private source HE
// selection; the estimate credits the control value of stripping the damage
// source because the count is public once the first clause resolved.
internal sealed class LiejieSourceDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LiejieSourceDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new LiejieSourceDiscardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected,
        static (_, context) => context.PublicControlValue(5d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied),
         new ReadCardSet(effect.SourceBind!)];
}

public sealed class LiejieSourceDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LiejieSourceDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IWeiZiProgramHost)host).LiejieSourceDiscard(f, e);
}

internal static class LiejieSourceDiscardContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.LiejieSourceDiscard)) return;
        if (trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || trigger.Effects is not
            [{ Op: SkillProgramEffectOp.SelectOwnedCards } selection,
             { Op: SkillProgramEffectOp.MoveBoundCards } payment,
             { Op: SkillProgramEffectOp.Draw } draw,
             { Op: SkillProgramEffectOp.LiejieSourceDiscard } discard] ||
            selection.Target != SkillProgramEffectTarget.Owner || selection.TargetReference is not null ||
            selection.ResultBind is null || selection.MinimumCards != 1 || selection.MaximumCards != 3 ||
            !selection.Zones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) ||
            selection.NumberExpression is not null || selection.RequireExactCount == true || selection.AllowDecline ||
            payment.SourceBind != selection.ResultBind || payment.ExceptBind is not null ||
            payment.Target != SkillProgramEffectTarget.Owner || payment.Destination != SkillProgramCardDestination.DiscardPile ||
            draw.Target != SkillProgramEffectTarget.Owner || draw.TargetReference is not null ||
            draw.NumberExpression != SkillProgramNumberExpression.BoundCardCount || draw.SourceBind != selection.ResultBind ||
            draw.ResultBind is not null || discard.SourceBind != selection.ResultBind ||
            trigger.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException($"Invalid skill program at {path}: Liejie source discard requires its owner perDamage HE selection, real discard, bound draw and terminal source selection.");
    }
}

// Shared estimate hook: an accepted whole-hand gift to the preparation-phase
// turn owner. The recipient's hand gain is priced through the target draw so
// the shared target scoring decides whether the gift is worth the empty hand.
internal sealed partial class ProgramAiEstimateContext
{
    internal void AllHandTurnOwnerGift()
    {
        _targetDraw += _player.HandCount;
        _otherAdjustment -= _player.HandCount * 7d;
        _otherAdjustment += 4d; // the same-turn damage-draw benefit prior
    }
}
