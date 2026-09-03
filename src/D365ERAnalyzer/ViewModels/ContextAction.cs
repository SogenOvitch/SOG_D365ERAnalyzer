namespace D365ERAnalyzer.ViewModels;

/// <summary>
/// One entry in a right-click submenu. Each carries its own command so the menu item template
/// needs nothing but <c>Display</c> and <c>Command</c> — no reaching back up the visual tree from
/// inside a popup that is not part of it.
/// </summary>
public sealed class ContextAction
{
    public required string Display { get; init; }
    public required RelayCommand Command { get; init; }
}
