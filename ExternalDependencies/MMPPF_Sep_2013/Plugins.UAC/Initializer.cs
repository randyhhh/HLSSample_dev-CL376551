using System;
using System.ComponentModel.Composition;
using System.Windows;
using Microsoft.SilverlightMediaFramework.Plugins.Metadata;

namespace Microsoft.SilverlightMediaFramework.Plugins.UAC
{
    /// <summary>
    /// Provides SMF with the ability to parse and display DFXP formatted Timed Text captions arriving over in-stream data tracks.
    /// </summary>
    [ExportGenericPlugin(PluginName = PluginName, PluginDescription = PluginDescription, PluginVersion = PluginVersion)]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class Initializer : IGenericPlugin
    {
        #region IPlugin Members
        private const string PluginName = "Microsoft.Advertising.UAC.Initializer";
        private const string PluginDescription = "Provides SMF with the ability to use the Microsoft UAC ad provider.";
        private const string PluginVersion = "2.2012.1005.0";
        #endregion

        public const string Key_AppId = "Microsoft.Advertising.ApplicationID";

        private bool isLoaded;
        public bool IsLoaded
        {
            get { return isLoaded; }
        }
        // Disable event not used warning
#pragma warning disable 67
        public event Action<IPlugin, Microsoft.SilverlightMediaFramework.Plugins.Primitives.LogEntry> LogReady;
#pragma warning restore 67

        public event Action<IPlugin> PluginLoaded;

        public event Action<IPlugin> PluginUnloaded;

        public event Action<IPlugin, Exception> PluginLoadFailed;

        public event Action<IPlugin, Exception> PluginUnloadFailed;

        public void Load()
        {
            try
            {
                if (player != null)
                {
                    //
                    // Initialize the advertising system
                    //
                    if (player.GlobalConfigMetadata.ContainsKey(Key_AppId))
                    {
                        var appId = player.GlobalConfigMetadata[Key_AppId] as string;
                    }
                }
            }
            catch (Exception ex)
            {
                if (PluginLoadFailed != null) PluginLoadFailed(this, ex);
                return;
            }
            isLoaded = true;
            if (PluginLoaded != null) PluginLoaded(this);
        }

        public void Unload()
        {
            try
            {
                player = null;
            }
            catch (Exception ex)
            {
                if (PluginUnloadFailed != null) PluginUnloadFailed(this, ex);
                return;
            }

            isLoaded = false;
            if (PluginUnloaded != null) PluginUnloaded(this);
        }

        private IPlayer player;
        public void SetPlayer(FrameworkElement Player)
        {
            player = Player as IPlayer;
        }
    }
}
