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

    using System.Net;
    using System.Net.NetworkInformation;
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;

    /// <summary>
    /// Demo page for comprehensive multi-protocol streaming server functionality.
    /// Demonstrates running a unified streaming server supporting RTMP, RTSP, SRT, HLS, MPEG-DASH,
    /// and WebRTC protocols with various configuration options including publishing points,
    /// proxy sources, and mixing sources.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class ServerPage2 : ContentPage
    {

        /// <summary>
        /// List of available video capture devices on the system.
        /// </summary>
        private List<VAST.Capture.VideoCaptureDeviceDescriptor> videoDevices = null;

        /// <summary>
        /// The currently selected video capture device descriptor.
        /// </summary>
        private VAST.Capture.VideoCaptureDeviceDescriptor videoDevice = null;

        /// <summary>
        /// The currently selected video capture mode (resolution, framerate, pixel format).
        /// </summary>
        private VAST.Capture.VideoCaptureMode videoCaptureMode = null;

        /// <summary>
        /// List of available audio capture devices on the system.
        /// </summary>
        private List<VAST.Capture.AudioCaptureDeviceDescriptor> audioDevices = null;

        /// <summary>
        /// The currently selected audio capture device descriptor.
        /// </summary>
        private VAST.Capture.AudioCaptureDeviceDescriptor audioDevice = null;

        /// <summary>
        /// The currently selected audio capture mode (sample rate, channels, format).
        /// </summary>
        private VAST.Capture.AudioCaptureMode audioCaptureMode = null;

        /// <summary>
        /// The video rotation angle in degrees to compensate for device orientation.
        /// </summary>
        private int videoRotation = 0;

        /// <summary>
        /// The local IP address for the streaming server.
        /// </summary>
        private string localIp = null;

        /// <summary>
        /// The multi-protocol streaming server instance.
        /// </summary>
        private VAST.Network.StreamingServer server = null;

        #pragma warning disable CS0414
        /// <summary>
        /// Background task for pushing media to the server (used in advanced scenarios).
        /// </summary>
        private Task pushingTask = null;
        #pragma warning restore CS0414

        /// <summary>
        /// Creates a new instance of <see cref="ServerPage2"/>.
        /// </summary>
        /// <remarks>
        /// Detects the local IP address for the streaming server by enumerating network interfaces.
        /// Initializes picker controls with default values and configures encoding framework options.
        /// </remarks>
        public ServerPage2()
        {

            InitializeComponent();

            // Initialize picker controls with default values
            this.pickerVideoProfile.SelectedIndex = 1;
            this.pickerVideoLevel.SelectedIndex = 0;
            this.pickerAudioSampleRate.SelectedIndex = 1;
            this.pickerAudioChannels.SelectedIndex = 1;

            // Configure platform-specific encoding framework options
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {
                this.pickerEncodingFramework.ItemsSource = new List<string> { "Media Foundation", "FFmpeg" };
                this.pickerEncodingFramework.SelectedIndex = 0;
                this.layoutEncodingFramework.IsVisible = true;
            }
            else
            {
                this.pickerEncodingFramework.ItemsSource = new List<string> { "Builtin" };
                this.pickerEncodingFramework.SelectedIndex = 0;
                this.layoutEncodingFramework.IsVisible = false;
            }

            // Auto-detect the local IP address for the streaming server
            // Iterates through network interfaces to find a non-loopback IPv4 address
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.Supports(NetworkInterfaceComponent.IPv4) && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        IPInterfaceProperties prop = nic.GetIPProperties();
                        IPv4InterfaceProperties ipv4Prop = prop.GetIPv4Properties();
                        foreach (var addr in prop.UnicastAddresses)
                        {
                            var b = addr.Address.GetAddressBytes();
                            if (b != null && b.Length == 4)
                            {
                                localIp = addr.Address.ToString();
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(localIp)) break;
                    }
                }
                catch { }
            }

        }

        /// <summary>
        /// Called when the page size changes due to device rotation or window resize.
        /// Updates the video rotation compensation and recreates the capture source if needed.
        /// </summary>
        /// <param name="width">The new page width.</param>
        /// <param name="height">The new page height.</param>
        protected override void OnSizeAllocated(double width, double height)
        {

            base.OnSizeAllocated(width, height);

            // Track rotation changes to compensate for device orientation
            int oldRotation = this.videoRotation;
            this.updateRotation();
            if (oldRotation == this.videoRotation) return;

            // Rotation changed - need to restart capture with new rotation
            Task.Run(() => { this.recreateVideoCaptureSource(); });

        }

        /// <summary>
        /// Handles the page navigated to event by initializing rotation and enumerating capture devices.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatedTo(object sender, NavigatedToEventArgs e)
        {
            this.updateRotation();
            Task.Run(async () =>
            {
                await this.enumerateDevices();
            });
        }

        /// <summary>
        /// Handles the page navigating from event by stopping the server.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {
            this.stopServer();
        }

        /// <summary>
        /// Handles the page unloaded event by forwarding to navigating from handler.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void ContentPage_Unloaded(object sender, EventArgs e)
        {
            this.ContentPage_NavigatingFrom(null, null);
        }

        /// <summary>
        /// Handles the start button click to initialize and start the multi-protocol server.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Requires a valid local IP address to be detected during initialization.
        /// Displays the server URI for clients to connect to upon successful start.
        /// </remarks>
        private void btnStart_Clicked(object sender, EventArgs e)
        {
            try
            {
                this.labelServerUri.IsVisible = true;
                if (string.IsNullOrEmpty(localIp))
                {
                    this.labelServerUri.Text = "Failed to start the server: can't detect local IP";
                }
                else
                {
                    if (this.server == null)
                    {
                        this.startServer();
                        this.labelServerUri.Text =
                            $@"Camera stream is available at:
rtmp://{localIp}:1935/live/camera
rtsp://{localIp}:10554/camera
srt://{localIp}:21330
http://{localIp}:8888/hls/camera // HLS
http://{localIp}:8888/dash/camera // MPEG-DASH";
                    }
                }
            }
            catch (Exception ex)
            {
                this.labelServerUri.Text = "Failed to start server: " + ex.Message;
            }
        }

        /// <summary>
        /// Handles the stop button click to stop the server.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStop_Clicked(object sender, EventArgs e)
        {
            this.stopServer();
        }

        /// <summary>
        /// Handles the send log button click to transmit diagnostic logs to support.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnSendLog_Clicked(object sender, EventArgs e)
        {
            if (await VAST.Common.License.SendLog("MAUI server #2 issue"))
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "The log has been sent successfully", "OK");
            }
            else
            {
                await MauiProgram.VastDisplayAlert(this, "Error", "Failed to send the log! Please grab it manually and send to support@vastreaming.net.", "OK");
            }
        }

        /// <summary>
        /// Handles video device selection changes.
        /// Updates the video capture mode picker with available modes for the selected device.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerVideoDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Offset by -1 because index 0 is the "No Video" entry
            int index = this.pickerVideoDevice.SelectedIndex - 1;
            this.videoDevice = (index >= 0 && this.videoDevices != null && this.videoDevices.Count > index) ? this.videoDevices[index] : null;

            // Populate capture mode picker with available modes for selected device
            if (this.pickerVideoDevice.SelectedIndex > 0 && this.videoDevice != null && this.videoDevice.CaptureModes != null && this.videoDevice.CaptureModes.Count > 0)
            {
                // Build capture mode display strings
                List<string> src = new List<string>();
                foreach (VAST.Capture.VideoCaptureMode captureMode in this.videoDevice.CaptureModes)
                {
                    src.Add(string.Format("{0}x{1}, {2:0.##} fps{3}", captureMode.Width, captureMode.Height, captureMode.Framerate, (captureMode.PixelFormat == VAST.Common.PixelFormat.None) ? "" : $", {captureMode.PixelFormat}"));
                }
                this.pickerVideoCaptureMode.ItemsSource = src;
                this.pickerVideoCaptureMode.ItemsSource = this.pickerVideoCaptureMode.GetItemsAsArray();

                // Auto-select: prefer device default if <= 1080p, otherwise find best 1080p mode
                if (this.videoDevice.DefaultCaptureMode >= 0 && this.videoDevice.CaptureModes[this.videoDevice.DefaultCaptureMode].Height <= 1080)
                {
                    this.pickerVideoCaptureMode.SelectedIndex = this.videoDevice.DefaultCaptureMode;
                }
                else
                {
                    var mode = this.videoDevice.CaptureModes.LastOrDefault(cm => cm.Height <= 1080 && cm.Width <= 1920);
                    this.pickerVideoCaptureMode.SelectedIndex = (mode != null) ? this.videoDevice.CaptureModes.IndexOf(mode) : -1;
                }
            }
            else
            {
                // No device selected or no modes available — clear the mode picker
                this.pickerVideoCaptureMode.ItemsSource = new List<string>();
                this.pickerVideoCaptureMode.ItemsSource = this.pickerVideoCaptureMode.GetItemsAsArray();
                this.pickerVideoCaptureMode.SelectedIndex = -1;
            }
        }

        /// <summary>
        /// Handles video capture mode selection changes.
        /// Updates the active capture mode configuration.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerVideoCaptureMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.pickerVideoCaptureMode.SelectedIndex >= 0)
            {
                this.videoCaptureMode = this.videoDevice.CaptureModes[this.pickerVideoCaptureMode.SelectedIndex];
            }
            else
            {
                this.videoCaptureMode = null;
            }
        }

        /// <summary>
        /// Handles audio device selection changes.
        /// Updates the audio capture mode picker with available modes for the selected device.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerAudioDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Offset by -1 because index 0 is the "No Audio" entry
            int index = this.pickerAudioDevice.SelectedIndex - 1;
            this.audioDevice = (index >= 0 && this.audioDevices != null && this.audioDevices.Count > index) ? this.audioDevices[index] : null;

            // Populate capture mode picker with available modes for selected device
            if (this.pickerAudioDevice.SelectedIndex > 0 && this.audioDevice != null && this.audioDevice.CaptureModes != null && this.audioDevice.CaptureModes.Count > 0)
            {
                // Build capture mode display strings
                List<string> src = new List<string>();
                foreach (VAST.Capture.AudioCaptureMode captureMode in this.audioDevice.CaptureModes)
                {
                    src.Add(string.Format("{0} Hz, {1} ch{2}", captureMode.SampleRate, captureMode.Channels, (captureMode.SampleFormat == Common.SampleFormat.Unknown) ? "" : $", {captureMode.SampleFormat}"));
                }
                this.pickerAudioCaptureMode.ItemsSource = src;
                this.pickerAudioCaptureMode.ItemsSource = this.pickerAudioCaptureMode.GetItemsAsArray();
                this.pickerAudioCaptureMode.SelectedIndex = this.audioDevice.DefaultCaptureMode;
            }
            else
            {
                // No device selected or no modes available — clear the mode picker
                this.pickerAudioCaptureMode.ItemsSource = new List<string>();
                this.pickerAudioCaptureMode.ItemsSource = this.pickerAudioCaptureMode.GetItemsAsArray();
                this.pickerAudioCaptureMode.SelectedIndex = -1;
            }
        }

        /// <summary>
        /// Handles audio capture mode selection changes.
        /// Updates the active capture mode configuration.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerAudioCaptureMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.pickerAudioCaptureMode.SelectedIndex >= 0)
            {
                this.audioCaptureMode = this.audioDevice.CaptureModes[this.pickerAudioCaptureMode.SelectedIndex];
            }
            else
            {
                this.audioCaptureMode = null;
            }
        }

        /// <summary>
        /// Asynchronously enumerates all available video and audio capture devices on the system.
        /// Populates the device picker controls with discovered devices and their capabilities.
        /// </summary>
        /// <returns>A task representing the asynchronous enumeration operation.</returns>
        /// <remarks>
        /// Discovers devices from multiple frameworks including Media Foundation, DirectShow, WASAPI, ASIO, and NDI.
        /// Device names are prefixed with their framework identifier for clarity (e.g., [MF], [DS], [NDI]).
        /// Automatically selects "Front Camera" on mobile devices if available.
        /// </remarks>
        private async Task enumerateDevices()
        {

            // Enumerate video capture devices from all available frameworks
            this.videoDevices = await VAST.Capture.VideoDeviceEnumerator.Enumerate(VAST.Common.MediaFramework.Unknown);
            if (this.videoDevices.Count > 0)
            {

                // Build device list with framework prefix for identification
                List<string> src = new List<string>();
                src.Add("No Video");
                foreach (VAST.Capture.VideoCaptureDeviceDescriptor device in this.videoDevices)
                {
                    string s = string.Empty;
                    switch (device.Framework)
                    {
                        case Common.MediaFramework.MediaFoundation:
                            s = "[MF] ";
                            break;
                        case Common.MediaFramework.DirectShow:
                            s = "[DS] ";
                            break;
                        case Common.MediaFramework.NDI:
                            s = "[NDI] ";
                            break;
                    }
                    src.Add(s + device.Name);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {

                    this.pickerVideoDevice.ItemsSource = src;
                    this.pickerVideoDevice.ItemsSource = this.pickerVideoDevice.GetItemsAsArray();
                    this.pickerVideoDevice.IsEnabled = true;
                    this.pickerVideoCaptureMode.IsEnabled = true;

                    // Auto-select front camera on mobile devices if available
                    int selectedIndex = -1;
                    for (int i = 0; i < this.videoDevices.Count; ++i)
                    {
                        if (this.videoDevices[i].Name == "Front Camera")
                        {
                            selectedIndex = i + 1; // +1 for "No Video" entry
                            break;
                        }
                    }

                    // Default to first device if front camera not found
                    if (selectedIndex < 0) selectedIndex = 1;
                    this.pickerVideoDevice.SelectedIndex = selectedIndex;

                });

            }
            else
            {
                // No video devices available
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.pickerVideoDevice.ItemsSource = new List<string> { "No Video" };
                    this.pickerVideoDevice.ItemsSource = this.pickerVideoDevice.GetItemsAsArray();
                    this.pickerVideoDevice.IsEnabled = false;
                    this.pickerVideoCaptureMode.IsEnabled = false;
                });
            }

            // Enumerate audio capture devices from all available frameworks
            this.audioDevices = await VAST.Capture.AudioDeviceEnumerator.Enumerate(VAST.Common.MediaFramework.Unknown);
            if (this.audioDevices.Count > 0)
            {

                // Build device list with framework prefix for identification
                List<string> src = new List<string>();
                src.Add("No Audio");
                foreach (VAST.Capture.AudioCaptureDeviceDescriptor device in this.audioDevices)
                {
                    string s = string.Empty;
                    switch (device.Framework)
                    {
                        case Common.MediaFramework.MediaFoundation:
                            s = "[MF] ";
                            break;
                        case Common.MediaFramework.DirectShow:
                            s = "[DS] ";
                            break;
                        case Common.MediaFramework.WASAPI:
                            s = "[WASAPI] ";
                            break;
                        case Common.MediaFramework.ASIO:
                            s = "[ASIO] ";
                            break;
                        case Common.MediaFramework.NDI:
                            s = "[NDI] ";
                            break;
                    }
                    src.Add(s + device.Name);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.pickerAudioDevice.ItemsSource = src;
                    this.pickerAudioDevice.ItemsSource = this.pickerAudioDevice.GetItemsAsArray();
                    this.pickerAudioDevice.IsEnabled = true;
                    this.pickerAudioCaptureMode.IsEnabled = true;
                    this.pickerAudioDevice.SelectedIndex = 1; // select first audio device
                });

            }
            else
            {
                // No audio devices available
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.pickerAudioDevice.ItemsSource = new List<string> { "No Audio" };
                    this.pickerAudioDevice.ItemsSource = this.pickerAudioDevice.GetItemsAsArray();
                    this.pickerAudioDevice.IsEnabled = false;
                    this.pickerAudioCaptureMode.IsEnabled = false;
                });
            }

        }

        /// <summary>
        /// Starts the multi-protocol streaming server with configured protocols and settings.
        /// </summary>
        /// <remarks>
        /// Configures and enables the following protocols based on compile-time feature flags:
        /// <list type="bullet">
        /// <item><description>RTMP - port 1935 (live application)</description></item>
        /// <item><description>RTSP - port 10554</description></item>
        /// <item><description>SRT - configurable via SRT server parameters</description></item>
        /// <item><description>HTTP - port 8888 (for HLS, MPEG-DASH, WebRTC, MJPEG)</description></item>
        /// <item><description>MPEG-DASH - /dash path</description></item>
        /// <item><description>HLS - /hls path</description></item>
        /// <item><description>JSON API - enabled for admin access</description></item>
        /// </list>
        /// Also subscribes to server events for authorization, connections, and errors.
        /// </remarks>
        private void startServer()
        {

            server = new VAST.Network.StreamingServer(20);

#if VAST_FEATURE_RTMP
            // RTMP
            server.EnableRtmp = true;
            server.RtmpServerParameters.RtmpEndPoints.Add(new IPEndPoint(IPAddress.Any, 1935));
            // uncomment RTMPS end point assignment if you want to run secure protocol
            //server.RtmpServerParameters.RtmpsEndPoints.Add(new IPEndPoint(IPAddress.Any, 1936)),
            server.RtmpServerParameters.RtmpApplication = "live";
#endif

#if VAST_FEATURE_RTSP
            // RTSP
            server.EnableRtsp = true;
            server.RtspServerParameters.AllowedRtpTransports = VAST.RTP.RtpTransportType.Any;
            server.RtspServerParameters.RtspEndPoints.Add(new IPEndPoint(IPAddress.Any, 10554));
            // uncomment RTSPS end point assignment if you want to run secure protocol
            //server.RtspServerParameters.RtspsEndPoints.Add(new IPEndPoint(IPAddress.Any, 555));
#endif

#if VAST_FEATURE_SRT
            // SRT
            server.EnableSrt = true;
            // start simple instance for camera stream only
            VAST.SRT.SrtServerInstanceParameters srtInstancePars = new VAST.SRT.SrtServerInstanceParameters
            {
                IsSimpleInstance = true,
                PublishingPath = "camera",
                FlowDirection = VAST.Common.MediaFlowDirection.Output
            };
            srtInstancePars.EndPoints.Clear();
            srtInstancePars.EndPoints.Add(new IPEndPoint(IPAddress.Any, VAST.SRT.SrtGlobal.DefaultSrtPort));
            server.SrtServerParameters.Instances.Add(srtInstancePars);
#endif

#if VAST_FEATURE_API || VAST_FEATURE_HLS || VAST_FEATURE_TS || VAST_FEATURE_MPEG_DASH || VAST_FEATURE_WEBRTC || VAST_FEATURE_JPEG
            // common HTTP server for WebRTC, MPEG-DASH and HLS
            server.EnableHttp = true;
            server.HttpServerParameters = new VAST.HTTP.HttpServerParameters
            {
                HttpPorts = { 8888 },
                // uncomment HTTPS port assignment if you want to run secure protocol
                //HttpsPorts = { 8889 }
            };
#endif

#if VAST_FEATURE_MPEG_DASH
            // MPEG-DASH
            server.EnableMpegDash = true;
            server.DashServerParameters = new VAST.DASH.DashServerParameters
            {
                MpegDashPath = "/dash"
            };
#endif

#if VAST_FEATURE_HLS
            // HLS
            server.EnableHls = true;
            server.HlsServerParameters = new VAST.HLS.HlsServerParameters
            {
                HlsPath = "/hls",
                EnableLowLatency = false
            };
#endif

#if VAST_FEATURE_TS
            // TS over HTTP
            // change to true if you want to run this server
            server.EnableTsHttp = false;
            server.TsHttpServerParameters = new VAST.TS.TsHttpServerParameters
            {
                TsHttpPath = "/ts",
            };
#endif

#if VAST_FEATURE_JPEG
            // MJPEG over HTTP
            // change to true if you want to run this server
            server.EnableMjpeg = false;
            server.MjpegServerParameters = new VAST.Image.JPEG.MjpegServerParameters
            {
                MjpegPath = "/mjpeg",
            };
#endif

#if VAST_FEATURE_AUDIO
            // custom PCM server
            // change to true if you want to run this server
            //server.EnableWsPcm = false;
            //server.WsPcmServerParameters = new VAST.Audio.WsPcmServerParameters
            //{
            //    WsPcmPath = "/pcm",
            //};
#endif

#if VAST_FEATURE_API
            // API server
            server.EnableJsonApi = true;
            server.ApiServerParameters.Users.Add("admin", "admin");
#endif

            // proper certificate thumbprint must be assigned if you want to run secure protocols
            server.CertificateThumbprint = "YOUR_CERTIFICATE_THUMBPRINT";

            server.PublishingPointRequested += Server_PublishingPointRequested;
            server.Authorize += Server_Authorize;
            server.PublisherConnected += Server_PublisherConnected;
            server.ClientConnected += Server_ClientConnected;
            server.Error += Server_Error;
            server.Disconnected += Server_Disconnected;
            server.Start();

            // below listed optional use-cases which could be un-commented if necessary:

            ///////////////////////////////////////////////////////////////////
            // 1. Create publishing point with a pull source
            ///////////////////////////////////////////////////////////////////

            //this.pullSourceId = this.server.CreatePublishingPoint("YOUR_PUBLISHING_POINT_NAME", "YOUR_SOURCE_URI");

            ///////////////////////////////////////////////////////////////////
            // 2. Create VOD publishing point for a single file
            //    NOTE: accessible via HLS http://127.0.0.1:8888/hls/VOD_PUBLISHING_POINT_NAME
            //          or MPEG-DASH http://127.0.0.1:8888/dash/VOD_PUBLISHING_POINT_NAME
            ///////////////////////////////////////////////////////////////////

            //Uri uri = new Uri(@"MP4_FILE_PATH");
            //this.server.CreatePublishingPoint("VOD_PUBLISHING_POINT_NAME", uri.AbsoluteUri, VAST.Common.StreamingMode.Vod);

            ///////////////////////////////////////////////////////////////////
            // 3. Create VOD publishing point for a single file from user stream
            //    NOTE: accessible via HLS http://127.0.0.1:8888/hls/VOD_PUBLISHING_POINT_NAME
            ///////////////////////////////////////////////////////////////////

            //VAST.File.ISO.IsoSource source = new VAST.File.ISO.IsoSource();
            //System.IO.Stream file = System.IO.File.Open(@"MP4_FILE_PATH", System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            //byte[] buffer = new byte[file.Length];
            //file.Read(buffer);
            //source.Stream = new System.IO.MemoryStream(buffer);
            //// if using user stream then publishing point can support only one output protocol
            //this.server.CreatePublishingPoint("VOD_PUBLISHING_POINT_NAME", source, VAST.Common.StreamingMode.Vod,
            //    new VAST.Network.PublishingPointParameters { IsTemporary = true, AllowedProtocols = VAST.Common.StreamingProtocol.HLS });

            ///////////////////////////////////////////////////////////////////
            // 4. Create VOD publishing point for a directory
            //    NOTE: accessible via HLS http://127.0.0.1:8888/hls/VOD_DIRECTORY_PUBLISHING_POINT_NAME/filename.mp4
            //          or MPEG-DASH http://127.0.0.1:8888/dash/VOD_DIRECTORY_PUBLISHING_POINT_NAME/filename.mp4
            //          where filename.mp4 is a file path relative to PATH_TO_FOLDER_WITH_FILES
            ///////////////////////////////////////////////////////////////////

            //Uri uri = new Uri(@"PATH_TO_FOLDER_WITH_FILES");
            //// parameter ?recursive=1 and can be used to access files in subdirectories
            //string wildcardUri = uri.AbsoluteUri + "/*.mp4?recursive=1";
            //this.server.CreatePublishingPoint("VOD_DIRECTORY_PUBLISHING_POINT_NAME", wildcardUri, VAST.Common.StreamingMode.Vod);

            ///////////////////////////////////////////////////////////////////
            // 5. Create publishing point with a file source (endless loop of the same file)
            ///////////////////////////////////////////////////////////////////

            //VAST.File.FileSource source = new VAST.File.FileSource();
            //source.Uri = @"VIDEO_FILE_PATH";
            //source.Loop = true;
            //this.pullSourceId = this.server.CreatePublishingPoint("YOUR_PUBLISHING_POINT_NAME", source);

            ///////////////////////////////////////////////////////////////////
            // 6. Create publishing point with an image source
            ///////////////////////////////////////////////////////////////////

            //VAST.File.ImageSource source = new VAST.File.ImageSource();
            //// source image can be set via file path, stream or Bitmap
            //source.SetImage(@"IMAGE_FILE_PATH");
            //source.Open();
            //// set video encoding parameters
            //VAST.Common.MediaType mt = new VAST.Common.MediaType
            //{
            //    ContentType = VAST.Common.ContentType.Video,
            //    CodecId = VAST.Common.Codec.H264,
            //    Bitrate = 1000000,
            //    Width = 1280,
            //    Height = 720,
            //    Framerate = new VAST.Common.Rational(30),
            //};
            //source.SetDesiredOutputType(0, mt);
            //// create publishing point
            //this.pullSourceId = this.server.CreatePublishingPoint("YOUR_PUBLISHING_POINT_NAME", source);

            ///////////////////////////////////////////////////////////////////
            // 7. Create publishing point with a capture source
            ///////////////////////////////////////////////////////////////////

            Task.Run(() => createCaptureSource());

            ///////////////////////////////////////////////////////////////////
            // 8. Create publishing point which uses pre-encoded media data of user
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine1());

            ///////////////////////////////////////////////////////////////////
            // 9. Create publishing point which uses ImageSource for encoding video stream
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine2());

            ///////////////////////////////////////////////////////////////////
            // 10. Encode uncompressed user video on the fly and push to the server
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine3());

            ///////////////////////////////////////////////////////////////////
            // 11. Create mixing publishing point waiting for publisher
            ///////////////////////////////////////////////////////////////////

            //this.createMixingPublishingPoint1();

            ///////////////////////////////////////////////////////////////////
            // 12. Create proxy source to forcibly transcode pull stream
            ///////////////////////////////////////////////////////////////////

            //this.createProxySource("transcode", "rtsp://192.168.0.101/stream");

        }

        /// <summary>
        /// Stops the streaming server and releases associated resources.
        /// </summary>
        private async void stopServer()
        {
            try
            {
                if (this.server != null)
                {
                    this.server.Stop();
                    this.server = null;
                }
                this.labelServerUri.IsVisible = false;
            }
            catch (Exception ex)
            {
                await MauiProgram.VastDisplayAlert(this, "Capture Demo", "Streaming stop exception:\n" + ex.ToString(), "OK");
            }
        }

        /// <summary>
        /// Handles publishing point requested event for on-demand publishing point creation.
        /// </summary>
        /// <param name="connectionId">The unique connection identifier.</param>
        /// <param name="connectionInfo">Information about the requesting connection.</param>
        /// <param name="createdPublishingPointParameters">Parameters for the publishing point to create.</param>
        /// <remarks>
        /// Called when a client requests a publishing point that doesn't exist.
        /// User code can create on-demand publishing points here based on the requested path.
        /// </remarks>
        private void Server_PublishingPointRequested(Guid connectionId, VAST.Network.ConnectionInfo connectionInfo, VAST.Network.CreatedPublishingPointParameters createdPublishingPointParameters)
        {
            // Newly connected client has requested publishing point which doesn't exist yet
            // User code in this event handler can create on-demand publishing point here
        }

        /// <summary>
        /// Handles connection authorization events.
        /// </summary>
        /// <param name="connectionId">The unique connection identifier.</param>
        /// <param name="connectionInfo">Information about the connection to authorize.</param>
        /// <remarks>
        /// Connection type and parameters have been detected at this point.
        /// Set <see cref="VAST.Network.ConnectionInfo.IsValid"/> to true to accept or false to reject.
        /// </remarks>
        private void Server_Authorize(Guid connectionId, VAST.Network.ConnectionInfo connectionInfo)
        {
            // Accept all connections by default
            connectionInfo.IsValid = true;
        }

        /// <summary>
        /// Handles publisher connected events when a publisher or pull source is ready.
        /// </summary>
        /// <param name="connectionId">The unique connection identifier.</param>
        /// <param name="publishingPoint">The publishing point that was created for this publisher.</param>
        /// <remarks>
        /// Called when a publisher has been connected and authorized, or when a pull source is ready.
        /// The publishing point can be used to start forwarding to another server or recording to a file.
        /// </remarks>
        private void Server_PublisherConnected(Guid connectionId, VAST.Network.PublishingPoint publishingPoint)
        {
            // Publisher (or pull source created by user) has been connected and authorized
            // New publishing point has been created and is ready for forwarding, recording etc
            // Sample: publishingPoint.StartForwarding("YOUR_FORWARDING_URI");
            // Sample: publishingPoint.StartRecording("YOUR_RECORDING_FILE_PATH");
        }

        /// <summary>
        /// Handles client connected events when a new viewer connects.
        /// </summary>
        /// <param name="connectionId">The unique connection identifier.</param>
        /// <param name="client">Information about the connected client.</param>
        private void Server_ClientConnected(Guid connectionId, VAST.Network.ConnectedClient client)
        {
            // New client has been connected
            // VAST.Network.ConnectedClient contains additional information about a client
        }

        /// <summary>
        /// Handles server error events for unrecoverable session errors.
        /// </summary>
        /// <param name="connectionId">The unique connection identifier.</param>
        /// <param name="errorDescription">Description of the error that occurred.</param>
        private void Server_Error(Guid connectionId, string errorDescription)
        {
            // Session has encountered unrecoverable error and will be closed shortly
        }

        /// <summary>
        /// Handles disconnection events when a connection is closed.
        /// </summary>
        /// <param name="connectionId">The unique connection identifier.</param>
        /// <param name="socketError">Additional information about the disconnection reason.</param>
        private void Server_Disconnected(Guid connectionId, VAST.Network.ExtendedSocketError socketError)
        {
            // Connection has been closed
            // If Error event was raised for this connectionId, server forcibly closed the connection
        }

        /// <summary>
        /// Creates a capture source publishing point using UI-selected video and audio devices.
        /// </summary>
        /// <remarks>
        /// Enumerates the UI-selected video and audio devices, creates capture sources,
        /// configures H.264 video encoding and AAC audio encoding using values from the UI controls,
        /// and creates a publishing point named "capture".
        /// </remarks>
        private async void createCaptureSource()
        {

            VAST.Network.AggregatedNetworkSource source = null;

            try
            {

                // Determine encoding framework based on picker selection
                VAST.Common.MediaFramework mediaFramework = Common.MediaFramework.Builtin;
                switch ((string)this.pickerEncodingFramework.SelectedItem)
                {
                    case "Media Foundation":
                        mediaFramework = VAST.Common.MediaFramework.MediaFoundation;
                        break;
                    case "FFmpeg":
                        mediaFramework = VAST.Common.MediaFramework.FFmpeg;
                        break;
                    case "Builtin":
                    default:
                        mediaFramework = VAST.Common.MediaFramework.Builtin;
                        break;
                }

                bool allowHardwareAcceleration = this.checkBoxHardwareAcceleration.IsChecked;

                // Configure global encoder/decoder defaults
                VAST.Media.DecoderParameters.DefaultPreferredMediaFramework = mediaFramework;
                VAST.Media.DecoderParameters.DefaultAllowHardwareAcceleration = allowHardwareAcceleration;
                VAST.Media.EncoderParameters.DefaultPreferredMediaFramework = mediaFramework;
                VAST.Media.EncoderParameters.DefaultAllowHardwareAcceleration = allowHardwareAcceleration;

                // Create aggregated source to combine video and audio streams
                source = new VAST.Network.AggregatedNetworkSource();

                // Create and configure video capture source if device is selected
                if (this.videoDevice != null)
                {

                    VAST.Capture.IVideoCaptureSource2 videoSource = VAST.Media.SourceFactory.CreateVideoCapture(this.videoDevice.DeviceId, this.videoCaptureMode);
                    videoSource.Rotation = this.videoRotation;

                    // Validate video output parameters
                    int width = int.Parse(this.tboxVideoWidth.Text);
                    if (width < 100 || width > 3840) throw new Exception("Please enter proper video width");

                    int height = int.Parse(this.tboxVideoHeight.Text);
                    if (height < 100 || height > 2160) throw new Exception("Please enter proper video height");

                    // Swap width and height if necessary based on current orientation
                    if ((this.videoRotation % 180) == 90)
                    {
                        if (width > height)
                        {
                            int temp = width;
                            width = height;
                            height = temp;
                        }
                    }
                    else
                    {
                        if (width < height)
                        {
                            int temp = width;
                            width = height;
                            height = temp;
                        }
                    }

                    int framerate = int.Parse(this.tboxVideoFramerate.Text);

                    int videoBitrate = int.Parse(this.tboxVideoBitrate.Text);
                    if (videoBitrate < 100000 || videoBitrate > 10000000) throw new Exception("Please enter proper video bitrate");

                    int videoKeyframeInterval = int.Parse(this.tboxVideoKeyframeInterval.Text);
                    if (videoKeyframeInterval < 1 || videoKeyframeInterval > 1000) throw new Exception("Please enter proper video keyframe interval");

                    // Build H.264 video media type with encoding parameters
                    VAST.Common.MediaType mt = new VAST.Common.MediaType
                    {
                        ContentType = VAST.Common.ContentType.Video,
                        CodecId = VAST.Common.Codec.H264,
                        Bitrate = videoBitrate,
                        Width = width,
                        Height = height,
                        Framerate = new VAST.Common.Rational(framerate),
                    };
                    mt.Metadata.Add("KeyframeInterval", videoKeyframeInterval.ToString());

                    // Set H.264 profile if not auto
                    if (this.pickerVideoProfile.SelectedIndex > 0)
                    {
                        switch (this.pickerVideoProfile.SelectedIndex)
                        {
                            case 1: // baseline profile (66)
                                mt.Metadata.Add("Profile", "66");
                                break;
                            case 2: // main profile (77)
                                mt.Metadata.Add("Profile", "77");
                                break;
                            case 3: // high profile (100)
                                mt.Metadata.Add("Profile", "100");
                                break;
                        }
                    }

                    // Set H.264 level if specified
                    if (this.pickerVideoLevel.SelectedIndex > 0)
                    {
                        mt.Metadata.Add("Level", ((int)(float.Parse((string)this.pickerVideoLevel.SelectedItem) * 10)).ToString());
                    }

                    // Apply encoding configuration to video capture source
                    await videoSource.SetDesiredOutputType(0, mt);

                    source.AddSource(videoSource);

                }

                // Create and configure audio capture source if device is selected
                if (this.audioDevice != null)
                {

                    VAST.Capture.IAudioCaptureSource2 audioSource = VAST.Media.SourceFactory.CreateAudioCapture(this.audioDevice.DeviceId, this.audioCaptureMode);

                    // Validate audio bitrate
                    int audioBitrate = int.Parse(this.tboxAudioBitrate.Text);
                    if (audioBitrate < 8000 || audioBitrate > 256000) throw new Exception("Please enter proper audio bitrate");

                    // Build AAC audio media type with encoding parameters
                    VAST.Common.MediaType mt = new VAST.Common.MediaType
                    {
                        ContentType = VAST.Common.ContentType.Audio,
                        CodecId = VAST.Common.Codec.AAC,
                        Bitrate = audioBitrate,
                    };

                    // On Android, use capture mode parameters directly (no resampler available)
                    if (DeviceInfo.Current.Platform == DevicePlatform.Android && this.audioCaptureMode != null)
                    {
                        mt.SampleRate = this.audioCaptureMode.SampleRate;
                        mt.Channels = this.audioCaptureMode.Channels;
                    }
                    else
                    {
                        mt.SampleRate = int.Parse((string)this.pickerAudioSampleRate.SelectedItem);
                        mt.Channels = int.Parse((string)this.pickerAudioChannels.SelectedItem);
                    }

                    // Apply encoding configuration to audio capture source
                    await audioSource.SetDesiredOutputType(0, mt);

                    source.AddSource(audioSource);

                }

                // Create "camera" publishing point and hand off ownership of the source
                this.server.CreatePublishingPoint("camera", source);
                source = null; // ownership transferred, prevent disposal in finally block

            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await MauiProgram.VastDisplayAlert(this, "Error", "Capture source error: " + ex.Message, "OK");
                });
            }
            finally
            {
                if (source != null) source.Dispose();
            }

        }

        /// <summary>
        /// Creates a mixing publishing point that waits for a publisher to connect.
        /// </summary>
        /// <remarks>
        /// Creates a mixing source that transcodes the incoming stream to multiple video qualities:
        /// <list type="bullet">
        /// <item><description>1280x720 @ 3Mbps (H.264 constrained baseline)</description></item>
        /// <item><description>848x480 @ 1.5Mbps (H.264 constrained baseline)</description></item>
        /// <item><description>416x240 @ 400Kbps (H.264 constrained baseline)</description></item>
        /// </list>
        /// Also transcodes audio to AAC 44.1kHz stereo @ 128Kbps.
        /// Accessible at publishing point "mixing" with ingest source "vast://publishing-point/ingest".
        /// </remarks>
        private void createMixingPublishingPoint1()
        {

#if VAST_FEATURE_MIXING

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                VAST.Media.DecoderParameters.DefaultPreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                VAST.Media.DecoderParameters.DefaultAllowHardwareAcceleration = true;
                VAST.Media.EncoderParameters.DefaultPreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                VAST.Media.EncoderParameters.DefaultAllowHardwareAcceleration = true;
            }

            VAST.Network.MixingSource source = new VAST.Network.MixingSource(this.server);

            source.Update(new VAST.Network.ApiPublishingPointRequest
            {
                Path = $"mixing",
                StreamingMode = VAST.Common.StreamingMode.Live,
                AllowVideoProcessing = false,
                NoSamplesTimeoutMs = 500,
                Sources = new List<VAST.Network.ApiSource>
                (
                    new VAST.Network.ApiSource[]
                    {
                    new VAST.Network.ApiSource { Uri = "vast://publishing-point/ingest" },
                    }
                ),
                Processing = new VAST.Network.ApiProcessing
                {
                    VideoProcessing = new VAST.Network.ApiVideoProcessing
                    {
                        Discard = false,
                        Transcoding = true,
                        Mixing = new VAST.Network.ApiVideoMixing
                        {
                            Type = VAST.Image.Mixing.VideoMixingType.Single,
                            SourceIndex = 0,
                        },
                        Tracks = new List<VAST.Network.ApiVideoTrack>
                        (
                            new VAST.Network.ApiVideoTrack[]
                            {
                            new VAST.Network.ApiVideoTrack
                            {
                                Index = 0,
                                Width = 1280,
                                Height = 720,
                                Framerate = new VAST.Common.Rational(30),
                                KeyframeInterval = 30,
                                Bitrate = 3000000,
                                Codec = VAST.Common.Codec.H264,
                                Profile = 0x100 | 66, // enforce constrained baseline profile
                            },
                            new VAST.Network.ApiVideoTrack
                            {
                                Index = 1,
                                Width = 848,
                                Height = 480,
                                Framerate = new VAST.Common.Rational(30),
                                KeyframeInterval = 30,
                                Bitrate = 1500000,
                                Codec = VAST.Common.Codec.H264,
                                Profile = 0x100 | 66, // enforce constrained baseline profile
                            },
                            new VAST.Network.ApiVideoTrack
                            {
                                Index = 2,
                                Width = 416,
                                Height = 240,
                                Framerate = new VAST.Common.Rational(30),
                                KeyframeInterval = 30,
                                Bitrate = 400000,
                                Codec = VAST.Common.Codec.H264,
                                Profile = 0x100 | 66, // enforce constrained baseline profile
                            },
                            }
                        )
                    },
                    AudioProcessing = new VAST.Network.ApiAudioProcessing
                    {
                        Discard = false,
                        Transcoding = true,
                        Mixing = new VAST.Network.ApiAudioMixing
                        {
                            Type = VAST.Image.Mixing.AudioMixingType.Single,
                            SourceIndex = 0,
                        },
                        Tracks = new List<VAST.Network.ApiAudioTrack>
                        (
                            new VAST.Network.ApiAudioTrack[]
                            {
                            new VAST.Network.ApiAudioTrack
                            {
                                Index = 3,
                                SampleRate = 44100,
                                Channels = 2,
                                Bitrate = 128000,
                                Codec = VAST.Common.Codec.AAC,
                            }
                            }
                        )
                    }
                }
            });

            this.server.CreatePublishingPoint($"mixing", source, new VAST.Network.PublishingPointParameters { InactivityTimeout = new TimeSpan(1, 0, 0) });

