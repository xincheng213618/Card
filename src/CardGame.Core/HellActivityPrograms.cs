namespace CardGame.Core;

internal interface IHellActivityProgramHost
{
    SkillProgramStepOutcome ExecuteHellActivity(ProgramSkillFrame frame, SkillProgramEffectOp op);
}
internal abstract class HellActivityOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new HellActivityEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
}
internal sealed class GainHellTurnSlashDescriptor : HellActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GainHellTurnSlash;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class DrawHellChongshengRefillDescriptor : HellActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawHellChongshengRefill;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.SelfDyingResponse)];
}
internal sealed class RecoverHellChongshengDescriptor : HellActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverHellChongsheng;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.SelfDyingResponse)];
}
internal sealed class HellActivityEffectHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        h.GetCapability<IHellActivityProgramHost>().ExecuteHellActivity(f, Op);
}
public sealed record HellChongshengRefillIssuedEvent(long ProgramFrameId, int OwnerSeat, int RequestedCount) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HellActivityProgramHost(GameEngine engine) : IHellActivityProgramHost
    {
        public SkillProgramStepOutcome ExecuteHellActivity(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.ExecuteHellActivity(f, op);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40110)]
    private bool AllowsHellActivityTier(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId is not ("hell:player-tier" or "hell:boss-tier")) return true;
        if (_modeDefinition.HellActivity is not { } hell) return false;
        if (gate.SkillId == "hell:player-tier") return !IsHellNpc(gate.Owner);
        if (!IsHellNpc(gate.Owner)) return false;
        return gate.Candidate.BindingId switch
        {
            "arrival-equipment" => hell.BossTier >= 2,
            "arrival-assault" => hell.BossTier == 5,
            "turn-slash" => hell.BossTier >= 3,
            _ => false
        };
    }
    private SkillProgramStepOutcome ExecuteHellActivity(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var owner = _players[frame.OwnerSeat];
        switch (operation)
        {
            case SkillProgramEffectOp.GainHellTurnSlash:
                if (_modeDefinition.HellActivity is not { BossTier: >= 3 } || !IsHellNpc(owner) ||
                    frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
                    _turnProgression.OwnerSeat != owner.Seat) throw new InvalidOperationException("Hell tier Slash lost its actual turn owner.");
                var slash = _cardZones.CardsAt(CardLocation.DrawPile).FirstOrDefault(card => card.Kind == CardKind.Slash) ??
                    _cardZones.CardsAt(CardLocation.DiscardPile).FirstOrDefault(card => card.Kind == CardKind.Slash);
                if (slash is not null) MoveCard(slash, _cardZones.GetLocation(slash.Id), CardLocation.Hand(owner.Seat), new("hell.tier.obtain-slash"));
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.DrawHellChongshengRefill:
                var count = Math.Max(0, Math.Min(owner.MaxHp, 5) - GetHand(owner).Count);
                AdvanceEventRulesAndQueueFact(new HellChongshengRefillIssuedEvent(frame.Id, owner.Seat, count));
                return BeginOperationProgramDraw(frame, owner.Seat, count, new("hell.chongsheng.refill"));
            case SkillProgramEffectOp.RecoverHellChongsheng:
                RecoverProgramTargetTo(frame.Id, owner.Seat, owner.Seat, SkillProgramNumberExpression.IntegerConstant, owner.MaxHp, true);
                return SkillProgramStepOutcome.Continue;
            default: throw new InvalidOperationException("Unknown Hell activity operation.");
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawHellChongshengRefill)]
    private void ReturnHellChongshengDraw(ProgramSkillFrame frame, NativeDrawOutcome outcome)
    {
        var issued = ProgramEventHistory<HellChongshengRefillIssuedEvent>().Single(e => e.ProgramFrameId == frame.Id);
        if (issued.OwnerSeat != frame.OwnerSeat || outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != issued.RequestedCount)
            throw new InvalidOperationException("Chongsheng refill changed its exact paid draw request.");
        CompleteProgramDrawCards(frame.Id, frame.OwnerSeat, null, SkillProgramCardSetVisibility.Private, new("hell.chongsheng.refill"), 0, outcome.ActualDrawIds);
        AdvanceRuntimeProgram(frame.Id);
    }
}
