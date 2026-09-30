namespace CardGame.Core;

internal interface IHandControlProgramHost
{
    SkillProgramStepOutcome ExecuteHandControl(SkillProgramEffect effect, ProgramSkillFrame frame);
}

/// <summary>A serializable, instruction-owned selection over public counts and physical owned cards.</summary>
public sealed record ProgramHandControlDraft(
    SkillProgramEffectOp Operation,
    string Stage,
    int Maximum,
    IReadOnlyList<int> ParticipantSeats,
    int ParticipantIndex = 0,
    int? CardOwnerSeat = null,
    IReadOnlyList<int>? CandidateCardIds = null,
    IReadOnlyList<CardLocation>? CandidateLocations = null,
    IReadOnlyList<int>? RevealedCardIds = null,
    int ObtainedCount = 0);

public sealed record ProgramHandCountInterventionEvent(long ResolutionId, string SkillId,
    int OwnerSeat, int TargetSeat, string Intervention) : IGameEvent;
public sealed record ProgramHandColorDiscardEvent(long ResolutionId, string SkillId,
    int OwnerSeat, bool IsRed, IReadOnlyList<int> CardIds) : IGameEvent;
public sealed record ProgramParticipantCardPlacedOnTopEvent(long ResolutionId, string SkillId,
    int OwnerSeat, int ParticipantSeat, CardZoneKind SourceZone) : IGameEvent;

internal abstract class HandControlDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "useMaximumHp", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var maximumHp = r.Has("useMaximumHp") && r.RequiredBool("useMaximumHp");
        if (r.Has("amount") || r.Has("useMaximumHp") && Op != SkillProgramEffectOp.DrawThenPutOwnedCardOnTopParticipants)
            throw new InvalidOperationException($"Invalid hand-control parameters at {r.Path}.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), booleanValue: maximumHp);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
internal sealed class ChooseHandCountInterventionDescriptor : HandControlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHandCountIntervention;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseHandCountInterventionHandler();
}
internal sealed class RevealHandColorDiscardAndTakeDescriptor : HandControlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealHandColorDiscardAndTake;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealHandColorDiscardAndTakeHandler();
}
internal sealed class DrawThenPutOwnedCardOnTopParticipantsDescriptor : HandControlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenPutOwnedCardOnTopParticipants;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawThenPutOwnedCardOnTopParticipantsHandler();
}
internal sealed class DrawTurnOwnerThenDiscardMaximumHandForDodgeDescriptor : HandControlDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawTurnOwnerThenDiscardMaximumHandForDodgeHandler();
}
internal sealed class LoseOwnerSkillsAndGrantDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseOwnerSkillsAndGrant;
    public override ISkillProgramEffectHandler Handler { get; } = new LoseOwnerSkillsAndGrantHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnSkills, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "skillIds", "sourceBind", "condition");
        var skills = r.RequiredIdentifierArray("skillIds");
        if (skills.Count == 0 || skills.Distinct(StringComparer.Ordinal).Count() != skills.Count)
            throw new InvalidOperationException("Skill replacement requires distinct skills to lose.");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            skillIds: skills, sourceBind: r.RequiredIdentifier("sourceBind"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
public abstract class HandControlHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => ((IHandControlProgramHost)host).ExecuteHandControl(effect, frame);
}
public sealed class ChooseHandCountInterventionHandler : HandControlHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseHandCountIntervention; }
public sealed class RevealHandColorDiscardAndTakeHandler : HandControlHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealHandColorDiscardAndTake; }
public sealed class DrawThenPutOwnedCardOnTopParticipantsHandler : HandControlHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenPutOwnedCardOnTopParticipants; }
public sealed class LoseOwnerSkillsAndGrantHandler : HandControlHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseOwnerSkillsAndGrant; }
public sealed class DrawTurnOwnerThenDiscardMaximumHandForDodgeHandler : HandControlHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge; }
