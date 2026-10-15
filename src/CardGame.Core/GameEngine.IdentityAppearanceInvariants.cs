namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasIdentityAppearanceAttack(IDamageAttempt attack)
    {
        foreach (var appearance in _resolutionStack.OfType<IdentityAppearanceFrame>())
        {
            if (appearance.Receipt.Entry != IdentityAppearanceEntry.DamageDeduction ||
                appearance.Receipt.ParentFrameId != attack.ResolutionId ||
                !IdentityAppearanceAttackMatches(appearance.Receipt, attack)) continue;
            RequireIdentityAppearance(appearance);
            if (appearance.ActiveWindowId is not { } windowId) return true;
            return _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(window =>
                window.Id == windowId && window.IdentityAppearanceFrameId == appearance.Id &&
                window.Window == SkillProgramTriggerWindow.IdentityGeneralAppeared &&
                window.Continuation == ProgramLifecycleContinuation.ResumeIdentityAppearance);
        }
        return false;
    }
}
