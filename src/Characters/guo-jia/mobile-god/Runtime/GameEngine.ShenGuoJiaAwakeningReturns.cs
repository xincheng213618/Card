namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Paid awakening continuations retain the already issued waiver even if a
    // child removes the skill. Initial eligibility still requires the current
    // enabled skill instance through HasShenGuoJiaAwakeningCondition.
    private bool HasShenGuoJiaAwakeningReceipt(ProgramSkillFrame frame) =>
        _contentRegistry.Skills.TryGetValue(frame.SkillId, out var definition) &&
        (definition.Tags & SkillTag.Awakening) != 0 && definition.Program?.GameplayHash == frame.GameplayHash &&
        ShenGuoJiaAwakeningBindings(frame.SkillId).Contains(GetProgramBindingId(frame), StringComparer.Ordinal) &&
        ProgramEventHistory<ShenGuoJiaAwakeningOverrideEvent>().Any(fact =>
            fact.FrameId < frame.Id && fact.Recipient == frame.OwnerSeat && fact.SkillId == frame.SkillId &&
            fact.InstanceId == frame.SkillInstanceId && fact.GameplayHash == frame.GameplayHash);
}