#endif

        }

        /// <summary>
        /// Creates a proxy source to forcibly transcode a pull stream.
        /// </summary>
        /// <param name="publishingPath">The path for the publishing point.</param>
        /// <param name="uri">The URI of the source stream to transcode.</param>
        /// <remarks>
        /// Opens the original source, creates a proxy source on top of it,
        /// and configures transcoding to H.264 video (constrained baseline) and AAC audio.
        /// Automatically creates a publishing point when the source is opened.
        /// </remarks>
        private void createProxySource(string publishingPath, string uri)
        {

            VAST.Media.IMediaSource originalSource = VAST.Media.SourceFactory.Create(uri);
            originalSource.StateChanged += (object sender, Media.MediaState e) =>
            {

                switch (e)
                {

                    case VAST.Media.MediaState.Opened:
                        {

                            var proxySource = new VAST.Media.ProxySource(originalSource);
                            proxySource.Open(); // ProxySource.Open() runs synchronously if original source is already opened

                            // initialize transcoders
                            for (int i = 0; i < proxySource.StreamCount; ++i)
                            {

                                VAST.Common.MediaType inputMediaType = proxySource.GetMediaType(i);
                                switch (inputMediaType.ContentType)
                                {

                                    case VAST.Common.ContentType.Audio:
                                        proxySource.SetDesiredOutputType(i, new VAST.Common.MediaType
                                        {
                                            ContentType = VAST.Common.ContentType.Audio,
                                            CodecId = VAST.Common.Codec.AAC,
                                            Bitrate = 128000,
                                            SampleRate = 44100,
                                            Channels = 2,
                                        }).Wait();
                                        break;

                                    case VAST.Common.ContentType.Video:
                                        VAST.Common.MediaType outputMediaType = new VAST.Common.MediaType
                                        {
                                            ContentType = VAST.Common.ContentType.Video,
                                            CodecId = VAST.Common.Codec.H264,
                                            Bitrate = inputMediaType.Width * inputMediaType.Height * 3,
                                            Width = inputMediaType.Width,
                                            Height = inputMediaType.Height,
                                            Framerate = inputMediaType.Framerate,
                                        };
                                        outputMediaType.Metadata.Add("KeyframeInterval", ((int)Math.Ceiling(inputMediaType.Framerate.ToDouble())).ToString());
                                        outputMediaType.Metadata.Add("Profile", (0x100 | 66).ToString()); // enforce constrained baseline profile
                                        proxySource.SetDesiredOutputType(i, outputMediaType).Wait();
                                        break;

                                }

                            }

                            this.server.CreatePublishingPoint(publishingPath, proxySource);
                            break;

                        }

                    case VAST.Media.MediaState.Error:
                        {
                            // TODO: process error
                            break;
                        }

                }

            };

            // initiate async open
            originalSource.Open();

        }

        /// <summary>
        /// Updates the video rotation angle based on current device orientation.
        /// Calculates the compensation angle needed to keep video upright.
        /// </summary>
        /// <remarks>
        /// MAUI rotation semantics differ by platform:
        /// - iOS/Android: Rotation0 = portrait
        /// - Windows/MacCatalyst: Rotation0 = landscape right
        ///
        /// The calculated rotation is applied to the camera capture source
        /// to compensate for device orientation and produce correctly oriented video.
        /// </remarks>
        private void updateRotation()
        {

            // Get current interface rotation from display info
            var r = DeviceDisplay.Current.MainDisplayInfo.Rotation;

            // Platform-specific rotation mapping due to MAUI inconsistencies
#if __MACCATALYST__ || WINDOWS
            // MacCatalyst/Windows: Rotation0 means landscape right, different from other platforms
            switch (r)
            {
                case DisplayRotation.Rotation270: // portrait upside down
                    this.videoRotation = 270;
                    break;
                case DisplayRotation.Rotation0: // landscape right
                    this.videoRotation = 0;
                    break;
                case DisplayRotation.Rotation180: // landscape left
                    this.videoRotation = 180;
                    break;
                case DisplayRotation.Rotation90: // portrait
                default:
                    this.videoRotation = 90;
                    break;
            }
#else
            // iOS/Android: Rotation0 means portrait
            switch (r)
            {
                case DisplayRotation.Rotation270: // landscape left
                    this.videoRotation = 180;
                    break;
                case DisplayRotation.Rotation0: // portrait
                    this.videoRotation = 90;
                    break;
                case DisplayRotation.Rotation180: // portrait upside down
                    this.videoRotation = 270;
                    break;
                case DisplayRotation.Rotation90: // landscape right
                default:
                    this.videoRotation = 0;
                    break;
            }
#endif
            VAST.Common.Log.DebugFormat("Current rotation: interface {0} camera {1}", r, this.videoRotation);

        }

        /// <summary>
        /// Recreates the video capture source with updated rotation settings.
        /// Stops and restarts the server to apply the new orientation.
        /// </summary>
        private void recreateVideoCaptureSource()
        {
            try
            {
                if (this.server != null)
                {
                    this.stopServer();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        this.btnStart_Clicked(null, null);
                    });
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    string s = string.Format("Failed to change orientation: {0}", ex);
                    await MauiProgram.VastDisplayAlert(this, "Capture Demo", s, "OK");
                });
            }
        }

    }

}
