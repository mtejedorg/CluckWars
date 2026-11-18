namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The seam between the main menu (Presentation) and the dev-tools balance editor (Diagnostics, above it): the menu
    /// asks for the editor to open, nothing more. Bound only by <c>ProjectInstaller.InstallDiagnostics</c>, so a production
    /// build has no implementation and the menu's button stays hidden (<c>[InjectOptional]</c> null).
    /// </summary>
    public interface IDevBalanceEditor
    {
        /// <summary>Shows the editor over the current screen. Does nothing without <see cref="DevTools.Available"/>.</summary>
        void Open();
    }
}
