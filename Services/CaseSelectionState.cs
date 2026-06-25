using StateManagementPoc.Models;

namespace StateManagementPoc.Services;

/// <summary>
/// Per-circuit in-memory state container for the currently selected case.
/// Registered as Scoped — one instance per SignalR circuit (i.e. per browser tab).
/// Do NOT register as Singleton: a singleton would share state across all users' circuits.
/// </summary>
public class CaseSelectionState
{
    private CaseDto? _selectedCase;

    /// <summary>Raised whenever SelectedCase changes.</summary>
    public event Action? OnChange;

    public CaseDto? SelectedCase
    {
        get => _selectedCase;
        set
        {
            _selectedCase = value;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
