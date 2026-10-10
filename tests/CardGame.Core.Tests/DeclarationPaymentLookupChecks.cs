using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DeclarationPaymentLookupChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Unclaimed = Method("UnclaimedDeclarationPayment");
    private static readonly MethodInfo Available = Method("AvailableDeclarationCard");
    private static readonly MethodInfo Origin = Method("CapturedDeclarationOrigin");
    private static readonly MethodInfo Description = Method("PublicDeclarationDescription");
    private static readonly MethodInfo Live = Method("LiveDeclarationPayment");

    public static void ReverseLookupsPreserveOriginalReceiptPrecedence()
    {
        // This deliberately assembled host state compares lookup semantics; it
        // does not represent accepted commands or claim command-replay coverage.
        var registry = ContentRegistry.Build(new StandardContentPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, ModeId = "identity:standard-5", UseInteractiveSetup = true
        }, registry);
        var frames = Field<FrameStore>(game, "_resolutionStack");
        var zones = Field<CardZoneStore>(game, "_cardZones");
        var players = Field<IReadOnlyList<CharacterState>>(game, "_players");
        var cards = zones.CardsAt(CardLocation.DrawPile).Take(4).ToArray();
        zones.Move(cards[0].Id, CardLocation.DrawPile, CardLocation.Processing);
        zones.Move(cards[1].Id, CardLocation.DrawPile, CardLocation.Processing);
        zones.Move(cards[2].Id, CardLocation.DrawPile, CardLocation.Hand(1));
        zones.Move(cards[3].Id, CardLocation.DrawPile, CardLocation.DiscardPile);
        var missing = new Card(1_000_000, CardKind.Dodge, Suit.Club, 7);
        var queryCards = cards.Append(missing).ToArray();
        var rootPayment = Payment(1, 0, cards[0], CardLocation.Hand(0));
        var topPayment = Payment(3, 0, cards[1], CardLocation.Equipment(0));

        Check("empty");
        Set(new CardUseFrame(1, 0, cards[0].Id, CardKind.Slash, [1]),
            new ResponseWindowFrame(2, 1, 0, 1, CardKind.Slash, RequiredCardKind: CardKind.Dodge));
        Check("no receipts");

        var applying = Declaration(2, rootPayment with { DeclarationId = 2, OwnerFrameId = 2 }, CardDeclarationStage.Applying);
        var top = Accepted(3, topPayment);
        Set(applying, top);
        Require(ReferenceEquals(Invoke(Available, players[0]), cards[0]),
            "An older Applying declaration must outrank every newer accepted receipt.");
        Check("Applying segment has priority");

        var laterApplying = Declaration(4, Payment(4, 0, cards[1], CardLocation.Hand(0)), CardDeclarationStage.Applying);
        Set(applying, laterApplying, Accepted(5, Payment(5, 0, cards[0], CardLocation.Hand(0))));
        Require(ReferenceEquals(Invoke(Available, players[0]), cards[1]),
            "Within the Applying segment the newest eligible declaration must win.");
        Check("newest Applying declaration");
        Set(applying, laterApplying with { Payment = laterApplying.Payment with { Claimed = true } }, top);
        Require(ReferenceEquals(Invoke(Available, players[0]), cards[0]),
            "A claimed newer declaration must not hide an older eligible Applying receipt.");
        Check("claimed newer Applying declaration");

        Set(applying with { Payment = applying.Payment with { Cost = new(missing.Id, missing.Kind, CardLocation.Hand(0)) } }, top);
        Require(Invoke(Available, players[0]) is null,
            "The selected declaration's absent entity must not fall back to a different receipt.");
        Check("selected missing entity");

        Set(Accepted(1, rootPayment), top with { AcceptedDeclarationPayment = topPayment with
            { Cost = new(cards[2].Id, cards[2].Kind, CardLocation.Hand(0)) } });
        Require(Invoke(Available, players[0]) is null,
            "The newest accepted receipt outside Processing must not fall back to an older processing entity.");
        Check("selected entity outside Processing");

        foreach (var stage in Enum.GetValues<CardDeclarationStage>())
        foreach (var claimed in new[] { false, true })
        foreach (var revealed in new[] { false, true })
        {
            var provider = ((int)stage + (claimed ? 1 : 0)) % 2;
            var card = cards[(int)stage % cards.Length];
            var declarationPayment = Payment(2, provider, card,
                revealed ? CardLocation.Judgment(provider) : CardLocation.Hand(provider), claimed, revealed);
            var declaration = Declaration(2, declarationPayment, stage) with
            {
                AcceptedDeclarationPayment = Payment(2, 1 - provider, cards[0], CardLocation.Authority(1 - provider))
            };
            Set(Accepted(1, rootPayment), declaration, Accepted(3, topPayment with
                { Claimed = revealed, IsRevealed = claimed }),
                new ResponseWindowFrame(4, 3, 0, 1, CardKind.Slash, RequiredCardKind: CardKind.Dodge));
            Check($"{stage}, claimed={claimed}, revealed={revealed}");
        }

        // On an Applying frame its own payment replaces, rather than falls
        // back to, AcceptedDeclarationPayment in four of the five projections.
        Set(applying with { Payment = applying.Payment with { Claimed = true }, AcceptedDeclarationPayment = topPayment });
        Require(Invoke(Unclaimed, 0, cards[1].Id) is null &&
                Equals(Invoke(Origin, cards[1].Id, CardLocation.DrawPile), CardLocation.DrawPile) &&
                Invoke(Live, cards[1].Id) is null,
            "A declaration's accepted receipt cannot leak through its overriding payment projection.");
        Require(ReferenceEquals(Invoke(Available, players[0]), cards[1]),
            "Available-card lookup retains accepted receipts on declarations in its separate first segment.");
        Check("Applying payment overrides same-frame accepted receipt");

        // The last matching receipt, including all source/color metadata, must
        // survive even when different receipts name the same physical entity.
        Set(Accepted(1, rootPayment), Accepted(3, Payment(3, 1, cards[0], CardLocation.Equipment(1))));
        Check("same entity, distinct sources and origins");
        while (frames.LastOrDefault() is { } frame) frames.CompleteTop(frame.Id, frame.Kind);

        DeclaredCardPayment Payment(long id, int provider, Card card, CardLocation from,
            bool claimed = false, bool revealed = false) =>
            new(id, id, provider, new(card.Id, card.Kind, from, id % 2 == 0),
                new("lookup:source-" + id, "binding-" + id, provider, "instance-" + id),
                id % 2 == 0 ? CardKind.DrawTwo : CardKind.Slash, claimed, revealed,
                ActorSeat: 1 - provider, FrozenSuit: id % 2 == 0 ? Suit.Heart : Suit.Spade);

        CardUseFrame Accepted(long id, DeclaredCardPayment payment) =>
            new(id, 0, payment.Cost.CardId, CardKind.Slash, [1]) { AcceptedDeclarationPayment = payment };

        CardDeclarationFrame Declaration(long id, DeclaredCardPayment payment, CardDeclarationStage stage) =>
            new(id, payment.ProviderSeat, 1, payment,
                new(CardDeclarationPurpose.Use, 0, null, null, 1 - payment.ProviderSeat), [1], stage);

        void Set(params ResolutionFrame[] values)
        {
            while (frames.LastOrDefault() is { } frame) frames.CompleteTop(frame.Id, frame.Kind);
            foreach (var frame in values) frames.Push(frame);
        }

        void Check(string scenario)
        {
            var beforeFrames = JsonSerializer.Serialize(frames);
            var beforeZones = JsonSerializer.Serialize(game.CreateCardZoneDiagnostics());
            var revision = game.Revision;
            foreach (var owner in players.Take(3))
            {
                Same(() => OriginalAvailable(owner), () => Invoke(Available, owner), "available");
                foreach (var card in queryCards)
                    Same(() => OriginalUnclaimed(owner.Seat, card.Id), () => Invoke(Unclaimed, owner.Seat, card.Id), "unclaimed");
            }
            foreach (var card in queryCards)
            {
                Same(() => OriginalOrigin(card.Id, CardLocation.DrawPile), () => Invoke(Origin, card.Id, CardLocation.DrawPile), "origin");
                Same(() => OriginalDescription(card, "ordinary"), () => Invoke(Description, card, "ordinary"), "description");
                Same(() => OriginalLive(card.Id), () => Invoke(Live, card.Id), "live");
            }
            Require(JsonSerializer.Serialize(frames) == beforeFrames &&
                    JsonSerializer.Serialize(game.CreateCardZoneDiagnostics()) == beforeZones && game.Revision == revision,
                "Payment lookups must leave host state unchanged: " + scenario);

            void Same(Func<object?> original, Func<object?> current, string lookup)
            {
                Require(Capture(original) == Capture(current), lookup + " lookup diverged: " + scenario);
            }
        }

        // Keep the prior LINQ definitions as the reference, including their
        // distinct stage, claimed, provider and Processing restrictions.
        DeclaredCardPayment? OriginalUnclaimed(int provider, int cardId) =>
            frames.Select(frame => frame is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration
                    ? declaration.Payment : frame.AcceptedDeclarationPayment)
                .LastOrDefault(payment => payment is { Claimed: false } && payment.ProviderSeat == provider &&
                    payment.Cost.CardId == cardId && zones.GetLocation(cardId) == CardLocation.Processing);

        Card? OriginalAvailable(CharacterState owner) =>
            frames.Select(frame => frame.AcceptedDeclarationPayment)
                .Concat(frames.OfType<CardDeclarationFrame>().Where(frame => frame.Stage == CardDeclarationStage.Applying).Select(frame => frame.Payment))
                .LastOrDefault(payment => payment is { Claimed: false } && payment.ProviderSeat == owner.Seat) is { } receipt
                ? zones.CardsAt(CardLocation.Processing).SingleOrDefault(card => card.Id == receipt.Cost.CardId) : null;

        CardLocation OriginalOrigin(int cardId, CardLocation actual) =>
            frames.Select(frame => frame is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration
                    ? declaration.Payment : frame.AcceptedDeclarationPayment)
                .LastOrDefault(payment => payment is { Claimed: false } && payment.Cost.CardId == cardId)?.Cost.From ?? actual;

        string OriginalDescription(Card card, string ordinary)
        {
            var paid = frames.Select(frame => frame is CardDeclarationFrame declaration ? declaration.Payment : frame.AcceptedDeclarationPayment)
                .LastOrDefault(payment => payment is { IsRevealed: false } && payment.Cost.CardId == card.Id);
            return paid is not null && zones.GetLocation(card.Id) == CardLocation.Processing
                ? "声明【" + CardCatalog.Get(paid.DeclaredKind).DisplayName + "】" : ordinary;
        }

        DeclaredCardPayment? OriginalLive(int cardId) =>
            frames.Select(frame => frame is CardDeclarationFrame declaration ? declaration.Payment : frame.AcceptedDeclarationPayment)
                .LastOrDefault(payment => payment?.Cost.CardId == cardId && zones.GetLocation(cardId) == CardLocation.Processing);

        object? Invoke(MethodInfo method, params object?[] arguments)
        {
            try { return method.Invoke(game, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }

    private static MethodInfo Method(string name) => typeof(GameEngine).GetMethod(name, Flags)!;
    private static T Field<T>(GameEngine game, string name) => (T)typeof(GameEngine).GetField(name, Flags)!.GetValue(game)!;
    private static LookupOutcome Capture(Func<object?> query)
    {
        try { return new(query(), null, null); }
        catch (InvalidOperationException error) { return new(null, error.GetType(), error.Message); }
    }

    private readonly record struct LookupOutcome(object? Value, Type? Error, string? Message);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
