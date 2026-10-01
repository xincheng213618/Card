namespace CardGame.Core;

internal interface INamedDefenseAndPublicDraftHost
{
    SkillProgramStepOutcome BeginNamedTargetDefense(ProgramSkillFrame frame, string ledgerId, IReadOnlyList<CardKind> names);
    SkillProgramStepOutcome BeginLowHandPublicDraft(ProgramSkillFrame frame);
}

internal sealed class DeclareNameForTargetDefenseProgramDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DeclareNameForTargetDefense;
    public override ISkillProgramEffectHandler Handler { get; } = new DeclareNameForTargetDefenseProgramHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "cardKinds", "condition");
        var names = r.RequiredEnumArray<CardKind>("cardKinds");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || names.Count == 0 ||
            names.Any(kind => CardUseCategoryCatalog.Get(kind) == CardUseCategories.Equipment) ||
            names.Any(kind => kind is CardKind.FireSlash or CardKind.ThunderSlash))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: declare canonical non-equipment card names for the target owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(),
            stateId: r.RequiredIdentifier("stateId"), cardKinds: names);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
            new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Target, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
}
public sealed class DeclareNameForTargetDefenseProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DeclareNameForTargetDefense;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((INamedDefenseAndPublicDraftHost)host).BeginNamedTargetDefense(frame, effect.StateId!, effect.CardKinds);
}
internal sealed class DrawAndDraftLowHandPopulationProgramDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAndDraftLowHandPopulation;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawAndDraftLowHandPopulationProgramHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: a hand draft belongs to its owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class DrawAndDraftLowHandPopulationProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAndDraftLowHandPopulation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((INamedDefenseAndPublicDraftHost)host).BeginLowHandPublicDraft(frame);
}

public sealed record ProgramNamedTargetDefense(string LedgerId, CardKind? Name = null, int Stage = 0);
public sealed record ProgramPublicHandDraft(IReadOnlyList<int> RecipientSeats, IReadOnlyList<int> CardIds, int Stage = 0, int Cursor = 0);
public sealed record ProgramDefenseNameDeclaredEvent(long FrameId, string SkillId, int OwnerSeat, CardKind Name) : IGameEvent;
public sealed record ProgramHandDraftRevealedEvent(long FrameId, string SkillId, int OwnerSeat, IReadOnlyList<int> RecipientSeats, IReadOnlyList<int> CardIds) : IGameEvent;
public sealed record ProgramHandDraftCardObtainedEvent(long FrameId, string SkillId, int OwnerSeat, int RecipientSeat, int CardId) : IGameEvent;
