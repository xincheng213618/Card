namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record ProgramNestedDamage(
        AttackResolution Child,
        AttackResolution OuterAttack,
        DamageTriggerResolution OuterWindow,
        ProgramNestedDamage? Parent);

    private ProgramNestedDamage? _pendingProgramNestedDamage;

    private bool TrySuspendProgramDamageParent(ProgramSkillFrame frame, AttackResolution child)
    {
        if (frame.WindowContext is not
                { Window: SkillProgramTriggerWindow.AfterDamageApplied, ParentFrameId: var parentId } ||
            _pendingDamageTrigger is not { } outer || outer.FrameId != parentId ||
            _pendingAttack is not { } outerAttack || !ReferenceEquals(outer.Attack, outerAttack))
            return false;

        _pendingProgramNestedDamage = new(child, outerAttack, outer,
            _pendingProgramNestedDamage);
        _pendingDamageTrigger = null;
        _pendingAttack = null;
        return true;
    }

    private void RestoreProgramDamageParent(AttackResolution child)
    {
        if (_pendingProgramNestedDamage is not { } nested ||
            !ReferenceEquals(nested.Child, child))
            return;
        if (_pendingDamageTrigger is not null || _pendingAttack is not null)
            throw new InvalidOperationException("A nested program damage child retained a live window or attack.");
        _pendingProgramNestedDamage = nested.Parent;
        _pendingDamageTrigger = nested.OuterWindow;
        _pendingAttack = nested.OuterAttack;
    }
}
