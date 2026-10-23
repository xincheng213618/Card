using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryXingDaoRongHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class ResolveAnniversaryXingDaoRongXuHeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveAnniversaryXingDaoRongXuHeHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class ResolveAnniversaryXingDaoRongXuHeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryXingDaoRongHost>().Begin(f);
}
public enum XingDaoRongXuHeStage { Mode, Next, Card, Drawing, DrawChildren, DiscardChildren, Completed }
public sealed record XingDaoRongXuHeReceipt(CardConversionSource Source, string Hash, long ParentId, int ActualTurn, int PaidMaximum,
    XingDaoRongXuHeStage Stage = XingDaoRongXuHeStage.Mode, string Mode = "", int Index = 0, long Before = 0, long After = 0)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
    public CardMovementRecord? Discard { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XingDaoRongXuHeReceipt? XingDaoRongXuHe { get; init; }
}
public sealed record XingDaoRongXuHeStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int ActualTurn, int PaidMaximum) : IGameEvent;
public sealed record XingDaoRongXuHeModeEvent(long FrameId, string Mode, int Count) : IGameEvent;
public sealed record XingDaoRongXuHeTargetEvent(long FrameId, int Ordinal, int TargetSeat) : IGameEvent;
public sealed record XingDaoRongXuHeDiscardedEvent(long FrameId, int Ordinal, int TargetSeat, int CardId, CardLocation From, CardLocation To, int Sequence, long BatchId) : IGameEvent;
public sealed record XingDaoRongXuHeDrawnEvent(long FrameId, int Ordinal, int TargetSeat, int ActualCount, long Before, long After) : IGameEvent;
internal static class XingDaoRongComposition
{
    internal const string Skill = "anniversary:xuhe", Starting = "pay-maximum-neighbors", Ending = "not-highest-maximum";
    [ProgramCatalogValidation(45700)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id != Skill && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe)) return;
        if (p.Id != Skill || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 ||
            p.Triggers is not [
                { Id: Starting, Window: SkillProgramTriggerWindow.PlayPhaseStarting, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.Own, Optional: true, UsageScope: null, Condition.Kind: SkillProgramTriggerConditionKind.Always,
                  Effects: [{ Op: SkillProgramEffectOp.ChangeMaximumHp, Target: SkillProgramEffectTarget.Owner, Amount: -1, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.ResolveAnniversaryXingDaoRongXuHe, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] },
                { Id: Ending, Window: SkillProgramTriggerWindow.PlayEnding, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.Own, Optional: false, UsageScope: null, Condition.Kind: SkillProgramTriggerConditionKind.Always,
                  Effects: [{ Op: SkillProgramEffectOp.ChangeMaximumHp, Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.ChooseOption, Target: SkillProgramEffectTarget.Owner, ResultBind: "xu-he-ending", Condition.Kind: SkillProgramConditionKind.Always } options,
                    { Op: SkillProgramEffectOp.Recover, Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "xu-he-ending", OptionId: "recover" } },
                    { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 2, NumberExpression: null, Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "xu-he-ending", OptionId: "draw" } }] }] ||
            !options.Options.Select(o => o.Id).SequenceEqual(["recover", "draw"]))
            throw new InvalidOperationException(path + ": Xuhe requires the official native maximum-HP payment, whole nearby group option and independent Play-ending recovery/draw.");
    }
}
