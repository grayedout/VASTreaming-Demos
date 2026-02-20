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
    public static class MauiProgram
    {

        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("ios14.2")]
        [SupportedOSPlatform("maccatalyst13.1")]
        [SupportedOSPlatform("android")]
        public static MauiApp CreateMauiApp()
        {

            VAST.Common.Log.LogFileName = System.IO.Path.Combine(VAST.Common.SpecialFolderHelper.GetPath(VAST.Common.SpecialFolder.UserSpecificApplicationData), "Demo.Streaming.MAUI.log");
            VAST.Common.Log.LogLevel = VAST.Common.Log.Level.Debug;
            VAST.Common.Log.MaxLogFileSize = 10 * 1024 * 1024;

            // license must be set before any other operation
            VAST.Common.License.Key = "YOUR_LICENSE_KEY";

#if VAST_FEATURE_FFMPEG
            VAST.Codecs.CodecGlobal.Initialize();
#endif

            VAST.Common.NtpTime.Sync();

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSansRegular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSansSemibold.ttf", "OpenSansSemibold");
                }).ConfigureMauiHandlers(handlers =>
                {
                    handlers.AddHandler(typeof(VAST.Controls.VideoPreviewControl), typeof(VAST.Controls.VideoPreviewHandler));
                    handlers.AddHandler(typeof(VAST.Controls.MediaPlayerControlBase), typeof(VAST.Controls.MediaPlayerHandler));
                });

            return builder.Build();

        }

        public static Task VastDisplayAlert(Page page, string title, string message, string cancel)
        {
#if NET10_0_OR_GREATER
            return page.DisplayAlertAsync(title, message, cancel);
#else
            return page.DisplayAlert(title, message, cancel);
#endif
        }

    }

}
