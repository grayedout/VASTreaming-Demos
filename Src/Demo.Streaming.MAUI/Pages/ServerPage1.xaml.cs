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
    using System.Runtime.Versioning;

    /// <summary>
    /// Demo page for RTSP streaming server functionality.
    /// Demonstrates running an RTSP server that captures video/audio from local devices
    /// and streams them to connected clients over RTSP protocol.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class ServerPage1 : ContentPage
    {

        /// <summary>
        /// The currently active video capture source for the server.
        /// </summary>
        private VAST.Capture.IVideoCaptureSource2 activeVideoCaptureSource = null;

        /// <summary>
        /// The currently active audio capture source for the server.
        /// </summary>
        private VAST.Capture.IAudioCaptureSource2 activeAudioCaptureSource = null;

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
        /// Indicates whether video streaming is enabled.
        /// </summary>
        private bool allowVideo = true;

        /// <summary>
        /// Indicates whether audio streaming is enabled.
        /// </summary>
        private bool allowAudio = true;

        /// <summary>
        /// The video rotation angle in degrees to compensate for device orientation.
        /// </summary>
        private int videoRotation = 0;

        /// <summary>
        /// The local IP address for the RTSP server.
        /// </summary>
        private string localIp = null;

        /// <summary>
        /// The port number for the RTSP server.
        /// </summary>
        private int rtspPort = 10554;

        /// <summary>
        /// The RTSP server instance.
        /// </summary>
        private VAST.RTSP.RtspServer server = null;

        /// <summary>
        /// The aggregated network source that combines video and audio capture sources.
        /// </summary>
        private VAST.Network.AggregatedNetworkSource serverSource = null;

        /// <summary>
        /// The publishing point name for the stream (default: "camera").
        /// </summary>
        private string publishName = "camera";

        /// <summary>
        /// List of media types for streams being published.
        /// </summary>
        private List<VAST.Common.MediaType> mediaStreams = new List<VAST.Common.MediaType>();

        /// <summary>
        /// Dictionary of connected clients indexed by their endpoint.
        /// </summary>
        private Dictionary<EndPoint, VAST.Media.IMediaSink> connectedClients = new Dictionary<EndPoint, Media.IMediaSink>();

        /// <summary>
        /// Creates a new instance of <see cref="ServerPage1"/>.
        /// </summary>
        /// <remarks>
        /// Initializes picker controls with default values, configures encoding framework options,
        /// and detects the local IP address for the RTSP server.
        /// </remarks>
        public ServerPage1()
        {

            InitializeComponent();

            // Initialize picker controls with default values
            this.pickerVideoProfile.SelectedIndex = 1;
            this.pickerVideoLevel.SelectedIndex = 0;
            this.pickerAudioSampleRate.SelectedIndex = 1;
            this.pickerAudioChannels.SelectedIndex = 1;

            // Configure platform-specific encoding framework options
            // Windows supports Media Foundation and FFmpeg encoders
            // Other platforms use the built-in encoder
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

            // Auto-detect the local IP address for the RTSP server
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
        /// Handles the page navigating from event by stopping the RTSP server and releasing resources.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {
            this.stopServer();
        }

        /// <summary>
        /// Handles the page unloaded event by delegating to the navigating from handler.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void ContentPage_Unloaded(object sender, EventArgs e)
        {
            this.ContentPage_NavigatingFrom(null, null);
        }

        /// <summary>
        /// Handles the start button click to initialize and start the RTSP server.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Requires a valid local IP address to be detected during initialization.
        /// Displays the server URI for clients to connect to upon successful start.
        /// </remarks>
        private async void btnStart_Clicked(object sender, EventArgs e)
        {
            try
            {
                this.labelServerUri.IsVisible = true;
                if (string.IsNullOrEmpty(localIp))
                {
                    this.labelServerUri.Text = "Failed to start RTMP server: can't detect local IP";
                }
                else
                {
                    await this.startServer();
                }
            }
            catch (Exception ex)
            {
                this.labelServerUri.Text = "Failed to start RTMP server: " + ex.Message;
            }
        }

        /// <summary>
        /// Handles the stop button click to stop the RTSP server and release resources.
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
            if (await VAST.Common.License.SendLog("MAUI server #1 issue"))
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
            // Index 0 is "No Video", actual devices start at index 1
            int index = this.pickerVideoDevice.SelectedIndex - 1;
            this.videoDevice = (index >= 0 && this.videoDevices != null && this.videoDevices.Count > index) ? this.videoDevices[index] : null;

            // Populate capture mode picker with available modes for selected device
            if (this.pickerVideoDevice.SelectedIndex > 0 && this.videoDevice != null && this.videoDevice.CaptureModes != null && this.videoDevice.CaptureModes.Count > 0)
            {
                List<string> src = new List<string>();
                foreach (VAST.Capture.VideoCaptureMode captureMode in this.videoDevice.CaptureModes)
                {
                    src.Add(string.Format("{0}x{1}, {2:0.##} fps{3}", captureMode.Width, captureMode.Height, captureMode.Framerate, (captureMode.PixelFormat == VAST.Common.PixelFormat.None) ? "" : $", {captureMode.PixelFormat}"));
                }
                this.pickerVideoCaptureMode.ItemsSource = src;
                this.pickerVideoCaptureMode.ItemsSource = this.pickerVideoCaptureMode.GetItemsAsArray();
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
                // No device selected or no modes available
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
            // Index 0 is "No Audio", actual devices start at index 1
            int index = this.pickerAudioDevice.SelectedIndex - 1;
            this.audioDevice = (index >= 0 && this.audioDevices != null && this.audioDevices.Count > index) ? this.audioDevices[index] : null;

            // Populate capture mode picker with available modes for selected device
            if (this.pickerAudioDevice.SelectedIndex > 0 && this.audioDevice != null && this.audioDevice.CaptureModes != null && this.audioDevice.CaptureModes.Count > 0)
            {
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
                // No device selected or no modes available
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
        /// Handles new stream notifications from the aggregated source.
        /// Records the media type for each stream to provide to connecting clients.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The new stream event arguments containing stream index and media type.</param>
        /// <remarks>
        /// Thread-safe via locking to prevent concurrent modification of the media streams list.
        /// Dynamically expands the streams list if a higher stream index is reported.
        /// </remarks>
        private void Publisher_NewStream(object sender, VAST.Media.NewStreamEventArgs e)
        {
            lock (this)
            {
                // Ensure the list is large enough to hold the new stream
                while (this.mediaStreams.Count < e.StreamCount)
                {
                    this.mediaStreams.Add(null);
                }
                this.mediaStreams[e.StreamIndex] = e.MediaType;
            }
        }

        /// <summary>
        /// Handles new media sample notifications from the aggregated source.
        /// Distributes each sample to all connected RTSP clients.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The new sample event arguments containing the media sample.</param>
        /// <remarks>
        /// Thread-safe via locking to prevent concurrent modification of the clients dictionary.
        /// Each connected client receives a copy of every media sample for streaming.
        /// </remarks>
        private void Publisher_NewSample(object sender, VAST.Media.NewSampleEventArgs e)
        {
            lock (this)
            {
                // Distribute sample to all connected clients
                foreach (VAST.Media.IMediaSink client in this.connectedClients.Values)
                {
                    client.PushMedia(e.Sample.StreamIndex, e.Sample);
                }
            }
        }

        /// <summary>
        /// Handles state changes from the aggregated source.
        /// Starts the source when it transitions to the Opened state.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The new media state.</param>
        private void Publisher_StateChanged(object sender, Media.MediaState e)
        {
            if (e == Media.MediaState.Opened)
            {
                this.serverSource.Start();
            }
        }

        /// <summary>
        /// Handles client disconnection events from the RTSP server.
        /// Removes the disconnected client from the connected clients dictionary.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="args">The transport event arguments containing the client endpoint.</param>
        /// <remarks>
        /// Thread-safe via locking to prevent concurrent modification of the clients dictionary.
        /// Logs an error if the disconnecting peer is not found in the dictionary.
        /// </remarks>
        private void Server_Disconnected(object sender, VAST.Transport.TransportArgs args)
        {
            lock (this)
            {
                VAST.Media.IMediaSink sink = null;
                if (this.connectedClients.TryGetValue(args.EndPoint, out sink))
                {
                    this.connectedClients.Remove(args.EndPoint);
                }
                else
                {
                    VAST.Common.Log.ErrorFormat("Disconnected peer {0} not found", args.EndPoint);
                }
            }
        }

        /// <summary>
        /// Handles publisher connection attempts to the RTSP server.
        /// This server is receive-only, so all publisher connections are rejected.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="publisher">The connecting publisher source.</param>
        private void Server_PublisherConnected(object sender, VAST.Network.INetworkSource publisher)
        {
            // This server only streams out, it doesn't accept incoming streams
            publisher.Accept = false;
        }

        /// <summary>
        /// Handles new client connections to the RTSP server.
        /// Validates the requested stream path and configures the client with available media streams.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="client">The connecting client sink.</param>
        /// <remarks>
        /// Thread-safe via locking to prevent concurrent modification of the clients dictionary.
        /// Only accepts clients requesting the published stream name.
        /// Provides all available media streams (video/audio) to the connecting client.
        /// </remarks>
        private void Server_ClientConnected(object sender, VAST.Network.INetworkSink client)
        {

            lock (this)
            {

                // Verify the client is requesting our published stream
                if (client.PublishingPath != this.publishName)
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} is not published", client.PublishingPath);
                    return;
                }

                // Accept the connection and configure media streams
                client.Accept = true;

                // Add all available media streams to the client
                int index = 0;
                foreach (VAST.Common.MediaType mt in this.mediaStreams)
                {
                    client.AddStream(index++, mt);
                }

                // Track the connected client
                this.connectedClients.Add(client.EndPoint, client);

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
                            selectedIndex = i + 1;
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
                    this.pickerAudioDevice.SelectedIndex = 1;
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
        /// Starts the RTSP server and initializes video/audio capture.
        /// </summary>
        /// <returns>A task representing the asynchronous server start operation.</returns>
        /// <remarks>
        /// Configures the server to listen on all network interfaces on the specified RTSP port.
        /// Forces TCP interleaved transport for RTP to ensure reliable delivery through firewalls.
        /// Displays the RTSP URI that clients can use to connect and view the stream.
        /// </remarks>
        private async Task startServer()
        {

            try
            {

                // Prevent starting if already running
                if (this.server != null) return;

                // Configure server to listen on all interfaces
                var pars = new VAST.RTSP.RtspServerParameters();
                pars.RtspEndPoints.Add(new IPEndPoint(IPAddress.Any, this.rtspPort));

                // Create and configure the RTSP server instance
                this.server = new VAST.RTSP.RtspServer(10, pars);
                this.server.Disconnected += Server_Disconnected;
                this.server.PublisherConnected += Server_PublisherConnected;
                this.server.ClientConnected += Server_ClientConnected;

                // Force TCP interleaved transport for better NAT/firewall traversal
                VAST.RTSP.RtspGlobal.ForceRtpTransport = VAST.RTP.RtpTransportType.TcpInterleaved;

                // Start the server
                this.server.Start();

                // Display connection URI for users
                this.labelServerUri.Text = $"Open URI rtsp://{this.localIp}:{this.rtspPort}/{this.publishName} in your player to watch the stream";

                // Initialize and start capture sources
                await this.startCapture();

            }
            catch (Exception ex)
            {
                this.stopServer();
                await MauiProgram.VastDisplayAlert(this, "Error", $"Server start exception: {ex}", "OK");
            }

        }

        /// <summary>
        /// Stops the RTSP server and releases all associated resources.
        /// </summary>
        /// <remarks>
        /// Stops capture sources first, then shuts down the RTSP server.
        /// Clears the connected clients dictionary and hides the server URI label.
        /// </remarks>
        private void stopServer()
        {

            try
            {

                // Stop capture sources first
                this.stopCapture();

                // Stop and dispose the RTSP server
                if (this.server != null)
                {
                    this.server.Stop();
                    this.server = null;
                }

                // Clear connected clients and hide UI
                this.connectedClients = new Dictionary<EndPoint, VAST.Media.IMediaSink>();
                this.labelServerUri.IsVisible = false;

            }
            catch
            {
            }

        }

        /// <summary>
        /// Creates video and audio capture sources based on current device and mode selections.
        /// </summary>
        /// <returns>A task representing the asynchronous capture source creation operation.</returns>
        /// <remarks>
        /// Configures the encoding framework (Media Foundation, FFmpeg, or Builtin) and hardware
        /// acceleration settings before creating sources. Each capture source uses reference counting
        /// via AddRef() to manage lifetime. The video capture source is configured with rotation
        /// compensation for mobile device orientation. After creating sources, calls
        /// <see cref="updateCaptureOutput"/> to configure encoding parameters.
        /// </remarks>
        private async Task createCaptureSources()
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

            // Apply hardware acceleration setting
            bool allowHardwareAcceleration = this.checkBoxHardwareAcceleration.IsChecked;

            // Configure global encoder/decoder defaults
            VAST.Media.DecoderParameters.DefaultPreferredMediaFramework = mediaFramework;
            VAST.Media.DecoderParameters.DefaultAllowHardwareAcceleration = allowHardwareAcceleration;
            VAST.Media.EncoderParameters.DefaultPreferredMediaFramework = mediaFramework;
            VAST.Media.EncoderParameters.DefaultAllowHardwareAcceleration = allowHardwareAcceleration;

            // Create video capture source if video is enabled and device is selected
            if (this.allowVideo && this.videoDevice != null)
            {
                if (this.activeVideoCaptureSource == null)
                {
                    this.activeVideoCaptureSource = VAST.Media.SourceFactory.CreateVideoCapture(this.videoDevice.DeviceId, this.videoCaptureMode);
                    this.activeVideoCaptureSource.Rotation = this.videoRotation;
                    this.activeVideoCaptureSource.AddRef();
                }
            }

            // Create audio capture source if audio is enabled and device is selected
            if (this.allowAudio && this.audioDevice != null)
            {
                if (this.activeAudioCaptureSource == null)
                {
                    this.activeAudioCaptureSource = VAST.Media.SourceFactory.CreateAudioCapture(this.audioDevice.DeviceId, this.audioCaptureMode);
                    this.activeAudioCaptureSource.AddRef();
                }
            }

            // Configure encoding output parameters
            await this.updateCaptureOutput();

        }

        /// <summary>
        /// Configures the desired output encoding parameters for video and audio capture sources.
        /// </summary>
        /// <returns>A task representing the asynchronous configuration operation.</returns>
        /// <remarks>
        /// <para>
        /// For video, configures H.264 encoding with the following parameters from UI controls:
        /// resolution (width/height), framerate, bitrate, keyframe interval, profile (Baseline/Main/High),
        /// and level. Validates that resolution is between 100x100 and 3840x2160, bitrate is between
        /// 100kbps and 10Mbps, and keyframe interval is between 1 and 1000 frames.
        /// </para>
        /// <para>
        /// For audio, configures AAC encoding with sample rate, channel count, and bitrate.
        /// On Android, uses capture mode parameters directly since audio resampling is not available.
        /// Validates that audio bitrate is between 8kbps and 256kbps.
        /// </para>
        /// </remarks>
        /// <exception cref="Exception">Thrown when validation fails for any encoding parameter.</exception>
        private async Task updateCaptureOutput()
        {

            if (this.allowVideo)
            {

                VAST.Capture.IVideoCaptureSource2 captureSource = this.activeVideoCaptureSource;
                if (captureSource != null)
                {

                    // Build video media type with H.264 encoding parameters
                    var mt = new VAST.Common.MediaType { ContentType = VAST.Common.ContentType.Video };

                    // Validate and set output resolution
                    int outputWidth = int.Parse(this.tboxVideoWidth.Text);
                    if (outputWidth < 100 || outputWidth > 3840) throw new Exception("Please enter proper video width");
                    mt.Width = outputWidth;

                    int outputHeight = int.Parse(this.tboxVideoHeight.Text);
                    if (outputHeight < 100 || outputHeight > 2160) throw new Exception("Please enter proper video height");
                    mt.Height = outputHeight;

                    // Swap width and height if necessary based on current orientation
                    if ((this.videoRotation % 180) == 90)
                    {
                        if (mt.Width > mt.Height)
                        {
                            mt.Width = outputHeight;
                            mt.Height = outputWidth;
                        }
                    }
                    else
                    {
                        if (mt.Width < mt.Height)
                        {
                            mt.Width = outputHeight;
                            mt.Height = outputWidth;
                        }
                    }

                    // Set framerate and codec
                    mt.Framerate = new VAST.Common.Rational(int.Parse(this.tboxVideoFramerate.Text));
                    mt.CodecId = VAST.Common.Codec.H264;

                    // Validate and set video bitrate
                    int videoBitrate = int.Parse(tboxVideoBitrate.Text);
                    if (videoBitrate < 100000 || videoBitrate > 10000000) throw new Exception("Please enter proper video bitrate");
                    mt.Bitrate = videoBitrate;

                    // Validate and set keyframe interval (GOP size)
                    int videoKeyframeInterval = int.Parse(tboxVideoKeyframeInterval.Text);
                    if (videoKeyframeInterval < 1 || videoKeyframeInterval > 1000) throw new Exception("Please enter proper video keyframe interval");
                    mt.Metadata.Add("KeyframeInterval", videoKeyframeInterval.ToString());

                    // Set H.264 profile if not auto
                    if (this.pickerVideoProfile.SelectedIndex > 0)
                    {
                        switch (this.pickerVideoProfile.SelectedIndex)
                        {
                            case 0: // auto - no profile specified
                                break;
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

                    // Set H.264 level if specified (e.g., 3.1 -> 31, 4.0 -> 40)
                    if (this.pickerVideoLevel.SelectedIndex > 0)
                    {
                        mt.Metadata.Add("Level", ((int)(float.Parse((string)this.pickerVideoLevel.SelectedItem) * 10)).ToString());
                    }

                    // Apply encoding configuration to capture source
                    await captureSource.SetDesiredOutputType(0, mt);

                }

            }

            if (this.allowAudio)
            {

                if (this.activeAudioCaptureSource != null)
                {

                    // Build audio media type with AAC encoding parameters
                    var mt = new VAST.Common.MediaType { ContentType = VAST.Common.ContentType.Audio };

                    // On Android, use capture mode parameters directly (no resampler available)
                    if (DeviceInfo.Current.Platform == DevicePlatform.Android && this.audioCaptureMode != null)
                    {
                        mt.SampleRate = this.audioCaptureMode.SampleRate;
                        mt.Channels = this.audioCaptureMode.Channels;
                    }
                    else
                    {
                        // On other platforms, use values from UI picker controls
                        mt.SampleRate = int.Parse((string)this.pickerAudioSampleRate.SelectedItem);
                        mt.Channels = int.Parse((string)this.pickerAudioChannels.SelectedItem);
                    }

                    // Set AAC codec and validate bitrate
                    mt.CodecId = VAST.Common.Codec.AAC;
                    int audioBitrate = int.Parse(tboxAudioBitrate.Text);
                    if (audioBitrate < 8000 || audioBitrate > 256000) throw new Exception("Please enter proper audio bitrate");
                    mt.Bitrate = audioBitrate;

                    // Apply encoding configuration to capture source
                    await this.activeAudioCaptureSource.SetDesiredOutputType(0, mt);

                }

            }

        }

        /// <summary>
        /// Starts video and audio capture by creating sources and initializing the aggregated network source.
        /// </summary>
        /// <returns>A task representing the asynchronous capture start operation.</returns>
        /// <remarks>
        /// Creates capture sources via <see cref="createCaptureSources"/>, then creates an
        /// <see cref="VAST.Network.AggregatedNetworkSource"/> that combines video and audio streams
        /// into a single source. The aggregated source uses reference counting (AddRef) for lifetime
        /// management. Event handlers are subscribed to receive new stream notifications, media samples,
        /// and state changes. Opening the aggregated source triggers the StateChanged event, which
        /// starts the source when it reaches the Opened state.
        /// </remarks>
        private async Task startCapture()
        {

            // Create and configure video/audio capture sources
            await this.createCaptureSources();

            // Create aggregated source to combine video and audio streams
            if (this.serverSource == null)
            {
                this.serverSource = new VAST.Network.AggregatedNetworkSource();
                this.serverSource.AddRef();

                // Add active capture sources to the aggregated source
                if (this.activeVideoCaptureSource != null) this.serverSource.AddSource(this.activeVideoCaptureSource);
                if (this.activeAudioCaptureSource != null) this.serverSource.AddSource(this.activeAudioCaptureSource);

                // Subscribe to events for stream management and sample distribution
                this.serverSource.NewStream += Publisher_NewStream;
                this.serverSource.NewSample += Publisher_NewSample;
                this.serverSource.StateChanged += Publisher_StateChanged;

                // Open the source (will start automatically when Opened state is reached)
                this.serverSource.Open();
            }

        }

        /// <summary>
        /// Stops video and audio capture and releases all associated resources.
        /// </summary>
        /// <remarks>
        /// First disconnects all connected RTSP clients by stopping their sinks.
        /// Then releases video and audio capture sources using reference counting (Release).
        /// Finally releases the aggregated network source and clears the media streams list.
        /// All source references are set to null after release to allow garbage collection.
        /// </remarks>
        private void stopCapture()
        {

            // Disconnect all connected RTSP clients
            foreach (KeyValuePair<EndPoint, VAST.Media.IMediaSink> entry in this.connectedClients)
            {
                entry.Value.Stop();
            }

            // Release video capture source
            if (this.activeVideoCaptureSource != null)
            {
                this.activeVideoCaptureSource.Release();
                this.activeVideoCaptureSource = null;
            }

            // Release audio capture source
            if (this.activeAudioCaptureSource != null)
            {
                this.activeAudioCaptureSource.Release();
                this.activeAudioCaptureSource = null;
            }

            // Release aggregated network source
            if (this.serverSource != null)
            {
                this.serverSource.Release();
                this.serverSource = null;
            }

            // Clear media stream type list
            this.mediaStreams = new List<VAST.Common.MediaType>();

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
        /// </summary>
        /// <remarks>
        /// Called when device orientation changes and the video capture needs to be
        /// restarted with the new rotation compensation. Stops all capture sources,
        /// then restarts them with the updated rotation value. Any errors during
        /// recreation are displayed to the user via an alert dialog on the main thread.
        /// </remarks>
        private async void recreateVideoCaptureSource()
        {
            try
            {
                if (this.server != null)
                {
                    // Stop existing capture and restart with new rotation
                    this.stopCapture();
                    await this.startCapture();
                }
            }
            catch (Exception ex)
            {
                // Display error on UI thread
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    string s = string.Format("Failed to change orientation: {0}", ex);
                    await MauiProgram.VastDisplayAlert(this, "Capture Demo", s, "OK");
                });
            }
        }

    }

}
