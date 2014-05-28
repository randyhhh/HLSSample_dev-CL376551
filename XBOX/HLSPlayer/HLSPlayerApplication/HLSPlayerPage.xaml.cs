using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverlight.Samples.HttpLiveStreaming;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Documents;
using System.Collections.ObjectModel;
using Microsoft.SilverlightMediaFramework.Core.Media;

namespace HLSPlayerApplication
{
    
    public partial class HLSPlayerPage : Page
    {
        public HLSPlayerPage()
        {
            InitializeComponent();
            Player.Loaded += new RoutedEventHandler(Player_Loaded);
            LayoutRoot.GamePadButtonDown += new EventHandler<GamePadButtonEventArgs>(LayoutRoot_GamePadButtonDown);
            Player.PlayStateChanged += new EventHandler<Microsoft.SilverlightMediaFramework.Core.CustomEventArgs<Microsoft.SilverlightMediaFramework.Plugins.Primitives.MediaPluginState>>(Player_PlayStateChanged);
        }

        void LayoutRoot_GamePadButtonDown(object sender, GamePadButtonEventArgs e)
        {
            GamePadButtonDown += new EventHandler<GamePadButtonEventArgs>(HLSPlayerPage_GamePadButtonDown);

        }

        void HLSPlayerPage_GamePadButtonDown(object sender, GamePadButtonEventArgs e)
        {
           
        }

        void Player_PlayStateChanged(object sender, Microsoft.SilverlightMediaFramework.Core.CustomEventArgs<Microsoft.SilverlightMediaFramework.Plugins.Primitives.MediaPluginState> e)
        {
            Console.WriteLine("State: ----------------{0}--------------", e.Value.ToString());
        }

        void Player_Loaded(object sender, RoutedEventArgs e)
        {
            Player.Back += new EventHandler(Player_Back);
            var playlist = new ObservableCollection<PlaylistItem>();
            var playlistItem = new PlaylistItem()
            {
                MediaSource = new Uri("http://devimages.apple.com/iphone/samples/bipbop/bipbopall.m3u8"),
                MediaAssetId = String.Format("http://gaiamtv.com/TV/{0}", "HLSPlayerPage"),
                MediaType = "application/x-mpegURL",
                //LiveDvrRequired = false,
                //JumpToLive = true,
                 DeliveryMethod= Microsoft.SilverlightMediaFramework.Plugins.Primitives.DeliveryMethods.AdaptiveStreaming
            };
            playlist.Add(playlistItem);
            Player.Playlist = playlist;
            Player.Play();
        }

        void Player_Back(object sender, EventArgs e)
        {
            
                if (NavigationService.CanGoBack)
                {
                    NavigationService.GoBack();
                }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }
    }
}
