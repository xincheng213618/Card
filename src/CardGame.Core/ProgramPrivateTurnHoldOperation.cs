using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record PrivateTurnHoldIdentity(long HoldId, string SkillId, string SkillInstanceId, string SourceId, int ExpiresTurnNumber);
public sealed record PrivateTurnHoldSnapshot(long HoldId, int OwnerSeat, string SkillId, string SkillInstanceId, string SourceId, int ExpiresTurnNumber, int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CardSnapshot>? Cards = null);
public sealed record ProgramPrivateTurnHoldDraft(CardLocation Location, bool Paid);
internal interface IPrivateTurnHoldProgramHost { SkillProgramStepOutcome HoldOwnerHandUntilTurnEnd(ProgramSkillFrame frame); }
internal sealed class HoldOwnerHandUntilTurnEndDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd;
    public override ISkillProgramEffectHandler Handler { get; } = new HoldOwnerHandUntilTurnEndHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e,c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e,r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardEffectBeforeApply)];
}
public sealed class HoldOwnerHandUntilTurnEndHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IPrivateTurnHoldProgramHost)host).HoldOwnerHandUntilTurnEnd(f);
}
