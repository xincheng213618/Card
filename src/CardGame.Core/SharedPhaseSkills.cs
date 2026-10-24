using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramSharedActivationStartedEvent(long FrameId, CardConversionSource Source,
    int ActorSeat, SkillGrantPhaseExpiry Phase) : IGameEvent;
public sealed record ProgramRandomPhaseSkillsOfferedEvent(long FrameId, int InstructionIndex,
    int ChooserSeat, int RecipientSeat, SkillGrantPhaseExpiry Phase, IReadOnlyList<string> SkillIds) : IGameEvent;
public sealed record ProgramRandomPhaseSkillChosenEvent(long FrameId, int ChooserSeat, int RecipientSeat,
    string SkillId, SkillGrantPhaseExpiry Phase) : IGameEvent;
public sealed record ProgramRandomPhaseSkillOffer(int InstructionIndex, int RecipientSeat,
    SkillGrantPhaseExpiry Phase, IReadOnlyList<string> SkillIds)
{
    private readonly IReadOnlyList<string> _skillIds = Array.AsReadOnly(SkillIds.ToArray());
    public IReadOnlyList<string> SkillIds { get => _skillIds; init => _skillIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ActivatingSeat { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SkillGrantPhaseExpiry? ActivationPhase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProgramRandomPhaseSkillOffer? RandomPhaseSkillOffer { get; init; }
}

internal static class SharedPhaseSkillContract
{
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (!activation.OtherCharactersOnly)
        {
            if (activation.Effects.Any(effect => effect.Target == SkillProgramEffectTarget.Activator))
                throw new InvalidOperationException(path + ": activator targets require a shared activation.");
            return;
        }
        if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 0 || activation.MaxTargets != 0 ||
            activation.UsesPerPhase is not > 0 || activation.UsesPerTurn is not null || activation.UsesPerGame is not null ||
            activation.TargetKind != SkillProgramTargetKind.AnyLiving || activation.CardCountExpression is not null ||
            activation.CardKinds.Count != 0 || activation.CardSuits.Count != 0 || activation.CardCategories.Count != 0 ||
            activation.EquipmentSlots.Count != 0 || activation.CategoryTargetLedgerId is not null || activation.TargetPhaseLedgerId is not null ||
            activation.SelectedCardsSameSuit || activation.SelectedCardsDistinctSuits || activation.TargetRequiresEmptyEquipmentSlot ||
            activation.UsageGroup != activation.Id ||
            activation.MarkerCost is not null || activation.ContinueAfterOwnerDeath || activation.Condition.Kind != SkillProgramConditionKind.Always ||
            activation.Effects.Any(effect => effect.Target != SkillProgramEffectTarget.Activator ||
                effect.NumberExpression is not null || effect.TargetReference is not null ||
                effect.Condition.Kind != SkillProgramConditionKind.Always ||
                effect.Op is not (SkillProgramEffectOp.LoseHp or SkillProgramEffectOp.Draw or SkillProgramEffectOp.OfferRandomPlayPhaseSkill or
                    SkillProgramEffectOp.ResolveMobileShiXinXianYingJiejie)))
            throw new InvalidOperationException(path + ": shared activations require zero selected input, a phase quota and unconditional activator effects.");
    }

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(effect => effect.Target == SkillProgramEffectTarget.Activator))
            throw new InvalidOperationException(path + ": trigger effects cannot refer to a shared activator.");
        if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.OfferRandomPlayPhaseSkill) &&
            (trigger.Window != SkillProgramTriggerWindow.PlayPhaseStarting || trigger.Subject != SkillProgramTriggerSubject.Owner ||
             trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own))
            throw new InvalidOperationException(path + ": a random phase skill offer requires its owner's actual Play-phase start.");
    }
}

internal interface IRandomPhaseSkillHost
{
    SkillProgramStepOutcome Offer(ProgramSkillFrame frame, SkillProgramEffect effect, int recipientSeat);
}
internal sealed class OfferRandomPlayPhaseSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferRandomPlayPhaseSkill;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferRandomPlayPhaseSkillHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnSkills,
        static (effect, context) => context.GrantTurnSkills(effect));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "skillIds", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        var count = reader.RequiredInt("amount");
        var ids = reader.RequiredIdentifierArray("skillIds");
        if (target is not (SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.Activator) || count < 1 || count > ids.Count)
            throw new InvalidOperationException(reader.Path + ": random offers require an owner/activator and a bounded distinct pool.");
        var effect = new SkillProgramEffect(Op, target, count, reader.Condition(), skillIds: ids);
        RequireAlways(effect, reader.Path);
        return effect;
    }
}
public sealed class OfferRandomPlayPhaseSkillHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferRandomPlayPhaseSkill;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IRandomPhaseSkillHost>().Offer(frame, effect, targetSeat);
}
