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

    using System;
    using System.Diagnostics;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Security.Principal;

    class Program
    {

        [DllImport("libc")]
        public static extern uint getuid();

        static void Main(string[] args)
        {

            // initialize log
            string folderName = System.IO.Path.GetDirectoryName(new Uri(AppContext.BaseDirectory).LocalPath);
            VAST.Common.Log.LogFileName = System.IO.Path.Combine(folderName, @"VAST.Demo.Server.log");
            VAST.Common.Log.LogLevel = VAST.Common.Log.Level.Debug;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {

                // administrator/root privilege is necessary to start HTTP server on Windows
                bool isAdmin = false;
                try
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        var principal = new WindowsPrincipal(identity);
                        isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    isAdmin = false;
                }

                if (!isAdmin)
                {

                    // need to restart the app as administrator/root
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {

                        VAST.Common.Log.DebugFormat("Restarting the app with administrator privileges");

                        ProcessStartInfo proc = new ProcessStartInfo();
                        proc.UseShellExecute = true;
                        proc.WorkingDirectory = Environment.CurrentDirectory;
                        proc.FileName = Assembly.GetEntryAssembly().Location;
                        proc.Verb = "runas";

                        try
                        {
                            Process.Start(proc);
                        }
                        catch
                        {
                            Console.WriteLine("This application requires elevated privileges in order to operate correctly!");
                            throw new Exception("This application requires elevated privileges in order to operate correctly!");
                        }

                    }
                    else
                    {
                        Console.WriteLine("This application requires root privileges in order to operate correctly!");
                        throw new Exception("This application requires root privileges in order to operate correctly!");
                    }

                    return;

                }

            }

            // enter the license
            VAST.Common.License.Key = "YOUR_LICENSE_KEY";

            // TODO: uncomment to enable Nvidia accelerated encoding/decoding on WSL
            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            //{
            //    VAST.Codecs.CodecGlobal.Cuda64BitPath = "/usr/lib/wsl/lib";
            //}

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(AppExceptionHandler);

            // TODO: choose which server you want to run
#if VAST_FEATURE_CORESERVER
            MultiProtoServer sample = new MultiProtoServer();
#else
            IDisposable sample = null;
#endif
            //SimpleRtmpServer sample = new SimpleRtmpServer();
            //SimpleRtspServer sample = new SimpleRtspServer();
            //SendSample1 sample = new SendSample1();
            //SendSample2 sample = new SendSample2();
            //ReceiveSample sample = new ReceiveSample("rtp://127.0.0.1:10000");
            //SimpleWebRtcServer sample = new SimpleWebRtcServer();
            //FileReaderSample sample = new FileReaderSample(@"C:\path\to\file.mp4");
            //FileWriterSample1 sample = new FileWriterSample1("rtsp://127.0.0.1/test", @"C:\path\to\file.mp4");
            //FileWriterSample2 sample = new FileWriterSample2(@"C:\path\to\file.mp4");
            //IsoFragmentWriterSample sample = new IsoFragmentWriterSample("rtsp://127.0.0.1/test");

            bool running = true;
            while (running)
            {
                int keyInfo = Console.Read();
                switch (Convert.ToChar(keyInfo))
                {
                    case 's':
                    case 'S':
                        // shutdown
                        running = false;
                        break;
                }
            }

            if (sample != null) sample.Dispose();
            // the call is mandatory to stop internal monitoring threads, otherwise the app will not close
            VAST.Media.MediaGlobal.Uninitialize();

        }

        static void AppExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            VAST.Common.Log.ErrorFormat("Unhandled application exception: {0}", ((Exception)args.ExceptionObject).ToString());
            if (args.IsTerminating)
            {
                VAST.Common.Log.ErrorFormat("Runtime terminating...");
                VAST.Common.Log.Close();
            }
            else
            {
                VAST.Common.Log.DebugFormat("Continue execution...");
            }
        }

    }

}
