using Fluxor;

namespace StateManagementPoc.FluxorDemo;

/// <summary>
/// Immutable state slice for the tag-selection demo. Never mutated in place —
/// all changes produce a new TagState instance via the reducer.
/// </summary>
[FeatureState]
public record TagState
{
    public IReadOnlyList<string> SelectedTags { get; init; } = Array.Empty<string>();
}
