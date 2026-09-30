using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using System.Text.Json;
using static Program;

internal static class FeedbackChecks
{
    public static void PublicProjection()
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var view = Engine(vm).CreateSnapshot(0);
        var sequence = 0L;
        EventEnvelope Envelope(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 1, "feedback-check", payload);
        var privateEvents = new[]
        {
            Envelope(new GeneralSelectionRequestedEvent(1, ["private-general"])),
            Envelope(new GeneralSelectedEvent(1, "private-general")),
            Envelope(new CardMovedEvent(9876, CardKind.Peach, CardLocation.DrawPile,
                new CardLocation(CardZoneKind.Hand, 1), CardMoveReasons.Draw))
        };
        Assert(BattleCueProjector.Project(privateEvents, view).Count == 0, "Hidden setup or hand movements leaked into feedback.");
        int[] targets = [1, 1, -1, 99];
        var publicEvents = new[]
        {
            Envelope(new CardUseDeclaredEvent(7, 2468, CardKind.Slash, 0)),
            Envelope(new TargetsConfirmedEvent(7, targets)),
            Envelope(new CardRespondedEvent(1357, 1, 0, CardKind.Slash)),
            Envelope(new DuelResponseEvent(7, 1, true, 1357, CardKind.Slash)),
            Envelope(new DamageAppliedEvent(0, 1, 2, 1, DamageNature.Fire)),
            Envelope(new RecoveryAppliedEvent(2, 1, 1, 2)),
            Envelope(new DamageAppliedEvent(0, 1, 0, 2))
        };
        var cues = BattleCueProjector.Project(publicEvents, view);
        targets[0] = 4;
        Assert(cues.Count == 4 && cues.Select(cue => cue.Sequence).Distinct().Count() == 4, "Public response duplicated or zero damage rendered.");
        Assert(cues[0].Label == "杀" && cues[0].TargetSeats.SequenceEqual([1]), "Declared card or detached public targets differ.");
        Assert(cues[1].Label == "打出杀" && cues[2].Label == "−2" && cues[2].Nature == DamageNature.Fire && cues[3].Label == "+1", "Response, damage, or recovery feedback differs from committed events.");
        var sourceFreeEvents = new[] { Envelope(new DamageAppliedEvent(1, 1, 1, 0) { SourceLess = true }) };
        Assert(BattleCueProjector.Project(sourceFreeEvents, view).Single() is
            { Kind: BattleCueKind.Damage, SourceSeat: -1, TargetSeats: [1] },
            "Source-free damage feedback falsely attributes its internal continuation seat.");
        var sourceFreeReport = MatchSummary.Create(view with { Status = EngineStatus.Completed }, sourceFreeEvents)!;
        Assert(sourceFreeReport.Players.Single(player => player.Seat == 1) is { DamageDealt: 0, DamageTaken: 1 },
            "Source-free debt damage must count as received damage without crediting a damage dealer.");
        var virtualResponses = BattleCueProjector.Project(
            [Envelope(new CardRespondedEvent(-1, 1, 0, CardKind.Dodge)),
             Envelope(new CardRespondedEvent(-1, 2, 0, CardKind.Dodge))], view);
        Assert(virtualResponses.Count == 2 && virtualResponses.All(cue => cue.Label == "打出闪"),
            "Distinct virtual responses must not be collapsed by their shared nonphysical card sentinel.");
        var armorCues = BattleCueProjector.Project(
        [
            Envelope(new ArmorEffectAppliedEvent(8, CardKind.RenwangShield, 0, 1, CardKind.Slash)),
            Envelope(new JudgmentResolvedEvent(
                9,
                8,
                1,
                JudgmentReasons.BaguaDefense,
                42,
                CardKind.Dodge,
                Suit.Heart,
                Rank: 7,
                Succeeded: true))
        ], view);
        Assert(
            armorCues.Count == 2 &&
            armorCues[0].Label.Contains("仁王盾") &&
            armorCues[0].Label.Contains("无效") &&
            armorCues[1].Kind == BattleCueKind.Judgment &&
            armorCues[1].Label == "八卦阵 · ♥7" &&
            armorCues[1].Detail == "红色 · 视为打出闪",
            "Formal armor outcomes were not projected as public battle feedback.");
        var judgmentCues = BattleCueProjector.Project(
        [
            Envelope(new JudgmentRequestedEvent(10, 7, 1, JudgmentReasons.Indulgence, CardKind.Indulgence)),
            Envelope(new JudgmentReplacementResolvedEvent(11, 10, 1, 0, JudgmentReasons.Indulgence, true,
                40, 41, CardKind.Peach, Suit.Heart, 12)),
            Envelope(new JudgmentResolvedEvent(10, 7, 1, JudgmentReasons.Indulgence, 41, CardKind.Peach,
                Suit.Heart, 12, true)),
            Envelope(new JudgmentResolvedEvent(12, 8, 1, JudgmentReasons.Lightning, 43, CardKind.Slash,
                Suit.Spade, 5, true))
        ], view);
        Assert(judgmentCues.Count == 4 &&
               judgmentCues[0] is { Kind: BattleCueKind.Judgment, Label: "乐不思蜀 · 判定中" } &&
               judgmentCues[1] is { SourceSeat: 0, TargetSeats: [1], Label: "鬼才改判 · ♥Q", Detail: "最终判定牌已替换" } &&
               judgmentCues[2] is { Label: "乐不思蜀 · ♥Q", Detail: "红桃 · 不跳过出牌阶段" } &&
               judgmentCues[3] is { Label: "闪电 · ♠5", Detail: "黑桃 2–9 · 命中" },
            "Judgment lifecycle, replacement, card face, or rule outcome was not projected exactly.");
        var responseCues = BattleCueProjector.Project(
        [
            Envelope(new ResponseRequestedEvent(0, 1, CardKind.Slash, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(20, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2)),
            Envelope(new NullificationRequestedEvent(21, 45, CardKind.Duel, 0, 1, false, 0)),
            Envelope(new NullificationRespondedEvent(21, 45, CardKind.Duel, 1, 46, true, 1)),
            Envelope(new NullificationRequestedEvent(21, 45, CardKind.Duel, 0, 2, true, 1)),
            Envelope(new NullificationResolvedEvent(21, 45, CardKind.Duel, true, 1))
        ], view);
        Assert(responseCues.Count == 6 &&
               responseCues[0] is { Kind: BattleCueKind.ResponseWindow, SourceSeat: 1, TargetSeats: [0], Label: "等待闪响应", Detail: "响应【杀】" } &&
               responseCues[1] is { Label: "连续响应 1 / 2", Detail: "已打出【闪】" } &&
               responseCues[2] is { Label: "无懈可击询问中 · 第 1 层", Detail: "当前锦囊生效中 · 可令其失效" } &&
               responseCues[3] is { Kind: BattleCueKind.Response, Label: "打出无懈可击 · 第 1 层", Detail: "锦囊暂时失效" } &&
               responseCues[4] is { Label: "无懈可击询问中 · 第 2 层", Detail: "当前锦囊已失效 · 可反制恢复" } &&
               responseCues[5] is { SourceSeat: -1, Label: "决斗 · 已失效", Detail: "无懈链共 1 次响应" },
            "Response request, locked progress, or layered Nullification state was not projected exactly.");
        var iFieldView = view with
        {
            Players = view.Players.Select(player => player.Seat == 0
                ? player with
                {
                    Skills = [new GeneralSkillDefinition("I力场", "")
                    { ContentId = "classic:i-field" }]
                }
                : player).ToArray()
        };
        var iFieldCue = BattleCueProjector.Project(
            [Envelope(new WuyanDamagePreventedEvent(22, 1, 0, CardKind.Duel, 1, 0,
                "classic:i-field"))], iFieldView).Single();
        Assert(iFieldCue.Label == "I力场 · 锦囊伤害已防止",
            "Incoming trick prevention must display the skill that actually prevented damage.");
    }


