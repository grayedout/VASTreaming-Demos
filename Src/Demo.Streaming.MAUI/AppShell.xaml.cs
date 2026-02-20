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
    public partial class AppShell : Shell
    {

        public AppShell()
        {

            InitializeComponent();

#if VAST_FEATURE_MIXING
            // Insert Extended Capture after Simple Capture (index 1)
            Items.Insert(1, new FlyoutItem
            {
                Title = "Extended Capture",
                Items =
                {
                    new ShellSection
                    {
                        Items =
                        {
                            new ShellContent
                            {
                                ContentTemplate = new DataTemplate(typeof(ExtendedCapturePage))
                            }
                        }
                    }
                }
            });
#endif

#if VAST_FEATURE_WEBRTC
            Items.Add(new FlyoutItem
            {
                Title = "WebRTC Playback",
                Items =
                {
                    new ShellSection
                    {
                        Items =
                        {
                            new ShellContent
                            {
                                ContentTemplate = new DataTemplate(typeof(WebRtcPlaybackPage))
                            }
                        }
                    }
                }
            });

            Items.Add(new FlyoutItem
            {
                Title = "WebRTC 2-Way",
                Items =
                {
                    new ShellSection
                    {
                        Items =
                        {
                            new ShellContent
                            {
                                ContentTemplate = new DataTemplate(typeof(WebRtcTwoWayPage))
                            }
                        }
                    }
                }
            });
#endif

#if VAST_FEATURE_RTSP
            Items.Add(new FlyoutItem
            {
                Title = "RTSP Server",
                Items =
                {
                    new ShellSection
                    {
                        Items =
                        {
                            new ShellContent
                            {
                                ContentTemplate = new DataTemplate(typeof(ServerPage1))
                            }
                        }
                    }
                }
            });
#endif

#if VAST_FEATURE_CORESERVER
            Items.Add(new FlyoutItem
            {
                Title = "Multi-proto Server",
                Items =
                {
                    new ShellSection
                    {
                        Items =
                        {
                            new ShellContent
                            {
                                ContentTemplate = new DataTemplate(typeof(ServerPage2))
                            }
                        }
                    }
                }
            });
#endif

        }

    }

}
