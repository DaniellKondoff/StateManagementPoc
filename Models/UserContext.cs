namespace StateManagementPoc.Models;

/// <summary>
/// Value distributed via CascadingValueSource in the cascading-value demo.
/// Intentionally minimal; unrelated to any real auth system in this PoC.
/// </summary>
public class UserContext
{
    public bool IsAdmin { get; set; }
}
