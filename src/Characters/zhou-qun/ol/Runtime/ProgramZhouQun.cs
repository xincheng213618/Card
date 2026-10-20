using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZhouQunStage { ChooseHandOwner, ChooseShownHand, RevealChildren, GainChildren, LeaseChildren, ViewChildren, ExchangeHand, ExchangeTop, ExchangeChildren, ChooseWeatherCard, ChooseWeatherRecipient, GrantChildren, HpIssuing, HpChildren, Judgment, JudgmentChildren, CleanupChildren }
public sealed record ZhouQunShownCard(int CardId, int OwnerSeat, CardKind Kind, Suit Suit, int Rank, int NameLength);
public sealed record ZhouQunReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner, ZhouQunStage Stage)
{
    public bool Paid { get; init; }
    public int Peer { get; init; } = -1;
    public int Cursor { get; init; }
    public int Step { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool Repeat { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public int? SlashRank { get; init; }
    public long JudgmentId { get; init; }
    public int? JudgmentCardId { get; init; }
    public int? JudgmentRank { get; init; }
    public int ChosenCard { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhouQunShownCard? Shown { get; init; }
    private IReadOnlyList<int> _revealed = Array.AsReadOnly(Array.Empty<int>()), _viewed = Array.AsReadOnly(Array.Empty<int>()), _hand = Array.AsReadOnly(Array.Empty<int>()), _top = Array.AsReadOnly(Array.Empty<int>()), _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Revealed { get => _revealed; init => _revealed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Viewed { get => _viewed; init => _viewed = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedHand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedTop { get => _top; init => _top = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<ZhouQunShownCard> _shownCards = Array.AsReadOnly(Array.Empty<ZhouQunShownCard>());
    public IReadOnlyList<ZhouQunShownCard> ShownCards { get => _shownCards; init => _shownCards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhouQunReceipt? ZhouQun { get; init; } }

public sealed record ZhouQunStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record ChenshuoHandShownEvent(long FrameId, int Index, ZhouQunShownCard Card) : IGameEvent;
public sealed record ChenshuoTopRevealedEvent(long FrameId, int Index, int CardId, CardKind Kind, Suit Suit, int Rank, int NameLength, bool Matched) : IGameEvent;
public sealed record ZhouQunMoveIssuedEvent(long FrameId, int Step, ZhouQunStage Stage, long Before, long After) : IGameEvent;
public sealed record ZhouQunMovedEvent(long FrameId, int Step, long BatchId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record TianhouInvokedEvent(long FrameId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record TianhouViewedEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record TianhouCardShownEvent(long FrameId, int OwnerSeat, int CardId, Suit Suit) : IGameEvent;
public sealed record TianhouWeatherGrantedEvent(long FrameId, CardConversionSource Source, int RecipientSeat, string SkillId, string GrantId, string SkillInstanceId, string GrantSourceId) : IGameEvent;
public sealed record TianhouWeatherExpiredEvent(long FrameId, long OriginalFrameId, int RecipientSeat, string GrantId) : IGameEvent;
public sealed record ZhouQunHpTargetEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record ZhouQunHpPaidEvent(long FrameId, int Index, int TargetSeat, int Amount, int RemainingHp) : IGameEvent;
public sealed record NingwuJudgmentIssuedEvent(long FrameId, long JudgmentId, long UseId, long ActionId, int ActorSeat, int TargetSeat, int? SlashRank) : IGameEvent;
public sealed record NingwuJudgmentReturnedEvent(long FrameId, long JudgmentId, int? CardId, int? Rank) : IGameEvent;
public sealed record NingwuUseNullifiedEvent(long FrameId, long UseId, long ActionId, long JudgmentId, int ActorSeat, int TargetSeat, int SlashRank, int JudgmentRank) : IGameEvent;
public sealed record ZhouQunCompletedEvent(long FrameId, bool Applied) : IGameEvent;

internal interface IZhouQunHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ZhouQunDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(ZhouQunComposition.Window(op))];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new ZhouQunHandler(op);
}
internal sealed class ChenshuoDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveChenshuoRepeatedReveal){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
internal sealed class TianhouDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
internal sealed class LieshuDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveLieshuEndingLoss){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
internal sealed class NingwuDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
internal sealed class ZhouyuNeighborsDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveZhouyuThunderNeighbors){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
internal sealed class YanshuangDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveYanshuangEndingLoss){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
internal sealed class ZhouQunHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IZhouQunHost>().Resolve(f, op);
}
internal static class ZhouQunComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveChenshuoRepeatedReveal or SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant or SkillProgramEffectOp.ResolveLieshuEndingLoss or SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment or SkillProgramEffectOp.ResolveZhouyuThunderNeighbors or SkillProgramEffectOp.ResolveYanshuangEndingLoss;
    internal static SkillProgramTriggerWindow Window(SkillProgramEffectOp op) => op switch
    { SkillProgramEffectOp.ResolveChenshuoRepeatedReveal => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment => SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ResolveZhouyuThunderNeighbors => SkillProgramTriggerWindow.AfterDamageApplied, _ => SkillProgramTriggerWindow.TurnEnding };
    [ProgramCatalogValidation(750)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Triggers.Any(t => t.Effects.Any(e => Handles(e.Op))) && p.Id != "ol:zhouyu") return;
        var expected = p.Id switch { "ol:chenshuo" => SkillProgramEffectOp.ResolveChenshuoRepeatedReveal, "ol:tianhou" => SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant, "ol:lieshu" => SkillProgramEffectOp.ResolveLieshuEndingLoss, "ol:ningwu" => SkillProgramEffectOp.ResolveNingwuSingletonSlashJudgment, "ol:zhouyu" => SkillProgramEffectOp.ResolveZhouyuThunderNeighbors, "ol:yanshuang" => SkillProgramEffectOp.ResolveYanshuangEndingLoss, _ => throw new InvalidOperationException(path + ": unexpected weather program.") };
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Modifiers.Count != 0 || p.DamageModifiers.Count != 0 || p.Contributions.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.Triggers.Count != (p.Id == "ol:zhouyu" ? 2 : 1)) throw new InvalidOperationException(path + ": Zhou Qun requires the complete public current main and weather rules.");
        var t = p.Triggers.SingleOrDefault(t => t.Effects.Any(e => e.Op == expected));
        if (t is null || t.Window != Window(expected) || t.Optional != (p.Id == "ol:chenshuo") || t.UsageLimit is not null || t.UsageScope is not null || t.IncludeResponseUses || t.SingleActionInstance || t.Effects is not [var effect] || effect.Op != expected || effect.Target != SkillProgramEffectTarget.Owner || effect.Condition.Kind != SkillProgramConditionKind.Always) throw new InvalidOperationException(path + ": weather operation lost its exact native window or mandatory scope.");
        if (p.Id == "ol:zhouyu" && !p.Triggers.Any(x => x.Window == SkillProgramTriggerWindow.BeforeDamageApplied && !x.Optional && x.Effects is [{ Op: SkillProgramEffectOp.PreventCurrentDamage }])) throw new InvalidOperationException(path + ": Zhouyu must retain its independent real fire prevention.");
    }
}
