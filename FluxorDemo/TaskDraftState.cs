using Fluxor;

namespace StateManagementPoc.FluxorDemo;

[FeatureState]
public record TaskDraftState
{
    public string Title { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
}
