using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Xbox.Input;

namespace HLSXBOXlayer
{
    public partial class MainPage : Page
    {
        private GridFocusHelper m_gridFocusHelper;
        public MainPage()
        {
            InitializeComponent();

            // Use a Grid focus helper to allow setting the focus on the MainPage buttons 
            // using the gamepad left and right DPad buttons.
            m_gridFocusHelper = new GridFocusHelper(ButtonsGrid);

            this.Loaded += new RoutedEventHandler(MainPage_Loaded);
        }

        // Executes when the user navigates to this page.
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            GC.Collect();
        }

        void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Button1.Focus();
        }

        private void Button1_Click(object sender, RoutedEventArgs e)
        {
            //string target = "/FullScreenPlayer.xaml";
            //target += string.Format("?SourceURI={0}", Uri.EscapeDataString("http://devimages.apple.com/iphone/samples/bipbop/bipbopall.m3u8"));
            string target = "/HLSPlayerPage.xaml";
            target += string.Format("?SourceURI={0}", Uri.EscapeDataString("http://devimages.apple.com/iphone/samples/bipbop/bipbopall.m3u8"));
            GC.Collect();
            NavigationService.Navigate(new Uri(target, UriKind.Relative));
        }


        private void Button2_Click(object sender, RoutedEventArgs e)
        {
            string target = "/FullScreenPlayer.xaml";
            target += string.Format("?SourceURI={0}", Uri.EscapeDataString("http://www.nasa.gov/multimedia/nasatv/NTV-Public-IPS.m3u8"));
            GC.Collect();
            NavigationService.Navigate(new Uri(target, UriKind.Relative));
        }


    }
}
