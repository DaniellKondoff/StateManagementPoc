using Fluxor;

namespace StateManagementPoc.FluxorDemo;

public static class TaskDraftReducer
{
    [ReducerMethod]
    public static TaskDraftState OnSetTitle(TaskDraftState state, SetTaskTitleAction action)
        => state with { Title = action.Title };

    [ReducerMethod]
    public static TaskDraftState OnSetPriority(TaskDraftState state, SetTaskPriorityAction action)
        => state with { Priority = action.Priority };

    [ReducerMethod]
    public static TaskDraftState OnClearTaskDraft(TaskDraftState state, ClearTaskDraftAction action)
        => state with { Title = string.Empty, Priority = string.Empty };
}
