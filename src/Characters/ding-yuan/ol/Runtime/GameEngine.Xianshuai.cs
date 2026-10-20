using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksRoundFirstDamage => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.QualifyFirstRoundDamage) == true;

    private RoundFirstSourcedDamageEvent? FirstRoundDamageFor(ProgramSkillWindowContext context) =>
        context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.DamageFrameId is { } id &&
        _resolutionStack.OfType<DamageTriggerWindowFrame>().Any(window => window.Id == context.ParentFrameId && window.ParentFrameId == id)
            ? CompleteProgramEventHistory().OfType<RoundFirstSourcedDamageEvent>().SingleOrDefault(fact =>
                fact.RoundNumber == _roundNumber && fact.DamageFrameId == id && fact.SourceSeat == context.SourceSeat &&
                fact.TargetSeat == context.TargetSeat && fact.Amount == context.Amount) : null;
    private void QualifyFirstRoundDamage(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || frame.WindowContext is not { } context || FirstRoundDamageFor(context) is not { } first ||
            !CanOfferDingYuan(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId!, frame.SkillInstanceId, frame.GameplayHash, 0),
                GetProgramTrigger(frame), context))
            throw new InvalidOperationException("First-round damage qualification requires the exact applied native damage and source instance.");
        AdvanceEventRulesAndQueueFact(new FirstRoundDamageQualifiedEvent(frame.Id, first.DamageFrameId,
            first.RoundNumber, frame.OwnerSeat, frame.SkillInstanceId, frame.TriggerId!));
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26020)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26020)]
    private bool CanContinueIssuedFirstRoundDamage(ProgramSkillFrame frame) => frame.SkillId == "ol:xianshuai" &&
        CompleteProgramEventHistory().OfType<FirstRoundDamageQualifiedEvent>().Any(fact =>
            fact.FrameId == frame.Id && fact.OwnerSeat == frame.OwnerSeat && fact.SkillInstanceId == frame.SkillInstanceId &&
            fact.BindingId == frame.TriggerId && fact.DamageFrameId == frame.WindowContext?.DamageFrameId);
}
