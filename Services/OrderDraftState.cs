using StateManagementPoc.Models;

namespace StateManagementPoc.Services;

/// <summary>
/// Holds in-progress, not-yet-confirmed form data across a multi-step flow.
/// Intentionally has no relationship to <see cref="CaseSelectionState"/> or CaseRepository.
/// Must be registered as Scoped (not Singleton) in Blazor Server — one instance per SignalR
/// circuit — to avoid leaking one user's in-progress draft into another user's circuit.
/// </summary>
public class OrderDraftState
{
    private OrderDto? _draft;

    public event Action? OnChange;

    public OrderDto? Draft
    {
        get => _draft;
        set { _draft = value; NotifyStateChanged(); }
    }

    public void Clear()
    {
        _draft = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
