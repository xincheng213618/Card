namespace CardGame.Core;

public sealed record ProgramDyingOwnedCardReceipt(int InstructionIndex, long DyingFrameId, long EntryFrameId,
    int VictimSeat, string ResultBind, bool NonBasic, int? PaidCardId, CardLocation? SourceLocation, long MovementSequenceBefore);
public sealed record ProgramDyingCardSelectionResolvedEvent(long FrameId, long DyingFrameId, int OwnerSeat,
    int VictimSeat, bool NonBasic) : IGameEvent;

internal interface IDyingOwnedCardProgramHost
{
    SkillProgramStepOutcome SelectDyingOwnedCard(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class SelectDyingOwnedCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectDyingOwnedCard;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectDyingOwnedCardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DyingRescue, static (e, c) => c.SelectDyingNonBasicCard(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "resultBind", "condition");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Distinct().Count() != zones.Count || zones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)))
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: dying card selection requires distinct HEJ zones.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), zones: zones, resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering), new SelectSingleTarget(), new CaptureSourceCard(effect.ResultBind!, 1)];
}
public sealed class SelectDyingOwnedCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectDyingOwnedCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IDyingOwnedCardProgramHost)host).SelectDyingOwnedCard(f, e);
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void SelectDyingNonBasicCard(SkillProgramEffect effect)
    {
        // The exact public victim supplies role attitudes; hidden cards are never priced by identity.
        _bindings[effect.ResultBind!] = UnknownCards(0d, ownerHeld: false);
        if (_publicContext.SelectedTarget is not { Hp: <= 0 } victim) { _otherAdjustment -= 1000d; return; }
        if (victim.Seat == _player.Seat) _ownerRecovery += 0.65d;
        else _targetRecovery += 0.65d;
    }
}
