namespace StateManagementPoc.FluxorDemo;

/// <summary>
/// Represents the user clicking a tag. Carries no logic — purely describes "this happened."
/// </summary>
public record SelectTagAction(string TagName);
