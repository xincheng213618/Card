using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShenXuChuStage { MarkChildren, MarkChoice, DiscardChoice, DiscardChildren, DrawAttempt, DrawChildren }
public sealed record ShenXuChuReceipt(CardConversionSource Source, string GameplayHash, long ParentId,
    int InstructionIndex, SkillProgramEffectOp Operation, ShenXuChuStage Stage, long UseId = 0,
    long ActionId = 0, int TargetSeat = -1, bool TargetMarked = false, int PaidMarks = 0,
    int RequiredDiscards = 0, int DrawIndex = 0, long Before = 0, long After = 0, int TargetIndex = 0,
    uint RandomBefore = 0, uint RandomAfter = 0)
{
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<ShenXuChuDraw> _draws = Array.AsReadOnly(Array.Empty<ShenXuChuDraw>());
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<int> _maximumSeats = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ShenXuChuDraw> Draws { get => _draws; init => _draws = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> MaximumSeats { get => _maximumSeats; init => _maximumSeats = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ShenXuChuDraw(int Seat, int Count);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShenXuChuReceipt? ShenXuChu { get; init; } }
public sealed record ZhengqingSettledEvent(long FrameId, CardConversionSource Source, int Round, int WinnerSeat,
    int MaximumDamage) : IGameEvent;
public sealed record ZhuangpoTargetIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int TargetSeat, bool TargetMarked) : IGameEvent;
public sealed record ZhuangpoMarksPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, int Before, int Removed, int After) : IGameEvent;
public sealed record ZhuangpoDiscardPaidEvent(long FrameId, int TargetSeat, int Count, long Before, long After) : IGameEvent;
public sealed record ShenXuChuDrawReturnedEvent(long FrameId, int Index, int Seat, int Requested, int Actual, long Before, long After) : IGameEvent;

internal interface IShenXuChuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShenXuChuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ShenXuChuHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveZhuangpoTarget ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindows(
        op == SkillProgramEffectOp.SettleZhengqingRound ? [SkillProgramTriggerWindow.RoundEnding] : [SkillProgramTriggerWindow.CardUseTargetsFinalized])];
}
internal sealed class SettleZhengqingRoundDescriptor() : ShenXuChuDescriptor(SkillProgramEffectOp.SettleZhengqingRound);
internal sealed class ResolveZhuangpoTargetDescriptor() : ShenXuChuDescriptor(SkillProgramEffectOp.ResolveZhuangpoTarget);
internal sealed class ShenXuChuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShenXuChuHost>().Resolve(f, op);
}
internal static class ShenXuChuComposition
{
    internal const string Zhengqing = "anniversary:zhengqing", Zhuangpo = "anniversary:zhuangpo", ViewAs = "printed-sha-duel";
    internal const string HappyZhengqing = "happy:zhengqing", HappyZhuangpo = "happy:zhuangpo";
    internal static bool IsZhengqing(string id) => id is Zhengqing or HappyZhengqing;
    internal static bool IsZhuangpo(string id) => id is Zhuangpo or HappyZhuangpo;
    [ProgramCatalogValidation(38800)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.SettleZhengqingRound or SkillProgramEffectOp.ResolveZhuangpoTarget)) return;
        if (p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 ||
            p.Triggers is not [var t] || t.Optional || t.UsageLimit is not null || t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e])
            throw new InvalidOperationException(path + ": Shen Xu Chu requires his exact native round/designation composition.");
        var valid = IsZhengqing(p.Id) ? p.ViewAs.Count == 0 && t.Id == "round-maximum-damage" && t.Window == SkillProgramTriggerWindow.RoundEnding && t.Subject == SkillProgramTriggerSubject.Owner && e.Op == SkillProgramEffectOp.SettleZhengqingRound :
            IsZhuangpo(p.Id) && t.Id == "converted-duel-target" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.OwnerRelation == SkillProgramCardActionOwnerRelation.ConversionSource &&
            t.SourceSkillId == p.Id && t.SourceViewAsId == ViewAs && t.CardKinds.Count == 0 && e.Op == SkillProgramEffectOp.ResolveZhuangpoTarget &&
            p.ViewAs is [var v] && v.Id == ViewAs && v.OutputKind == CardKind.Duel && v.InputCount == 1 && v.ForPlay && !v.ForResponse && v.AllowSameKind && v.SingleCardTrickUse &&
            v.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
            (v.InputKinds.Order().SequenceEqual(CardPrintedInfo.SlashKinds) ||
             v.InputKinds.Order().SequenceEqual(CardPrintedInfo.SlashKinds.Where(kind => kind != CardKind.ColdSlash)));
        if (!valid) throw new InvalidOperationException(path + ": Shen Xu Chu requires his complete edition-bound round settlement and printed-face conversion.");
    }
}
