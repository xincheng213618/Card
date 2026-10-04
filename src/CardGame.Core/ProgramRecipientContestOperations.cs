namespace CardGame.Core;

public enum UniqueHpPeerStage { SelectingCost, CostChildren, DrawChildren, PeerOffer, Complete }
public sealed record UniqueHpPeerInvoice(int ActorSeat, int RequiredCount,
    IReadOnlyList<int> CardIds, IReadOnlyList<CardLocation> SourceLocations,
    long CostBefore, long CostAfter, bool DrawIssued = false,
    long DrawBefore = 0, long DrawAfter = 0, int ActualDrawCount = 0)
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _sourceLocations = Array.AsReadOnly(SourceLocations.ToArray());
    public IReadOnlyList<CardLocation> SourceLocations { get => _sourceLocations; init => _sourceLocations = Array.AsReadOnly(value.ToArray()); }
}
public sealed record UniqueHpPeerReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, long WindowFrameId, long CardUseFrameId, long? CardActionId, ActualUseTargetIdentity TargetIdentity,
    UniqueHpPeerStage Stage, int ChooserSeat, int RequiredCount,
    IReadOnlyList<int> SelectedCardIds, IReadOnlyList<CardLocation> SelectedLocations,
    UniqueHpPeerInvoice? OwnerInvoice = null, UniqueHpPeerInvoice? PeerInvoice = null,
    int? PeerSeat = null, int? PeerHp = null)
{
    private readonly IReadOnlyList<int> _selectedCardIds = Array.AsReadOnly(SelectedCardIds.ToArray());
    public IReadOnlyList<int> SelectedCardIds { get => _selectedCardIds; init => _selectedCardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _selectedLocations = Array.AsReadOnly(SelectedLocations.ToArray());
    public IReadOnlyList<CardLocation> SelectedLocations { get => _selectedLocations; init => _selectedLocations = Array.AsReadOnly(value.ToArray()); }
}
public enum RecipientContestStage { GiftChildren, ChoosingThird, Pindian, ResultReady, SlashIssued, Complete }
public sealed record RecipientContestReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, int RecipientSeat, string ResultBind,
    IReadOnlyList<int> GiftCardIds, long SequenceBefore, long SequenceAfter,
    RecipientContestStage Stage = RecipientContestStage.GiftChildren,
    int? ThirdSeat = null, long? PindianFrameId = null, PindianWinnerSlashReturn? SlashReturn = null)
{
    private readonly IReadOnlyList<int> _giftCardIds = Array.AsReadOnly(GiftCardIds.ToArray());
    public IReadOnlyList<int> GiftCardIds { get => _giftCardIds; init => _giftCardIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record PindianWinnerSlashReturn(long ProgramFrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, string ResultBind, long PindianFrameId, int RecipientSeat, int ThirdSeat,
    int RecipientRank, int ThirdRank, int WinnerSeat, int OriginalTargetSeat, long CardUseFrameId, long CardActionId);

// Private selected/gift entities live only on the owning frame; public facts
// contain counts/sequences and the already revealed Pindian result identity.
public sealed record UniqueHpPeerStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, long WindowFrameId, long CardUseFrameId, long? CardActionId, ActualUseTargetIdentity TargetIdentity) : IGameEvent;
public sealed record UniqueHpTargetAnnouncedEvent(long WindowFrameId, ActualUseTargetIdentity Target) : IGameEvent;
public sealed record UniqueHpPeerCostPaidEvent(long FrameId, int ActorSeat, int RequiredCount, long Before, long After) : IGameEvent;
public sealed record UniqueHpPeerDrawIssuedEvent(long FrameId, int ActorSeat, long Before, long After, int ActualCount) : IGameEvent;
public sealed record UniqueHpPeerOfferedEvent(long FrameId, int? PeerSeat, int? Hp) : IGameEvent;
public sealed record RecipientContestGiftPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, int RecipientSeat, string ResultBind, int CardCount, long Before, long After) : IGameEvent;
public sealed record RecipientContestStartedEvent(long FrameId, int RecipientSeat, int ThirdSeat, long PindianFrameId) : IGameEvent;
public sealed record PindianWinnerSlashIssuedEvent(PindianWinnerSlashReturn Return) : IGameEvent;
public sealed record PindianWinnerSlashReturnedEvent(PindianWinnerSlashReturn Return) : IGameEvent;

internal interface IRecipientContestProgramHost
{
    SkillProgramStepOutcome DiscardDrawPeer(ProgramSkillFrame frame);
    SkillProgramStepOutcome GiveAllHandContest(ProgramSkillFrame frame, int recipientSeat, string resultBind);
    SkillProgramStepOutcome UseContestWinnerSlash(ProgramSkillFrame frame, string resultBind);
}
internal sealed class DiscardDrawAndOfferUniqueHpPeerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardDrawAndOfferUniqueHpPeerHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e, c) => { c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, e.Condition)); c.PublicControlValue(-12d); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"{r.Path}: discard/draw starts with the actual target owner.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition()); RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OtherActualUseTargeted)];
}
internal sealed class GiveAllHandAndStartRecipientPindianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveAllHandAndStartRecipientPindianHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (e, c) => c.AllHandRecipientContest());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"{r.Path}: whole-hand gift requires its original other recipient.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving), new CreatePindianResult(e.ResultBind!)];
}
internal sealed class UsePindianWinnerSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.UsePindianWinnerSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new UsePindianWinnerSlashHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartPindian, static (_, c) => c.PublicControlValue(4d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"{r.Path}: winner Slash must retain the original program owner.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadPindianResult(e.SourceBind!)];
}
public sealed class DiscardDrawAndOfferUniqueHpPeerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IRecipientContestProgramHost)host).DiscardDrawPeer(f);
}
public sealed class GiveAllHandAndStartRecipientPindianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IRecipientContestProgramHost)host).GiveAllHandContest(f, seat, e.ResultBind!);
}
public sealed class UsePindianWinnerSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.UsePindianWinnerSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IRecipientContestProgramHost)host).UseContestWinnerSlash(f, e.SourceBind!);
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void AllHandRecipientContest() { _targetDraw += _player.HandCount; _otherAdjustment -= _player.HandCount * 7d; _otherAdjustment += 4d; }
}
internal static class RecipientContestComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int cards, int targets, SkillProgramTargetKind kind, int? phaseUses)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer) &&
            (window != SkillProgramTriggerWindow.OtherActualUseTargeted || effects is not [{ Op: SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer }]))
            throw new InvalidOperationException($"{path}: unique-HP discard/draw is one actual-target instruction.");
        if (effects.Any(e => e.Op is SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian or SkillProgramEffectOp.UsePindianWinnerSlash) &&
            (window is not null || cards != 0 || targets != 1 || kind != SkillProgramTargetKind.OtherLiving || phaseUses != 1 ||
             effects is not [{ Op: SkillProgramEffectOp.GiveAllHandAndStartRecipientPindian, ResultBind: { } bind },
                 { Op: SkillProgramEffectOp.UsePindianWinnerSlash, SourceBind: { } read }] || bind != read))
            throw new InvalidOperationException($"{path}: recipient contest requires the exact two-node, zero-card, one-other-target actual-Play contract.");
    }
}
