namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// Defines all lifecycle events a plugin can subscribe to.
    /// </summary>
    public enum PluginTrigger
    {
        /// <summary>Called once when the application finishes launching.</summary>
        OnStartup,

        /// <summary>Called just before the application exits.</summary>
        OnShutdown,

        /// <summary>Called after sing-box starts successfully.</summary>
        OnCoreStarted,

        /// <summary>Called after sing-box stops.</summary>
        OnCoreStopped,

        /// <summary>
        /// Called before sing-box starts. Plugin receives the JSON config string
        /// and must return a (possibly modified) JSON config string.
        /// </summary>
        OnBeforeCoreStart,

        /// <summary>Called just before sing-box is about to be killed.</summary>
        OnBeforeCoreStop,

        /// <summary>
        /// Called after a subscription is downloaded and parsed.
        /// Plugin receives an array of node objects and may return a filtered/modified array.
        /// </summary>
        OnSubscribe,

        /// <summary>Triggered manually by the user from the Plugins page.</summary>
        OnManual,
    }
}
