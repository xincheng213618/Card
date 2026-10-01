namespace CardGame.Core;

/// <summary>Public facts shared by action generation, input validation and AI.</summary>
internal readonly record struct ProgramLegalityParticipant(int Seat, int HandCount, bool ProhibitsOtherPindianTarget = false);

/// <summary>
/// Static operation prerequisites. They never read physical hand identities or
/// replace the operation's runtime ownership, movement or continuation checks.
/// </summary>
internal sealed record ProgramOperationLegalityPolicy(
    bool RequiresOwnerHand = false,
    bool RequiresTargetHand = false,
    bool ExcludesOwnerAsTarget = false,
    bool RequiresPindianTarget = false)
{
    internal static ProgramOperationLegalityPolicy None { get; } = new();
    internal static ProgramOperationLegalityPolicy HandContest { get; } = new(
        RequiresOwnerHand: true, RequiresTargetHand: true, RequiresPindianTarget: true);
    internal static ProgramOperationLegalityPolicy OtherRecipient { get; } = new(
        ExcludesOwnerAsTarget: true);

    internal bool CanStart(ProgramLegalityParticipant owner) =>
        !RequiresOwnerHand || owner.HandCount > 0;

    internal bool CanSelectTarget(ProgramLegalityParticipant owner, ProgramLegalityParticipant target) =>
        (!RequiresTargetHand || target.HandCount > 0) &&
        (!ExcludesOwnerAsTarget || target.Seat != owner.Seat) &&
        (!RequiresPindianTarget || owner.Seat == target.Seat || !target.ProhibitsOtherPindianTarget);

    // An unknown AI recipient remains a possible choice. A known recipient uses
    // exactly the same public prerequisite as the action and submitted input.
    internal bool CanEstimate(PlayerSkillContext owner, PlayerSkillContext? target) =>
        CanStart(new(owner.Seat, owner.HandCount)) &&
        (target is null || CanSelectTarget(new(owner.Seat, owner.HandCount), new(target.Seat, target.HandCount)));
}
