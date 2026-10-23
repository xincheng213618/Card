@{
    # Routine smoke checks target about one second of test-process time.
    # Engine, replay, privacy, card movement and WPF checks use focused name filters.
    # Keep those registrations available; Full remains explicitly manual only.
    Core = @(
        'winner rules cover all three camps',
        'skill program loader separates canonical gameplay from presentation',
        'skill program loader rejects malformed and unsupported definitions',
        'content registry rejects duplicate ids and bad references'
    )
    Wpf = @()
}
