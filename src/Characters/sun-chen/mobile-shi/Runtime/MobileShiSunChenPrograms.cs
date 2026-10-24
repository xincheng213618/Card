using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiScStage { Cards, DiscardChildren, Votes, Gifts, GiveCard, GiftChildren, Chain, ChainChildren, Drawing, DrawChildren, Momentum, DamageChildren, Finished }
public sealed record ShiScVote(int Seat, bool Give);
public sealed record ShiScMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record ShiScReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, int Turn, int TurnOwner, int PhaseId,
    long? WindowId, long UseId, long ActionId, ShiScStage Stage)
{
    public int Before { get; init; }
    public int After { get; init; }
    public int Cursor { get; init; }
    public int Target { get; init; } = -1;
    public int Ungiven { get; init; }
    public bool Chain { get; init; }
    public bool Draw { get; init; }
    public bool Given { get; init; }
    public ShiScMaterial? Material { get; init; }
    private readonly IReadOnlyList<int> _cards = [], _participants = [], _targets = [];
    private readonly IReadOnlyList<ShiScVote> _votes = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Participants { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ShiScVote> Votes { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiScReceipt? ShiSunChen { get; init; } }
public sealed record ShiScStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, int Turn, int TurnOwner, int PhaseId, long? WindowId, long UseId, long ActionId) : IGameEvent;
public sealed record ShiScCostPaidEvent(long FrameId, int Count, int Before, int After) : IGameEvent;
public sealed record ShiScParticipantEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record ShiScVoteRevealedEvent(long FrameId, int Index, int Seat, bool Give) : IGameEvent;
public sealed record ShiScGiftPaidEvent(long FrameId, int Index, int Seat, int CardId, CardLocation From, CardLocation To, int Sequence) : IGameEvent;
public sealed record ShiScArmedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, int Count) : IGameEvent;
public sealed record ShiScLulianBoundEvent(long FrameId, long UseId, long ActionId, bool Chain, bool Draw, int TargetCount) : IGameEvent;
public sealed record ShiScTargetEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record ShiScChainIssuedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record ShiScDrawPaidEvent(long FrameId, int Count, long Before, long After) : IGameEvent;
public sealed record ShiScMomentumIssuedEvent(long FrameId, int Target) : IGameEvent;
public sealed record ShiScCompletedEvent(long FrameId) : IGameEvent;
public sealed record ShiScDamageReceipt(long AttackId, long ArmId, int Source, int Target, int ChainCursor, int Inherited, int? TransferredTarget = null);
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiScDamageReceipt? ShiSunChenDamage { get; init; } }
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiScDamageReceipt? ShiSunChenDamage { get; init; } }
public sealed partial record JudgmentFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiScDamageReceipt? ShiSunChenDamage { get; init; } }
public sealed record ShiScDamageIssuedEvent(long ArmId, long AttackId, int Source, int Target, int Cursor, int Inherited) : IGameEvent;
public sealed record ShiScDamageConsumedEvent(long ArmId, long AttackId, long DamageId, int Target, int Cursor) : IGameEvent;

internal interface IShiSunChenHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class ShiSunChenDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new ShiSunChenHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance | ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.NativeLogicalDiscard,
        Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op == SkillProgramEffectOp.ResolveMobileShiSunChenLulian ? [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)] : [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ResolveMobileShiSunChenNiguDescriptor() : ShiSunChenDescriptor(SkillProgramEffectOp.ResolveMobileShiSunChenNigu);
internal sealed class ResolveMobileShiSunChenLulianDescriptor() : ShiSunChenDescriptor(SkillProgramEffectOp.ResolveMobileShiSunChenLulian);
internal sealed class ShiSunChenHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IShiSunChenHost>().Begin(f, op);
}
internal static class ShiSunChenComposition
{
    [ProgramCatalogValidation(49100)] internal static void Validate(string path, SkillProgram p)
    {
        static bool Own(IReadOnlyList<SkillProgramEffect> es, SkillProgramEffectOp op) => es is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        if (!p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileShiSunChenNigu or SkillProgramEffectOp.ResolveMobileShiSunChenLulian)) return;
        var valid = p.Id == "mobile:shi-nigu" ? p.Triggers.Count == 0 && p.Activations is [{ Id: "distinct-suits-private-gifts", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, UsesPerPhase: 1, UsesPerTurn: null } a] && Own(a.Effects, SkillProgramEffectOp.ResolveMobileShiSunChenNigu) :
            p.Id == "mobile:shi-lulian" && p.Activations.Count == 0 && p.Triggers is [{ Id: "completed-hand-category", Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Actor, Optional: false, IncludeResponseUses: true, AllowNoEventTarget: false, SingleActionInstance: true } t] && Own(t.Effects, SkillProgramEffectOp.ResolveMobileShiSunChenLulian);
        if (!valid || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0) throw new InvalidOperationException(path + ": Shi Sun Chen requires the real once-Play payment and completed physical hand-use boundary.");
    }
}
internal sealed class ShiSunChenCompletedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "mobile:shi-lulian";
    public bool AllowsSingleActionInstance => true;
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !t.IncludeResponseUses || t.AllowNoEventTarget || !t.SingleActionInstance || t.Optional ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiSunChenLulian, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Lulian requires the exact completed native physical Hand use, including typed response uses.");
    }
}
