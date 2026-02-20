///////////////////////////////////////////////////////////////////////////////
//
// Copyright (c) 2016-2026 VASTreaming
//
// Licensee is granted permission to use, copy and modify this file.
// Licensee can distribute and sell this file in a binary form as a part of the
// licensee's product. Licensee is prohibited from selling this file and
// library separately from the licensee's products. Licensee is prohibited from
// disclosing this file to any 3rd party. Licensee is prohibited from openly
// publishing this file as a part of open-source software or any other means.
//
///////////////////////////////////////////////////////////////////////////////

namespace VAST.Demo
{

    using System.Runtime.Versioning;

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class App : Application
    {

        public App()
        {
            InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = new Window(new AppShell());
            if (window != null)
            {
                window.Title = "VASTreaming MAUI Demo";
                window.Width = 1920;
                window.Height = 1080;
                window.Destroying += Window_Destroying;
            }
            return window;
        }

        private void Window_Destroying(object sender, EventArgs e)
        {
            // the call is mandatory to stop internal monitoring threads, otherwise the app will not close
            VAST.Media.MediaGlobal.Uninitialize();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            VAST.Common.Log.ErrorFormat("Unhandled application exception: {0}", e);
            if (args.IsTerminating)
            {
                VAST.Common.Log.ErrorFormat("Runtime terminating...");
                // flush log to file
                VAST.Common.Log.Close();
            }
            else
            {
                VAST.Common.Log.DebugFormat("Continue execution...");
            }
        }

    }

}