    private static void Play(MainViewModel vm, LegalAction action)
    {
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
        if (action.TargetSeat is { } seat && !vm.Seats.Single(player => player.Seat == seat).IsSelectedTarget)
            vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
        Assert(vm.CanConfirmSelected, "Feedback fixture selected an unusable card.");
        vm.ConfirmSelectedCommand.Execute(null);
    }

    private static void Step(MainViewModel vm)
    {
        if (vm.IsGeneralSelectionPending) vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        else if (vm.IsDiscardSelectionPending) ResolveDiscard(vm);
        else if (vm.CanStepAi) vm.StepAiCommand.Execute(null);
        else if (vm.CanEndTurn)
        {
            var peach = Engine(vm).GetHumanLegalActions().FirstOrDefault(action => action.CardId is { } id &&
                vm.Hand.Single(card => card.Id == id).Name == "桃" && action.TargetSeat == 0);
            if (peach is not null) Play(vm, peach); else vm.EndTurnCommand.Execute(null);
        }
        else if (vm.CanDeclineResponse) vm.DeclineResponseCommand.Execute(null);
        else
        {
            var choices = new (IEnumerable<PromptChoice>, ICommand)[]
            {
                (vm.ResponseChoices, vm.SelectResponseChoiceCommand), (vm.DyingChoices, vm.SelectDyingChoiceCommand),
                (vm.HarvestChoices, vm.SelectHarvestChoiceCommand), (vm.TargetCardChoices, vm.SelectTargetCardChoiceCommand),
                (vm.FireAttackChoices, vm.SelectFireAttackChoiceCommand),
                (vm.NullificationChoices, vm.SelectNullificationChoiceCommand), (vm.SkillChoices, vm.SelectSkillChoiceCommand)
            };
            var available = choices.FirstOrDefault(pair => pair.Item1.Any());
            Assert(available.Item1 is not null, $"Feedback fixture has no continuation at {Engine(vm).State.Status}.");
            available.Item2.Execute(available.Item1!.First());
        }
        Assert(!vm.PromptText.Contains("未执行"), vm.PromptText);
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
