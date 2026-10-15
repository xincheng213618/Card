using System.IO;
using System.Collections.ObjectModel;
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

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Images(child)) yield return nested;
    }

    private static IEnumerable<(ImageDrawing Image, Rect Bounds)> ImageBounds(Drawing? drawing, Matrix transform)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect));
        if (drawing is DrawingGroup group)
        {
            var nestedTransform = group.Transform?.Value ?? Matrix.Identity;
            nestedTransform.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in ImageBounds(child, nestedTransform)) yield return nested;
        }
    }

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
        var privateQuantities = BattleCueProjector.Project(privateEvents, view);
        Assert(privateQuantities is [{ Kind: BattleCueKind.Draw, SourceSeat: 1, TargetSeats: [1], DrawCount: 1,
                Label: "摸牌 +1", PublicCardId: null, PublicCard: null, CardKind: null }] &&
               BattleCueProjector.Project(privateEvents.Take(2), view).Count == 0 &&
               BattleCueProjector.ProjectCardPlays(privateEvents, view).Count == 0,
            "Draw feedback may disclose only the public quantity; hidden generals and private physical faces must remain absent.");
        var drawState = SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
        var extraDraw = new CardMoveReason("program.extra-draw-phase.draw");
        var drawEvents = new[]
        {
            Envelope(new CardMovedEvent(8901, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8902, CardKind.Peach, CardLocation.DrawPile, CardLocation.Hand(1), extraDraw)),
            Envelope(new CardMovedEvent(8903, CardKind.Dodge, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8904, CardKind.Nullification, CardLocation.DrawPile, CardLocation.Hand(1), CardMoveReasons.Draw)),
            Envelope(new ActualDrawPhaseCompletedEvent(100, 0, 1, false, false, 99)),
            Envelope(new CardMovedEvent(8905, CardKind.IronChain, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.Draw)) with { Revision = 2 }
        };
        var drawCues = BattleCueProjector.Project(drawEvents, view);
        Assert(drawCues is
               [{ Kind: BattleCueKind.Draw, SourceSeat: 0, TargetSeats: [0], DrawCount: 2, Label: "摸牌 +2", Revision: 1 },
                { Kind: BattleCueKind.Draw, SourceSeat: 1, TargetSeats: [1], DrawCount: 2, Label: "摸牌 +2", Revision: 1 },
                { Kind: BattleCueKind.Draw, SourceSeat: 0, TargetSeats: [0], DrawCount: 1, Label: "摸牌 +1", Revision: 2 }] &&
               drawCues.Select(cue => cue.Sequence).SequenceEqual(new[] { drawEvents[2].Sequence, drawEvents[3].Sequence, drawEvents[5].Sequence }) &&
               drawCues.All(cue => cue.ActorName == view.Players.Single(player => player.Seat == cue.SourceSeat).GeneralName),
            "Actual normal/extra draws must aggregate once per committed revision and owner at the last movement, not use a requested phase quantity or another player's name.");
        Assert(drawCues.All(cue => cue.HasDrawCards && cue is
                { IsInquiry: false, CardKind: null, PublicCardId: null, PublicCard: null, JudgmentCard: null,
                    WeaponKind: null, IsUseCompletion: false, HpDelta: 0, RemainingHp: null }) &&
               BattleCueProjector.ProjectCardPlays(drawEvents, view).Count == 0 &&
               !(drawCues[0] with { DrawCount = 0 }).HasDrawCards &&
               !(drawCues[0] with { DrawCount = -1 }).HasDrawCards &&
               !(drawCues[0] with { SourceSeat = -1 }).HasDrawCards &&
               !(drawCues[0] with { IsInquiry = true }).HasDrawCards &&
               !(drawCues[0] with { Kind = BattleCueKind.Card }).HasDrawCards,
            "A draw quantity must not become a private face, fake played card, HP result or valid inquiry/empty/invalid-source animation.");
        var excludedDraws = new[]
        {
            Envelope(new CardMovedEvent(8910, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.InitialDeal)),
            Envelope(new CardMovedEvent(8911, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), CardMoveReasons.RecastDraw)),
            Envelope(new CardMovedEvent(8912, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), new("skill.private.draw"))),
            Envelope(new CardMovedEvent(8913, CardKind.Slash, CardLocation.DrawPile, new(CardZoneKind.PrivateReserve, 0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8914, CardKind.Slash, CardLocation.Hand(1), CardLocation.Hand(0), CardMoveReasons.Draw)),
            Envelope(new CardMovedEvent(8915, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(999), CardMoveReasons.Draw)),
            Envelope(new ActualDrawPhaseCompletedEvent(101, 0, 1, true, true, 2)),
            Envelope(new ActualDrawPhaseCompletedEvent(102, 0, 1, false, false, 0))
        };
        Assert(BattleCueProjector.Project(excludedDraws, view).Count == 0 &&
               BattleCueProjector.Project([], view).Count == 0,
            "Initial/recast/unknown/private-zone/invalid-seat movements or skipped/empty phases must not invent an actual draw.");
        var changedPrivateFaces = drawEvents.Select(envelope => envelope.Payload is CardMovedEvent moved
            ? envelope with { Payload = moved with { CardId = -777, CardKind = (CardKind)int.MaxValue } } : envelope).ToArray();
        Assert(JsonSerializer.Serialize(drawCues) == JsonSerializer.Serialize(BattleCueProjector.Project(changedPrivateFaces, view)) &&
               JsonSerializer.Serialize(drawCues) == JsonSerializer.Serialize(BattleCueProjector.Project(drawEvents, Engine(vm).CreateSnapshot(1))) &&
               drawState == SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true)),
            "Changing hidden draw identities/kinds or the safe viewer must not change public quantity feedback or mutate the engine.");
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
        Assert(cues.All(cue => cue.Revision == 1), "Public feedback lost its committed command revision.");
        Assert(cues[0].Label == "杀" && cues[0].TargetSeats.SequenceEqual([1]), "Declared card or detached public targets differ.");
        Assert(cues[0].CardKind == CardKind.Slash && cues[1].CardKind == CardKind.Slash && cues.Skip(2).All(cue => cue.CardKind is null),
            "Use and response artwork must come from their public effective kinds, never from private card IDs.");
        var recastCue = BattleCueProjector.Project([Envelope(new CardRecastEvent(0, 300, CardKind.IronChain, 1))], view).Single();
        Assert(recastCue is { CardKind: CardKind.IronChain, Detail: "重铸" }, "Recast lost its public card kind or action label.");
        Assert(cues[1].Label == "打出杀" && cues[2].Label == "−2" && cues[2].Nature == DamageNature.Fire && cues[3].Label == "+1", "Response, damage, or recovery feedback differs from committed events.");
        var lifeEvents = new[]
        {
            Envelope(new PlayerDyingEvent(41, 1, 0)),
            Envelope(new RecoveryAppliedEvent(2, 1, 2, 1)),
            Envelope(new DyingResolvedEvent(41, 1, true)),
            Envelope(new PlayerDyingEvent(42, 1, null)),
            Envelope(new PlayerDiedEvent(1, null)),
            Envelope(new DyingResolvedEvent(42, 1, false))
        };
        var lifeCues = BattleCueProjector.Project(lifeEvents, view);
        Assert(lifeCues.Select(cue => cue.Kind).SequenceEqual(new[]
            { BattleCueKind.Dying, BattleCueKind.Recovery, BattleCueKind.Rescued, BattleCueKind.Dying, BattleCueKind.Death }) &&
            lifeCues.Select(cue => cue.Sequence).SequenceEqual(lifeEvents.Take(5).Select(envelope => envelope.Sequence)),
            "Public dying, recovery, survival and death feedback must preserve event order without inventing a failed-rescue result.");
        Assert(lifeCues[0] is { LifeResolutionId: 41, SourceSeat: 1, TargetSeats: [1] } &&
            lifeCues[2] is { LifeResolutionId: 41, SourceSeat: 1, TargetSeats: [1], Label: "脱离濒死" } &&
            lifeCues[3] is { LifeResolutionId: 42, SourceSeat: 1, TargetSeats: [1] } &&
            lifeCues[4] is { LifeResolutionId: null, SourceSeat: 1, TargetSeats: [1] },
            "Life feedback must use the published victim and exact dying-window ID; death must not guess a frame ID or attribute its effect to the killer.");
        Assert(lifeCues[1] is { HpDelta: 2, RemainingHp: 1, SourceSeat: 2, TargetSeats: [1] } &&
            lifeCues.Where(cue => cue.Kind != BattleCueKind.Recovery).All(cue =>
                cue is { HpDelta: 0, RemainingHp: null, MaximumHp: null, IsInquiry: false }),
            "Only the public recovery fact may supply HP values; a survival event cannot infer an earlier HP result from the final viewer snapshot.");
        Assert(lifeCues.All(cue => cue is
            { CardKind: null, PublicCardId: null, PublicCard: null, IsUseCompletion: false, Revision: 1 }) &&
            BattleCueProjector.Project([Envelope(new DyingResolvedEvent(43, 2, false))], view).Count == 0,
            "Life outcomes must contain no private physical card face, fabricated card use, or rescue cue for a non-survived completion.");
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
        var zeroResponses = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardRespondedEvent(0, 1, 0, CardKind.Dodge)),
             Envelope(new GroupResponseEvent(30, CardKind.ArrowBarrage, CardKind.Dodge, 1, true, 0)),
             Envelope(new CardRespondedEvent(0, 2, 0, CardKind.Dodge)),
             Envelope(new GroupResponseEvent(30, CardKind.ArrowBarrage, CardKind.Dodge, 2, true, 0))], view);
        Assert(zeroResponses.Count == 2 && zeroResponses.Select(cue => cue.SourceSeat).SequenceEqual([1, 2]),
            "The zero virtual-card sentinel collapsed distinct players or duplicated their group results.");
        var unknownResponse = BattleCueProjector.ProjectCardPlays([Envelope(new CardRespondedEvent(9876, 1, 0))], view).Single();
        Assert(unknownResponse.CardKind is null && unknownResponse.Label == "打出响应牌",
            "A legacy response without a public kind inferred a hidden physical face.");
        var counterspellCards = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardRespondedEvent(46, 1, 0, CardKind.Nullification)),
             Envelope(new NullificationRespondedEvent(21, 45, CardKind.Duel, 1, 46, true, 1))], view);
        Assert(counterspellCards.Count == 1 && counterspellCards[0].CardKind == CardKind.Nullification,
            "The committed counterspell and its chain result produced two public cards.");
        var rescueCards = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardUseDeclaredEvent(31, 99, CardKind.Peach, 1)),
             Envelope(new DyingResponseEvent(32, 1, true, 99) { UsedPeachPhysicalCardKind = CardKind.Dodge })], view);
        Assert(rescueCards.Count == 1 && rescueCards[0].CardKind == CardKind.Peach && rescueCards[0].CardActionLabel == "使用",
            "Converted rescue duplicated its declared use or displayed its physical material as the effective card.");
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
        // A public zone can show the printed Spade while the judgment event
        // publishes an effective Heart (for example, Hongyan). Neither is
        // evidence for exposing a new ordinary played-card face.
        var judgmentView = view with
        {
            Players = Array.AsReadOnly(view.Players.Select(player => player.Seat == 1 ? player with
            {
                Judgment = Array.AsReadOnly(new[] { new CardSnapshot(41, CardKind.Peach, Suit.Spade, 12, "桃", "Q") })
            } : player).ToArray())
        };
        var judgmentEvents = new[]
        {
            Envelope(new JudgmentRequestedEvent(10, 7, 1, JudgmentReasons.Indulgence, CardKind.Indulgence)),
            Envelope(new JudgmentReplacementResolvedEvent(11, 10, 1, 0, JudgmentReasons.Indulgence, true,
                40, 41, CardKind.Peach, Suit.Heart, 12)),
            Envelope(new JudgmentResolvedEvent(10, 7, 1, JudgmentReasons.Indulgence, 41, CardKind.Peach,
                Suit.Heart, 12, true)),
            Envelope(new JudgmentResolvedEvent(12, 8, 1, JudgmentReasons.Lightning, 43, CardKind.Slash,
                Suit.Spade, 5, true))
        };
        var judgmentCues = BattleCueProjector.Project(judgmentEvents, judgmentView);
        Assert(judgmentCues.Count == 4 &&
               judgmentCues[0] is { Kind: BattleCueKind.Judgment, JudgmentStage: JudgmentCueStage.Requested,
                   JudgmentResolutionId: 10, JudgmentTitle: "乐不思蜀", IsInquiry: true, Label: "乐不思蜀 · 判定中" } &&
               judgmentCues[1] is { SourceSeat: 0, TargetSeats: [1], JudgmentStage: JudgmentCueStage.Replaced,
                   JudgmentResolutionId: 10, JudgmentTitle: "乐不思蜀", IsInquiry: false, Label: "鬼才改判 · ♥Q", Detail: "最终判定牌已替换" } &&
               judgmentCues[2] is { JudgmentStage: JudgmentCueStage.Resolved, JudgmentResolutionId: 10, IsInquiry: false,
                   JudgmentTitle: "乐不思蜀", Label: "乐不思蜀 · ♥Q", Detail: "红桃 · 不跳过出牌阶段" } &&
               judgmentCues[3] is { JudgmentStage: JudgmentCueStage.Resolved, JudgmentResolutionId: 12, IsInquiry: false,
                   JudgmentTitle: "闪电", Label: "闪电 · ♠5", Detail: "黑桃 2–9 · 命中" } &&
               judgmentCues.Select(cue => cue.Sequence).SequenceEqual(judgmentEvents.Select(envelope => envelope.Sequence)),
            "Judgment lifecycle, replacement, card face, or rule outcome was not projected exactly.");
        Assert(judgmentCues.Take(2).All(cue => cue.JudgmentCard is null && !cue.HasJudgmentCard) &&
            judgmentCues[2] is { JudgmentCard: { Id: 41, Kind: CardKind.Peach, Suit: Suit.Heart, Rank: 12, RankText: "Q" } } &&
            judgmentCues[3] is { JudgmentCard: { Id: 43, Kind: CardKind.Slash, Suit: Suit.Spade, Rank: 5, RankText: "5" } } &&
            judgmentView.Players.Single(player => player.Seat == 1).Judgment.Single().Suit == Suit.Spade &&
            judgmentCues.All(cue => cue is { CardKind: null, PublicCardId: null, PublicCard: null }) &&
            judgmentCues.Skip(2).All(cue => cue.HasJudgmentCard && cue.JudgmentCard is { } card &&
                card.DisplayName == CardCatalog.Get(card.Kind).DisplayName) &&
            BattleCueProjector.ProjectCardPlays(judgmentEvents, judgmentView).Count == 0,
            "Only a resolved judgment may publish effective suit/rank; those values cannot become an ordinary physical face or borrow a printed suit from the final zone.");
        var waitingJudgment = BattleCueProjector.Project([judgmentEvents[0]], judgmentView).Single();
        Assert(waitingJudgment is { IsInquiry: true, CardKind: null, PublicCardId: null, PublicCard: null, JudgmentCard: null } &&
            BattleCueProjector.Project(
            [Envelope(new JudgmentReplacementRequestedEvent(13, 10, 1, 0, JudgmentReasons.Indulgence, 40, CardKind.Slash, Suit.Spade)),
             Envelope(new JudgmentReplacementResolvedEvent(13, 10, 1, 0, JudgmentReasons.Indulgence, false,
                 40, null, null, null, null))], judgmentView).Count == 0,
            "A pending judgment or unaccepted private replacement opportunity inferred an initial or hidden hand face.");
        var exhaustedJudgment = BattleCueProjector.Project(
            [Envelope(new JudgmentResolvedEvent(14, 8, 2, JudgmentReasons.BaguaDefense, null, null, null, null, false))], judgmentView).Single();
        Assert(exhaustedJudgment is { Kind: BattleCueKind.Judgment, JudgmentStage: JudgmentCueStage.Resolved,
            JudgmentResolutionId: 14, IsInquiry: false, CardKind: null, PublicCardId: null, PublicCard: null,
            JudgmentTitle: "八卦阵", JudgmentCard: null, HasJudgmentCard: false, Detail: "没有可用的判定牌" } &&
            cues.All(cue => cue.JudgmentStage is null && cue.JudgmentResolutionId is null && cue.JudgmentCard is null && !cue.HasJudgmentCard),
            "An exhausted judgment invented a card face or an unrelated public action acquired a judgment stage.");
        var luoshenCues = BattleCueProjector.Project(
        [Envelope(new JudgmentResolvedEvent(20, 7, 1, JudgmentReasons.Luoshen, 44, CardKind.Slash, Suit.Spade, 3, false)),
         Envelope(new JudgmentResolvedEvent(21, 7, 1, JudgmentReasons.Luoshen, 45, CardKind.Slash, Suit.Club, 4, false)),
         Envelope(new JudgmentResolvedEvent(22, 7, 1, JudgmentReasons.Luoshen, 46, CardKind.Slash, Suit.Heart, 5, true)),
         Envelope(new JudgmentResolvedEvent(23, 7, 1, JudgmentReasons.Luoshen, 47, CardKind.Slash, Suit.Diamond, 6, true))], view);
        Assert(luoshenCues.All(cue => cue is { JudgmentTitle: "洛神", HasJudgmentCard: true }) &&
            luoshenCues.Select(cue => cue.Detail).SequenceEqual(new[]
                { "黑色 · 可继续判定", "黑色 · 可继续判定", "红色 · 结束", "红色 · 结束" }),
            "Luoshen must follow its public black-suit repeat condition, not invert the engine's generic red-suit Succeeded flag or promise another judgment was accepted.");
        var ganglieCues = BattleCueProjector.Project(
        [Envelope(new JudgmentResolvedEvent(24, 7, 1, JudgmentReasons.Ganglie, 48, CardKind.Slash, Suit.Club, 7, false)),
         Envelope(new JudgmentResolvedEvent(25, 7, 1, JudgmentReasons.Ganglie, 49, CardKind.Slash, Suit.Heart, 8, true))], view);
        Assert(ganglieCues.All(cue => cue is { JudgmentTitle: "刚烈", HasJudgmentCard: true, Detail: "判定花色已确定" }),
            "A shared Ganglie reason cannot call the skill successful/failed when its registered variants use different suit conditions.");
        var leijiCues = BattleCueProjector.Project(new[]
            { JudgmentReasons.Leiji, "skill.classic.leiji", "skill.national.leiji", "skill.boundary.leiji" }
            .Select((reason, index) => Envelope(new JudgmentResolvedEvent(26 + index, 7, 1, reason,
                50 + index, CardKind.Slash, Suit.Spade, 9, false))), view);
        Assert(leijiCues.Count == 4 && leijiCues.All(cue => cue is
            { Kind: BattleCueKind.Judgment, JudgmentTitle: "雷击", HasJudgmentCard: true,
                Detail: "判定花色已确定", HpDelta: 0, RemainingHp: null }),
            "A public black Leiji judgment must not claim generic failure or damage/recovery before the subsequent decisions and typed HP facts.");
        var tieqiCues = BattleCueProjector.Project(
        [Envelope(new JudgmentResolvedEvent(30, 7, 1, "skill.slash-response-judgment", 54, CardKind.Slash, Suit.Heart, 10, true)),
         Envelope(new JudgmentResolvedEvent(31, 7, 1, "skill.slash-response-judgment", 55, CardKind.Slash, Suit.Spade, 11, false)),
         Envelope(new JudgmentResolvedEvent(32, 7, 1, "boundary:tieqi", 56, CardKind.Slash, Suit.Heart, 12, true))], view);
        Assert(tieqiCues[0] is { JudgmentTitle: "铁骑", Detail: "红色 · 目标不能使用闪" } &&
            tieqiCues[1] is { JudgmentTitle: "铁骑", Detail: "黑色 · 可正常响应" } &&
            tieqiCues[2] is { Detail: "判定花色已确定" },
            "Only the verified classic Tieqi reason may interpret red as prohibiting Dodge; a later suit-payment decision cannot borrow that result.");
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
               responseCues[1] is { Label: "连续响应 1 / 2", Detail: "仍需 1 张【闪】", ResponseCount: 1,
                   RequiredResponseCount: 2, RequiredResponseCardKind: CardKind.Dodge, ResponseResolutionId: 20, HasResponseProgress: true } &&
               responseCues[2] is { Label: "无懈可击询问中 · 第 1 层", Detail: "当前锦囊生效中 · 可令其失效" } &&
               responseCues[3] is { Kind: BattleCueKind.Response, Label: "打出无懈可击 · 第 1 层", Detail: "锦囊暂时失效" } &&
               responseCues[4] is { Label: "无懈可击询问中 · 第 2 层", Detail: "当前锦囊已失效 · 可反制恢复" } &&
               responseCues[5] is { SourceSeat: -1, Label: "决斗 · 已失效", Detail: "无懈链共 1 次响应" },
            "Response request, locked progress, or layered Nullification state was not projected exactly.");
        Assert(responseCues.Select(cue => cue.IsInquiry).SequenceEqual(new[] { true, false, true, false, true, false }),
            "Response progress, a played response or the final outcome was treated as an unanswered inquiry.");
        Assert(responseCues.Where(cue => cue.Sequence != responseCues[1].Sequence).All(cue =>
                !cue.HasResponseProgress && cue.ResponseCount == 0 && cue.RequiredResponseCount == 0 &&
                cue.RequiredResponseCardKind is null && cue.ResponseResolutionId is null),
            "An inquiry, played counterspell or final result acquired progress from another public event.");

        // Public event projection only; native paid-response timing is covered by
        // the response fixture. Neither the viewer's prompt nor an incoming attack
        // can turn counted cards into a final immunity or evasion result.
        var projectionState = SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true));
        var countedResponses = new[]
        {
            Envelope(new CardRespondedEvent(5001, 1, 0, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(60, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2)),
            Envelope(new CardRespondedEvent(5002, 1, 0, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(60, 0, 1, CardKind.Slash, CardKind.Dodge, 2, 2))
        };
        var countedCues = BattleCueProjector.Project(countedResponses, view);
        var dodgeProgress = countedCues.Where(cue => cue.HasResponseProgress).ToArray();
        Assert(countedCues.Count == 4 && dodgeProgress.Length == 2 &&
               dodgeProgress[0] is { Kind: BattleCueKind.ResponseWindow, SourceSeat: 1, TargetSeats: [0],
                   Label: "连续响应 1 / 2", Detail: "仍需 1 张【闪】", ResponseCount: 1, RequiredResponseCount: 2,
                   RequiredResponseCardKind: CardKind.Dodge, ResponseResolutionId: 60, IsInquiry: false } &&
               dodgeProgress[1] is { Label: "连续响应 2 / 2", Detail: "所需【闪】已打齐", ResponseCount: 2,
                   RequiredResponseCount: 2, RequiredResponseCardKind: CardKind.Dodge, ResponseResolutionId: 60,
                   HpDelta: 0, CardKind: null, PublicCardId: null } &&
               BattleCueProjector.ProjectCardPlays(countedResponses, view).Count == 2 &&
               countedCues.Select(cue => cue.Sequence).Distinct().Count() == countedCues.Count,
            "Paid Dodge faces or exact public progress were duplicated, associated with a different group, or promoted to a final evasion result.");
        var slashProgress = BattleCueProjector.Project(
        [
            Envelope(new RequiredResponseProgressEvent(61, 2, 1, CardKind.Duel, CardKind.Slash, 1, 2)),
            Envelope(new RequiredResponseProgressEvent(61, 2, 1, CardKind.Duel, CardKind.Slash, 2, 2))
        ], view);
        Assert(slashProgress.Count == 2 && slashProgress[0] is
                { Detail: "仍需 1 张【杀】", RequiredResponseCardKind: CardKind.Slash, ResponseResolutionId: 61,
                    SourceSeat: 1, TargetSeats: [2], HasResponseProgress: true } &&
               slashProgress[1] is { Detail: "所需【杀】已打齐", ResponseCount: 2, RequiredResponseCount: 2,
                   RequiredResponseCardKind: CardKind.Slash, ResponseResolutionId: 61, HasResponseProgress: true },
            "Consecutive Slash progress used the incoming Duel or a previous Dodge rather than its exact counted card kind.");
        foreach (var (count, required) in new[] { (0, 2), (-1, 2), (3, 2), (2, 0), (2, -1), (int.MaxValue, 2) })
        {
            var invalid = BattleCueProjector.Project(
                [Envelope(new RequiredResponseProgressEvent(62, 0, 1, CardKind.Slash, CardKind.Dodge, count, required))], view).Single();
            Assert(!invalid.HasResponseProgress && invalid.ResponseCount == count && invalid.RequiredResponseCount == required &&
                   invalid.ResponseResolutionId == 62 && invalid.Detail == "已打出【闪】",
                "Invalid public counts invented remaining responses or a completed response result.");
        }
        var progress = dodgeProgress[0];
        Assert(!(progress with { IsInquiry = true }).HasResponseProgress &&
               !(progress with { Kind = BattleCueKind.Response }).HasResponseProgress &&
               !(progress with { RequiredResponseCardKind = null }).HasResponseProgress &&
               !(progress with { RequiredResponseCount = 1 }).HasResponseProgress,
            "Finite response progress escaped its exact non-inquiry multiple-response contract.");

        // These public terminal facts declare an equipment effect, rather than
        // a weapon card play, a private payment preview or a future HP result.
        var weaponEvents = new[]
        {
            Envelope(new StoneAxeResolvedEvent(70, 1, 2, true, [4001, 4002])),
            Envelope(new StoneAxeResolvedEvent(71, 1, 2, false, [])),
            Envelope(new QinglongCrescentBladeResolvedEvent(72, 1, 2, true, [3001])),
            Envelope(new QinglongCrescentBladeResolvedEvent(73, 1, 2, true, [3002, 3003], CardKind.FireSlash)),
            Envelope(new QinglongCrescentBladeResolvedEvent(74, 1, 2, true, [], CardKind.ThunderSlash)),
            Envelope(new QinglongCrescentBladeResolvedEvent(75, 1, 2, false, []))
        };
        var weaponCues = BattleCueProjector.Project(weaponEvents, view);
        Assert(weaponCues.Count == 6 && weaponCues.All(cue => cue.Kind == BattleCueKind.Weapon &&
                cue.SourceSeat == 1 && cue.TargetSeats.SequenceEqual([2]) && !cue.IsInquiry && !cue.HasResponseProgress &&
                cue.CardKind is null && cue.PublicCardId is null && cue.PublicCard is null &&
                cue.HpDelta == 0 && cue.RemainingHp is null && cue.MaximumHp is null) &&
               weaponCues[0] is { WeaponKind: CardKind.StoneAxe, WeaponResolutionId: 70, IsWeaponActivated: true,
                   Label: "贯石斧 · 破闪", Detail: "弃置 2 张牌 · 此杀继续结算" } &&
               weaponCues[1] is { WeaponKind: CardKind.StoneAxe, WeaponResolutionId: 71, IsWeaponActivated: false,
                   Label: "贯石斧 · 未发动", Detail: "此次未发动" } &&
               weaponCues[2] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 72, IsWeaponActivated: true,
                   Label: "青龙偃月刀 · 追击", Detail: "已追加【杀】使用" } &&
               weaponCues[3] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 73, IsWeaponActivated: true,
                   Detail: "已追加【火杀】使用" } &&
               weaponCues[4] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 74, IsWeaponActivated: true,
                   Detail: "已追加【雷杀】使用" } &&
               weaponCues[5] is { WeaponKind: CardKind.QinglongCrescentBlade, WeaponResolutionId: 75, IsWeaponActivated: false,
                   Label: "青龙偃月刀 · 未发动", Detail: "此次放弃追击" } &&
               BattleCueProjector.ProjectCardPlays(weaponEvents, view).Count == 0,
            "A public weapon result lost its own activation/effective kind, rejected zero-material pursuit, fabricated a weapon play or promised future damage/immunity.");
        Assert(BattleCueProjector.Project(privateEvents.Concat(weaponEvents), view).Where(cue => cue.Kind != BattleCueKind.Draw).Select(cue => cue.Sequence)
                .SequenceEqual(weaponCues.Select(cue => cue.Sequence)) &&
               cues.Concat(responseCues).All(cue => cue.WeaponKind is null && cue.WeaponResolutionId is null && !cue.IsWeaponActivated),
            "Hidden setup/hand movement or another public cue was promoted to an equipment effect.");
        var publicFacts = countedResponses.Concat(weaponEvents).ToArray();
        var factCues = BattleCueProjector.Project(publicFacts, view);
        var foreignCues = BattleCueProjector.Project(publicFacts, Engine(vm).CreateSnapshot(1));
        Assert(factCues.Zip(foreignCues).All(pair => pair.First.Sequence == pair.Second.Sequence &&
                pair.First.Kind == pair.Second.Kind && pair.First.SourceSeat == pair.Second.SourceSeat &&
                pair.First.TargetSeats.SequenceEqual(pair.Second.TargetSeats) && pair.First.Label == pair.Second.Label &&
                pair.First.Detail == pair.Second.Detail && pair.First.ResponseCount == pair.Second.ResponseCount &&
                pair.First.RequiredResponseCount == pair.Second.RequiredResponseCount &&
                pair.First.RequiredResponseCardKind == pair.Second.RequiredResponseCardKind &&
                pair.First.ResponseResolutionId == pair.Second.ResponseResolutionId && pair.First.HasResponseProgress == pair.Second.HasResponseProgress &&
                pair.First.WeaponKind == pair.Second.WeaponKind && pair.First.WeaponResolutionId == pair.Second.WeaponResolutionId &&
                pair.First.IsWeaponActivated == pair.Second.IsWeaponActivated) &&
               factCues.Count == foreignCues.Count && projectionState == SnapshotJson.Serialize(Engine(vm).CreateSnapshot(0, true)),
            "A foreign safe viewer changed public counted-response/equipment facts or projection mutated the engine.");
        var bulkProgress = Enumerable.Range(0, 40).Select(index =>
            Envelope(new RequiredResponseProgressEvent(100 + index, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2))).ToArray();
        var boundedProgress = BattleCueProjector.Project(bulkProgress, view);
        Assert(boundedProgress.Count == 12 && boundedProgress.Select(cue => cue.Sequence).SequenceEqual(
                bulkProgress.TakeLast(12).Select(envelope => envelope.Sequence)) &&
               boundedProgress.All(cue => cue.HasResponseProgress),
            "Typed progress changed ordinary bulk retention or duplicated a public event.");
        var bulkWeapons = Enumerable.Range(0, 40).Select(index =>
            Envelope(new StoneAxeResolvedEvent(200 + index, 1, 2, true, [4001, 4002]))).ToArray();
        var boundedWeapons = BattleCueProjector.Project(bulkWeapons, view);
        Assert(boundedWeapons.Count == 12 && boundedWeapons.Select(cue => cue.Sequence).SequenceEqual(
                bulkWeapons.TakeLast(12).Select(envelope => envelope.Sequence)) &&
               boundedWeapons.All(cue => cue.Kind == BattleCueKind.Weapon && cue.IsWeaponActivated),
            "Public weapon feedback changed ordinary retention or duplicated a terminal fact.");
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

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
