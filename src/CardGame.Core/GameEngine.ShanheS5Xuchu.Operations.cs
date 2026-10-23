namespace CardGame.Core;

internal interface IShanheS5LuoyiProgramHost
{
    void ResolveShanheS5Luoyi(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class ResolveShanheS5LuoyiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Luoyi;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveShanheS5LuoyiHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.DrawPlan | ProgramContextCapability.TurnEffects;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal,
        static (effect, context) => context.ShanheS5Luoyi(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stage", "sourceBind", "resultBind", "condition");
        var stage = r.RequiredIdentifier("stage");
        if (stage is not ("reveal" or "apply") ||
            stage == "reveal" && (r.Has("sourceBind") || !r.Has("resultBind")) ||
            stage == "apply" && (r.Has("resultBind") || !r.Has("sourceBind")))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: Luoyi requires reveal/resultBind or apply/sourceBind.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            stateId: stage, sourceBind: r.OptionalIdentifier("sourceBind"), resultBind: r.OptionalIdentifier("resultBind"));
        if (stage == "reveal") RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        effect.StateId == "reveal"
            ? [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting), new CreateCardSet(effect.ResultBind!, int.MaxValue, true)]
            : [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting), new ReadCardSet(effect.SourceBind!)];
}

public sealed class ResolveShanheS5LuoyiHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShanheS5Luoyi;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IShanheS5LuoyiProgramHost>().ResolveShanheS5Luoyi(frame, effect);
        return SkillProgramStepOutcome.Continue;
    }
}

internal static class ShanheS5LuoyiComposition
{
    internal const string Replace = "skip-draw";
    internal const string Normal = "normal-draw";

    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, SkillProgramDrawPhaseMode drawMode)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.ResolveShanheS5Luoyi)) return;
        if (window != SkillProgramTriggerWindow.DrawPhaseStarting || drawMode != SkillProgramDrawPhaseMode.Additive ||
            effects is not [var reveal, var choice, var apply, var filter, var gain, var rest, var decline] ||
            reveal.Op != SkillProgramEffectOp.ResolveShanheS5Luoyi || reveal.StateId != "reveal" ||
            choice.Op != SkillProgramEffectOp.ChooseOption || choice.ChooserRef is not null and not { Kind: ProgramParticipantRef.Owner } ||
            !choice.Options.Select(o => o.Id).SequenceEqual([Replace, Normal]) ||
            choice.Options.Any(o => o.Condition.Kind != SkillProgramConditionKind.Always || o.RequiredTargetKind is not null) ||
            apply.Op != SkillProgramEffectOp.ResolveShanheS5Luoyi || apply.StateId != "apply" || apply.SourceBind != reveal.ResultBind ||
            !Branch(apply, choice.ResultBind, Replace) ||
            filter.Op != SkillProgramEffectOp.FilterBoundCards || filter.SourceBind != reveal.ResultBind ||
            !filter.CardCategories.SequenceEqual([SkillProgramCardCategory.Basic]) || !filter.EquipmentSlots.SequenceEqual([EquipmentSlot.Weapon]) ||
            !filter.CardKinds.SequenceEqual([CardKind.Duel]) || filter.Suits.Count != 0 || filter.TargetReference is not null ||
            gain.Op != SkillProgramEffectOp.MoveBoundCards || gain.SourceBind != filter.ResultBind || gain.ExceptBind is not null ||
            gain.Destination != SkillProgramCardDestination.OwnerHand || !Branch(gain, choice.ResultBind, Replace) ||
            rest.Op != SkillProgramEffectOp.MoveBoundCards || rest.SourceBind != reveal.ResultBind || rest.ExceptBind != filter.ResultBind ||
            rest.Destination != SkillProgramCardDestination.DiscardPile || !Branch(rest, choice.ResultBind, Replace) ||
            decline.Op != SkillProgramEffectOp.MoveBoundCards || decline.SourceBind != reveal.ResultBind || decline.ExceptBind is not null ||
            decline.Destination != SkillProgramCardDestination.DiscardPile || !Branch(decline, choice.ResultBind, Normal) ||
            new[] { reveal, choice, filter }.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException($"Invalid S5 Luoyi composition at {path}: a public HP reveal must offer normal draw or actual draw skip with the exact basic/weapon/Duel partition.");
    }

    private static bool Branch(SkillProgramEffect e, string? bind, string option) =>
        e.Condition.Kind == SkillProgramConditionKind.ChoiceIs && e.Condition.SourceBind == bind && e.Condition.OptionId == option;
}

// Public HP and scalar producer identities only; the existing reveal event owns the public card collection.
public sealed record ShanheS5LuoyiFrozenEvent(long FrameId, int InstructionIndex, string SkillId, string SkillInstanceId,
    string GameplayHash, string Bind, long DrawPlanFrameId, int OwnerSeat, int ActualTurnNumber, int X) : IGameEvent;

internal sealed partial class ProgramAiEstimateContext
{
    internal void ShanheS5Luoyi(SkillProgramEffect effect)
    {
        // Use only public HP and the normal draw budget; unseen deck faces retain the shared reveal prior.
        var hp = Math.Max(0, _player.Hp);
        if (effect.StateId == "reveal")
            Reveal(new(SkillProgramEffectOp.RevealTopCards, SkillProgramEffectTarget.Owner, hp, effect.Condition,
                resultBind: effect.ResultBind, visibility: SkillProgramCardSetVisibility.Public));
        else
        {
            _ownerDraw -= Math.Max(0, _publicContext.NormalDrawCount);
            GrantTurnCardDamageModifier(new(SkillProgramEffectOp.GrantTurnCardDamageModifier,
                SkillProgramEffectTarget.Owner, hp, effect.Condition,
                damageModifierExpiration: SkillProgramDamageModifierExpiration.NextOwnerTurnStart));
        }
    }
}
