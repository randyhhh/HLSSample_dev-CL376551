using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;

namespace HLSPlayerApplication
{
    public partial class App : Application
    {
        public XboxApplicationFrame RootFrame { get; private set; }
        public Uri HomePage { get; private set; }

        public App()
        {
            // Global handler for uncaught exceptions. 
            this.UnhandledException += this.Application_UnhandledException;
            this.Startup += this.Application_Startup;
            this.Exit += this.Application_Exit;

            InitializeComponent();
            InitializeXboxApplication();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            RootVisual = RootFrame;
        }

        private void Application_Exit(object sender, EventArgs e)
        {

        }

        private void RootFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                // A navigation has failed; break into the debugger
                System.Diagnostics.Debugger.Break();
            }
        }

        // Code to execute on Unhandled Exceptions
        private void Application_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("App_UnhandledException() at {0}:", DateTime.Now);
            for (Exception ex = e.ExceptionObject; ex != null; ex = ex.InnerException)
            {
                Debug.WriteLine("{0}: {1}", ex.GetType(), ex.Message);
                Debug.WriteLine(ex.StackTrace);

                if (ex.InnerException != null)
                    Debug.WriteLine(".InnerException:");
            }

            if (System.Diagnostics.Debugger.IsAttached)
            {
                // An unhandled exception has occurred; break into the debugger
                System.Diagnostics.Debugger.Break();
            }
        }

        #region Xbox Application Initialization

        private bool xboxApplicationInitialized = false;

        private void InitializeXboxApplication()
        {
            if (xboxApplicationInitialized)
                return;

            RootFrame = new XboxApplicationFrame();
            RootFrame.NavigationFailed += RootFrame_NavigationFailed;

            HomePage = new Uri("/MainPage.xaml", UriKind.Relative);
            RootFrame.Source = HomePage;

            xboxApplicationInitialized = true;
        }

        #endregion

    }
}
