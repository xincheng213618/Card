@{
    # Routine checks use existing name filters; each runner executes their union once.
    # Full validation retains every registration, including individual content scenarios.
    Core = @(
        'command', 'observer', 'viewer', 'prepared snapshot', 'internal failure',
        'skill program', 'lifecycle programs', 'shared post-event',
        'shared use lifecycle', 'shared card-use', 'card movement', 'card inventory',
        'initial deal', 'invalid single and batch', 'draw and recover',
        'execution plans', 'skill executor', 'program gameplay hashes',
        'physical deck recipes', 'structured skill metadata', 'content registry',
        'standard package', 'composition kernel', 'rule query', 'slash resolution',
        'dying response', 'Xingshang', 'response use completion', 'selected gift',
        'configured active sequences', 'standard active programs', 'owned-card set',
        'turn card-use effects', 'active Program contracts', 'hand guidance',
        'actual marker payment', 'replacement draw and dying payment', 'two dynamic actual ends',
        'ordered nested native owner', 'payment nested comparison', 'first target history',
        'program conversion polarity shared state and replay',
        'Feng Lin Lu Zhi actual discard entry boundaries',
        'Ending child gift joins current boundary'
    )
    Wpf = @(
        'original card artwork', 'composed skills', 'conversion choices',
        'response context', 'hand responses', 'opaque target-card',
        'playback batches', 'saved UI boundaries', 'failed writes', 'public markers',
        'deferred hand alignment', 'Program conversion polarity stays visible'
    )
}
