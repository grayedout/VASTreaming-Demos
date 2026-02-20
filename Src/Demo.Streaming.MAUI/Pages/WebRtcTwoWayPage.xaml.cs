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

    /// <summary>
    /// Demo page for WebRTC two-way communication functionality.
    /// Demonstrates bidirectional audio/video communication using WebRTC protocol,
    /// including local camera capture and remote peer playback.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class WebRtcTwoWayPage : ContentPage
    {

        /// <summary>
        /// The WebRTC WebSocket two-way session for bidirectional communication.
        /// </summary>
        private VAST.WebRTC.WebRtcWsTwoWaySession rtcWsTwoWaySession = null;

        /// <summary>
        /// List of available video capture devices on the system.
        /// </summary>
        private List<VAST.Capture.VideoCaptureDeviceDescriptor> videoDevices = null;

        /// <summary>
        /// List of available audio capture devices on the system.
        /// </summary>
        private List<VAST.Capture.AudioCaptureDeviceDescriptor> audioDevices = null;

        /// <summary>
        /// The output video width in pixels.
        /// </summary>
        private int videoWidth = 1280;

        /// <summary>
        /// The output video height in pixels.
        /// </summary>
        private int videoHeight = 720;

        /// <summary>
        /// The output video frame rate.
        /// </summary>
        private VAST.Common.Rational videoFramerate = new VAST.Common.Rational(30);

        /// <summary>
        /// The video rotation angle in degrees to compensate for device orientation.
        /// </summary>
        private int videoRotation = 0;

        /// <summary>
        /// Creates a new instance of <see cref="WebRtcTwoWayPage"/>.
        /// </summary>
        public WebRtcTwoWayPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called when the page size is allocated, handles device orientation changes.
        /// </summary>
        /// <param name="width">The allocated width.</param>
        /// <param name="height">The allocated height.</param>
        /// <remarks>
        /// When the device rotation changes during an active session, the conference is restarted
        /// to apply the new camera orientation.
        /// </remarks>
        protected override void OnSizeAllocated(double width, double height)
        {

            base.OnSizeAllocated(width, height);

            int oldRotation = this.videoRotation;
            this.updateRotation();
            if (oldRotation == this.videoRotation) return;

            // Restart the conference if rotation changed during active session
            if (this.rtcWsTwoWaySession != null)
            {
                Task.Run(async () => { await this.restartConference(); });
            }

        }

        /// <summary>
        /// Handles the page navigated to event by initializing playback parameters and enumerating devices.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        /// <remarks>
        /// Configures both local and remote preview controls with low latency rendering
        /// and compatible renderer type for optimal WebRTC performance.
        /// </remarks>
        private void ContentPage_NavigatedTo(object sender, NavigatedToEventArgs e)
        {

            this.updateRotation();

            // Configure remote preview for low latency playback
            this.remotePreview.PlaybackParameters.VideoDecoderParameters.AllowHardwareAcceleration = true;
            this.remotePreview.PlaybackParameters.RenderingStrategy = VAST.Media.RenderingStrategy.LowLatency;
            this.remotePreview.PlaybackParameters.VideoRendererType = VAST.Media.VideoRendererType.Compatible;
            this.remotePreview.AudioStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;
            this.remotePreview.VideoStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;

            // Configure local preview for low latency rendering
            this.localPreview.RenderingStrategy = VAST.Media.RenderingStrategy.LowLatency;
            this.localPreview.RendererType = VAST.Media.VideoRendererType.Compatible;

            // Enumerate available capture devices in background
            Task.Run(async () =>
            {
                await this.enumerateDevices();
            });

        }

        /// <summary>
        /// Handles the page navigating from event by stopping the WebRTC session.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {
            this.stop();
        }

        /// <summary>
        /// Handles the Controls button click event to toggle the settings controls overlay panel.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Toggles visibility of both the controls panel and the transparent dismiss layer behind it.
        /// The dismiss layer captures taps outside the panel to close it.
        /// </remarks>
        private void btnControls_Clicked(object sender, EventArgs e)
        {
            this.controlsPanel.IsVisible = !this.controlsPanel.IsVisible;
            this.panelDismissLayer.IsVisible = this.controlsPanel.IsVisible;
        }

        /// <summary>
        /// Handles taps on the transparent dismiss layer to close the controls panel.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The tap event arguments.</param>
        /// <remarks>
        /// The dismiss layer is a full-screen transparent BoxView that sits between the video preview
        /// and the overlay panel in the z-order. Tapping anywhere outside the panel closes it.
        /// </remarks>
        private void dismissPanel_Tapped(object sender, TappedEventArgs e)
        {
            this.controlsPanel.IsVisible = false;
            this.panelDismissLayer.IsVisible = false;
        }

        /// <summary>
        /// Handles page size changes to switch between landscape and portrait preview layouts.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// In landscape orientation, the local and remote previews are placed side by side.
        /// In portrait orientation, they are stacked vertically. Buttons and the controls panel
        /// span all columns/rows to remain accessible in both orientations.
        /// </remarks>
        private void ContentPage_SizeChanged(object sender, EventArgs e)
        {
            if (this.Width > this.Height)
            {
                // Landscape: side by side
                this.gridPreview.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Star) };
                this.gridPreview.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) };
                Grid.SetRow(this.localPreview, 0);
                Grid.SetColumn(this.localPreview, 0);
                Grid.SetRow(this.remotePreview, 0);
                Grid.SetColumn(this.remotePreview, 1);
                Grid.SetColumnSpan(this.btnControls, 2);
                Grid.SetColumnSpan(this.btnEmailLog, 2);
                Grid.SetColumnSpan(this.controlsPanel, 2);
            }
            else
            {
                // Portrait: stacked vertically
                this.gridPreview.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Star) };
                this.gridPreview.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
                Grid.SetRow(this.localPreview, 0);
                Grid.SetColumn(this.localPreview, 0);
                Grid.SetRow(this.remotePreview, 1);
                Grid.SetColumn(this.remotePreview, 0);
                Grid.SetColumnSpan(this.btnControls, 1);
                Grid.SetColumnSpan(this.btnEmailLog, 1);
                Grid.SetColumnSpan(this.controlsPanel, 1);
            }
            Grid.SetRowSpan(this.btnControls, this.gridPreview.RowDefinitions.Count);
            Grid.SetRowSpan(this.btnEmailLog, this.gridPreview.RowDefinitions.Count);
            Grid.SetRowSpan(this.controlsPanel, this.gridPreview.RowDefinitions.Count);
        }

        /// <summary>
        /// Handles the play button click event to start the WebRTC two-way session.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnPlay_Clicked(object sender, EventArgs e)
        {
            await this.start();
        }

        /// <summary>
        /// Handles the stop button click event to stop the WebRTC two-way session.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStop_Clicked(object sender, EventArgs e)
        {
            this.stop();
        }

        /// <summary>
        /// Handles the send log button click event to send diagnostic logs to support.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnSendLog_Clicked(object sender, EventArgs e)
        {
            if (await VAST.Common.License.SendLog("MAUI WebRTC two-way issue"))
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "The log has been sent successfully", "OK");
            }
            else
            {
                await MauiProgram.VastDisplayAlert(this, "Error", "Failed to send the log! Please grab it manually and send to support@vastreaming.net.", "OK");
            }
        }

        /// <summary>
        /// Starts the WebRTC two-way session with configured video and audio capture sources.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// Creates a WebRTC session with the signaling server URI and room name from the UI.
        /// If video device is selected, creates a video capture source with H.264 encoding
        /// using constrained baseline profile for maximum compatibility.
        /// If audio device is selected, creates an audio capture source with PCM output.
        /// </remarks>
        private async Task start()
        {

            try
            {

                // Create WebRTC session with signaling server and room configuration
                this.rtcWsTwoWaySession = new VAST.WebRTC.WebRtcWsTwoWaySession(this.textUri.Text, this.textRoom.Text);
                this.rtcWsTwoWaySession.IceServers = "turn:test.vastreaming.net:3478";

                // Configure video capture source if a device is selected
                VAST.Capture.IVideoCaptureSource2 videoCaptureSource = null;
                if (this.pickerVideoDevice.SelectedIndex > 0)
                {

                    // Define desired video output parameters
                    var mt = new VAST.Common.MediaType
                    {
                        ContentType = VAST.Common.ContentType.Video,
                        CodecId = VAST.Common.Codec.H264,
                        Width = this.videoWidth,
                        Height = this.videoHeight,
                        Framerate = this.videoFramerate,
                        Bitrate = this.videoWidth * this.videoHeight * 3,
                    };

                    // Swap width and height if necessary based on current orientation
                    if ((this.videoRotation % 180) == 90)
                    {
                        if (mt.Width > mt.Height)
                        {
                            mt.Width = this.videoHeight;
                            mt.Height = this.videoWidth;
                        }
                    }
                    else
                    {
                        if (mt.Width < mt.Height)
                        {
                            mt.Width = this.videoHeight;
                            mt.Height = this.videoWidth;
                        }
                    }

                    // Find the capture mode closest to desired parameters
                    var dev = this.videoDevices[this.pickerVideoDevice.SelectedIndex - 1];
                    var captureMode = dev.FindClosestMatch(mt);

                    // Create and configure video capture source
                    videoCaptureSource = VAST.Media.SourceFactory.CreateVideoCapture(dev.DeviceId);
                    videoCaptureSource.CaptureParameters.EnableDeviceRecovery = false;
                    videoCaptureSource.Renderer = this.localPreview.Renderer;
                    videoCaptureSource.CaptureMode = captureMode;
                    videoCaptureSource.Rotation = this.videoRotation;

                    // Set encoding parameters for WebRTC compatibility
                    mt.Metadata.Add("KeyframeInterval", "30");
                    mt.Metadata.Add("Profile", (66 | 0x100).ToString()); // Constrained baseline profile

                    await videoCaptureSource.SetDesiredOutputType(0, mt);

                }

                // Configure audio capture source if a device is selected
                VAST.Capture.IAudioCaptureSource2 audioCaptureSource = null;
                if (this.pickerAudioDevice.SelectedIndex > 0)
                {
                    audioCaptureSource = VAST.Media.SourceFactory.CreateAudioCapture(this.audioDevices[this.pickerAudioDevice.SelectedIndex - 1].DeviceId);
                    await audioCaptureSource.SetDesiredOutputType(0, new VAST.Common.MediaType
                    {
                        ContentType = VAST.Common.ContentType.Audio,
                        CodecId = VAST.Common.Codec.PCM,
                        SampleFormat = VAST.Common.SampleFormat.S16,
                        SampleRate = 44100,
                        Channels = 1,
                    });
                }

                // Configure the session with local and remote peers
                this.rtcWsTwoWaySession.SetLocalPeer(videoCaptureSource, audioCaptureSource);
                this.rtcWsTwoWaySession.AddRemotePeer(this.remotePreview);

                // Start the WebRTC session
                this.rtcWsTwoWaySession.Start();

            }
            catch (Exception ex)
            {
                this.stop();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await MauiProgram.VastDisplayAlert(this, "Error", ex.Message, "OK");
                });
            }

        }

        /// <summary>
        /// Stops the WebRTC two-way session and releases all associated resources.
        /// </summary>
        private void stop()
        {

            try
            {

                if (this.rtcWsTwoWaySession != null)
                {
                    this.rtcWsTwoWaySession.Stop();
                    this.rtcWsTwoWaySession.Dispose();
                    this.rtcWsTwoWaySession = null;
                }

            }
            catch
            {
            }

        }

        /// <summary>
        /// Enumerates available video and audio capture devices and populates the device picker controls.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// Video devices are prefixed with their framework type ([MF], [DS], [NDI]).
        /// The "Front Camera" device is automatically selected if available, otherwise the first device is selected.
        /// Audio devices are similarly prefixed with their framework type ([MF], [DS], [WASAPI], [ASIO], [NDI]).
        /// </remarks>
        private async Task enumerateDevices()
        {

            // Enumerate video capture devices
            this.videoDevices = await VAST.Capture.VideoDeviceEnumerator.Enumerate(VAST.Common.MediaFramework.Unknown);
            if (this.videoDevices.Count > 0)
            {

                // Build display list with framework prefixes
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

                // Update UI on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {

                    this.pickerVideoDevice.ItemsSource = src;
                    this.pickerVideoDevice.ItemsSource = this.pickerVideoDevice.GetItemsAsArray();
                    this.pickerVideoDevice.IsEnabled = true;

                    // Try to select "Front Camera" by default
                    int selectedIndex = -1;
                    for (int i = 0; i < this.videoDevices.Count; ++i)
                    {
                        if (this.videoDevices[i].Name == "Front Camera")
                        {
                            selectedIndex = i + 1;
                            break;
                        }
                    }

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
                });
            }

            // Enumerate audio capture devices
            this.audioDevices = await VAST.Capture.AudioDeviceEnumerator.Enumerate(VAST.Common.MediaFramework.Unknown);
            if (this.audioDevices.Count > 0)
            {

                // Build display list with framework prefixes
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

                // Update UI on main thread and select first device
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.pickerAudioDevice.ItemsSource = src;
                    this.pickerAudioDevice.ItemsSource = this.pickerAudioDevice.GetItemsAsArray();
                    this.pickerAudioDevice.IsEnabled = true;
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
                });
            }

        }

        /// <summary>
        /// Updates the video rotation angle based on current device display orientation.
        /// </summary>
        /// <remarks>
        /// MAUI rotation handling differs between platforms:
        /// - On iOS, and Android: Rotation0 means portrait mode
        /// - On Windows, MacCatalyst: Rotation0 means landscape right
        /// The camera rotation is adjusted accordingly to compensate for device orientation.
        /// </remarks>
        private void updateRotation()
        {
            var r = DeviceDisplay.Current.MainDisplayInfo.Rotation;

#if __MACCATALYST__ || WINDOWS
            // Windows, MacCatalyst has different rotation semantics
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
            // iOS, and Android rotation handling
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
        /// Restarts the WebRTC conference after an orientation change.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// Stops the current session and starts a new one to apply the updated video rotation.
        /// </remarks>
        private async Task restartConference()
        {
            try
            {
                this.stop();
                await this.start();
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
