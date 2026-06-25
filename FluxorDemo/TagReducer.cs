using Fluxor;

namespace StateManagementPoc.FluxorDemo;

/// <summary>
/// Pure function — no side effects, no async. Toggles a tag in/out of SelectedTags,
/// always returning a new TagState rather than mutating the existing list.
/// </summary>
public static class TagReducer
{
    [ReducerMethod]
    public static TagState OnSelectTag(TagState state, SelectTagAction action)
    {
        if (state.SelectedTags.Contains(action.TagName))
            return state with { SelectedTags = state.SelectedTags.Where(t => t != action.TagName).ToList() };

        return state with { SelectedTags = state.SelectedTags.Append(action.TagName).ToList() };
    }

    [ReducerMethod(typeof(ClearTagsAction))]
    public static TagState OnClearTags(TagState state)
        => state with { SelectedTags = [] };
}
