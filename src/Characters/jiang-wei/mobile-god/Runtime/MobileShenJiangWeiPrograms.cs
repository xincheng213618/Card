using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IMobileJiangWeiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MobileJiangWeiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MobileJiangWeiHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveMobileJiangWeiHuitianDeath ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => op == SkillProgramEffectOp.ResolveMobileJiangWeiTiantao ? new(Triggered: ProgramTrackedFact.NativeLogicalDiscard) : default;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch
    {
        SkillProgramEffectOp.ResolveMobileJiangWeiTiantao => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)],
        SkillProgramEffectOp.ResolveMobileJiangWeiShenpei => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)],
        SkillProgramEffectOp.ResolveMobileJiangWeiHuitianDeath => [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)],
        // This is a public witness, including hand cards that stay in Hand.
        // The original receipt owns the temporary top-card cleanup.
        _ => [new CreateCardSet("xinghun-shown", 5, false)]
    };
}
internal sealed class MobileJiangWeiXinghunDescriptor() : MobileJiangWeiDescriptor(SkillProgramEffectOp.ResolveMobileJiangWeiXinghun);
internal sealed class MobileJiangWeiTiantaoDescriptor() : MobileJiangWeiDescriptor(SkillProgramEffectOp.ResolveMobileJiangWeiTiantao);
internal sealed class MobileJiangWeiShenpeiDescriptor() : MobileJiangWeiDescriptor(SkillProgramEffectOp.ResolveMobileJiangWeiShenpei);
internal sealed class MobileJiangWeiHuitianDescriptor() : MobileJiangWeiDescriptor(SkillProgramEffectOp.ResolveMobileJiangWeiHuitianDeath);
internal sealed class MobileJiangWeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMobileJiangWeiHost>().Resolve(f, op);
}
internal static class MobileJiangWeiComposition
{
    internal static bool Owns(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMobileJiangWeiXinghun or SkillProgramEffectOp.ResolveMobileJiangWeiTiantao or SkillProgramEffectOp.ResolveMobileJiangWeiShenpei or SkillProgramEffectOp.ResolveMobileJiangWeiHuitianDeath;
    [ProgramCatalogValidation(40600)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Owns(e.Op))) return;
        static bool E(IReadOnlyList<SkillProgramEffect> es, SkillProgramEffectOp op) => es is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = p.Id switch
        {
            "mobile:xinghun" => p.Triggers.Count == 0 && p.Activations is [{ Id: "five-stars", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 1, UsesPerTurn: null, UsesPerGame: null, MarkerCost: null, Condition.Kind: SkillProgramConditionKind.Always } a] && E(a.Effects, SkillProgramEffectOp.ResolveMobileJiangWeiXinghun),
            "mobile:tiantao" => p.Activations.Count == 0 && p.Triggers is [{ Id: "ending-region", Subject: SkillProgramTriggerSubject.Owner, Window: SkillProgramTriggerWindow.TurnEnding, Optional: false, UsageLimit: null, Condition.Kind: SkillProgramTriggerConditionKind.Always } t] && E(t.Effects, SkillProgramEffectOp.ResolveMobileJiangWeiTiantao),
            "mobile:shenpei" => p.Activations.Count == 0 && p.Triggers is [{ Id: "dying-rain", Subject: SkillProgramTriggerSubject.Owner, Window: SkillProgramTriggerWindow.DyingEntering, Optional: true, UsageScope: SkillUsageScope.Game, UsageLimit: 1, Condition.Kind: SkillProgramTriggerConditionKind.Always } t] && E(t.Effects, SkillProgramEffectOp.ResolveMobileJiangWeiShenpei),
            "mobile:huitian" => p.Activations.Count == 0 && p.Triggers.Count == 2 && p.Triggers.Any(t => t.Id == "higher-hp-extra-turn" && t.Window == SkillProgramTriggerWindow.AfterTurnEnded && t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && t.Effects is [{ Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1 }, { Op: SkillProgramEffectOp.PendExtraTurn, Target: SkillProgramEffectTarget.Owner }]) && p.Triggers.Any(t => t.Id == "next-round-death" && !t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && t.Window == SkillProgramTriggerWindow.RoundStarting && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && E(t.Effects, SkillProgramEffectOp.ResolveMobileJiangWeiHuitianDeath)),
            _ => false
        };
        if (!valid || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": mobile Jiang Wei requires exact five-card exchange, locked native region discard, limited original dying recovery and round-start direct death.");
    }
}
public enum MobileJiangWeiStage { Exchange, ExchangeTop, ExchangeChildren, Ordering, Target, Showing, ReserveChildren, Slashes, SlashIssued, SlashReturned, ReturnChildren, Region, OtherTargets, OtherDiscard, DiscardCard, DiscardChildren, HpLoss, HpLossIssued, RecoveryChildren, ThunderTarget, ThunderIssued, GrantChildren, DeathIssued, Finished }
public sealed record MobileJiangWeiShownCard(int Id, CardKind Kind, CardLocation From, long ProcessingSequence = 0);
public sealed record MobileJiangWeiUseReturn(long ParentId, int InstructionIndex, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int CardId, CardKind Kind, CardLocation From, int Target);
public sealed record MobileJiangWeiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId)
{
    public MobileJiangWeiStage Stage { get; init; }
    public MobileJiangWeiStage NextStage { get; init; }
    public int Target { get; init; } = -1;
    public int ChosenHand { get; init; }
    public CardZoneKind Region { get; init; }
    public int Index { get; init; }
    public int Amount { get; init; }
    public long DyingId { get; init; }
    public int PaymentIndex { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public MobileJiangWeiUseReturn? Use { get; init; }
    private readonly IReadOnlyList<int> _top = [], _ordered = [], _exchanged = [], _targets = [], _hp = [];
    private readonly IReadOnlyList<MobileJiangWeiShownCard> _shown = [];
    private readonly IReadOnlyList<CardMovementRecord> _paid = [];
    public IReadOnlyList<int> Top { get => _top; init => _top = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Ordered { get => _ordered; init => _ordered = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Exchanged { get => _exchanged; init => _exchanged = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> HpLossSeats { get => _hp; init => _hp = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MobileJiangWeiShownCard> Shown { get => _shown; init => _shown = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Paid { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileJiangWeiReceipt? MobileJiangWei { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileJiangWeiUseReturn? MobileJiangWeiReturn { get; init; } }
public sealed record MobileJiangWeiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long ParentId, long DyingId, int Amount) : IGameEvent;
public sealed record MobileJiangWeiPaidEvent(long FrameId, int PaymentIndex, long Before, long After, int Count) : IGameEvent;
public sealed record MobileJiangWeiUseIssuedEvent(MobileJiangWeiUseReturn Return) : IGameEvent;
public sealed record MobileJiangWeiUseReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MobileJiangWeiCompletedEvent(long FrameId) : IGameEvent;
