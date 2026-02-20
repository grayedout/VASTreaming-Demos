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
    /// Demo page for simple video and audio capture functionality.
    /// Demonstrates capture device enumeration, preview, and streaming to RTMP/RTSP servers.
    /// Supports device rotation handling, encoding configuration, and audio level monitoring.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class SimpleCapturePage : ContentPage
    {

        /// <summary>
        /// The default server URI for streaming (empty by default).
        /// </summary>
        private string defaultUri = "";

        /// <summary>
        /// The currently active video capture source.
        /// </summary>
        private VAST.Capture.IVideoCaptureSource2 activeVideoCaptureSource = null;

        /// <summary>
        /// The currently active audio capture source.
        /// </summary>
        private VAST.Capture.IAudioCaptureSource2 activeAudioCaptureSource = null;

        /// <summary>
        /// The media session used for local preview of captured video/audio.
        /// </summary>
        private VAST.Media.MediaSession previewSession = null;

        /// <summary>
        /// The media session used for streaming captured media to a server.
        /// </summary>
        private VAST.Media.MediaSession streamingSession = null;

        /// <summary>
        /// Indicates whether video capture is enabled.
        /// </summary>
        private bool allowVideo = true;

        /// <summary>
        /// Indicates whether audio capture is enabled.
        /// </summary>
        private bool allowAudio = true;

        /// <summary>
        /// The video renderer type to use for preview.
        /// </summary>
        private VAST.Media.VideoRendererType videoRendererType = VAST.Media.VideoRendererType.Best;

        /// <summary>
        /// Indicates whether the capture output is currently encoded (for streaming) or uncompressed (for preview only).
        /// </summary>
        private bool isOutputEncoded = false;

        /// <summary>
        /// The video rotation angle in degrees to compensate for device orientation.
        /// </summary>
        private int videoRotation = 0;

        /// <summary>
        /// The device monitor for tracking audio input levels.
        /// </summary>
        private VAST.Capture.DeviceMonitor deviceMonitor = null;

        /// <summary>
        /// The settings page instance. A new instance is created each time Settings is opened to avoid
        /// Android picker state restoration issues, with state copied from the previous instance.
        /// </summary>
        private SimpleCaptureSettingsPage settingsPage;

        /// <summary>
        /// Guard flag to prevent <see cref="ContentPage_NavigatingFrom"/> from disposing resources
        /// when navigating to the modal settings page. Set to true before pushing the settings modal
        /// and cleared immediately after the modal returns.
        /// </summary>
        private bool isOpeningSettings = false;

        /// <summary>
        /// Creates a new instance of <see cref="SimpleCapturePage"/>.
        /// </summary>
        public SimpleCapturePage()
        {

            InitializeComponent();

            this.settingsPage = new SimpleCaptureSettingsPage();
            this.settingsPage.DefaultUri = this.defaultUri;

        }

        /// <summary>
        /// Called when the page size is allocated, handles device orientation changes.
        /// </summary>
        /// <param name="width">The allocated width.</param>
        /// <param name="height">The allocated height.</param>
        /// <remarks>
        /// When the device rotation changes during capture, the capture source is recreated
        /// to apply the new camera orientation.
        /// </remarks>
        protected override void OnSizeAllocated(double width, double height)
        {

            base.OnSizeAllocated(width, height);

            int oldRotation = this.videoRotation;
            this.updateRotation();
            if (oldRotation == this.videoRotation) return;

            // Need to restart capture/preview when rotation changes
            Task.Run(() => { this.recreateVideoCaptureSource(); });

        }

        /// <summary>
        /// Handles the page navigated to event by updating rotation and enumerating devices.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatedTo(object sender, NavigatedToEventArgs e)
        {
            this.updateRotation();
            Task.Run(async () =>
            {
                await this.settingsPage.EnumerateDevices();
            });
        }

        /// <summary>
        /// Handles the page navigating from event by stopping all capture sessions and releasing resources.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        /// <remarks>
        /// Skipped when navigating to the modal settings page (guarded by <see cref="isOpeningSettings"/>).
        /// Disposes the audio level monitor and stops both streaming and preview sessions.
        /// </remarks>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {

            // Don't dispose resources when opening the settings modal
            if (this.isOpeningSettings) return;

            // Dispose the audio level monitor
            if (this.deviceMonitor != null)
            {
                this.deviceMonitor.Dispose();
                this.deviceMonitor = null;
            }

            // Stop all active sessions
            this.stopStreaming().Wait();
            this.stopPreview();

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
        /// Handles the Settings button click to open the modal settings page.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Settings cannot be changed while preview or streaming is active because
        /// encoder/decoder parameters require session recreation.
        /// A new settings page instance is created each time to work around Android picker
        /// state restoration issues. State is copied from the previous instance via
        /// <see cref="SimpleCaptureSettingsPage.InitializeFrom"/>.
        /// </remarks>
        private async void btnSettings_Clicked(object sender, EventArgs e)
        {
            if (this.previewSession != null || this.streamingSession != null)
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "Stop preview and streaming to apply new settings", "OK");
            }
            else
            {
                // Create a fresh settings page and copy state from the current one
                var newSettingsPage = new SimpleCaptureSettingsPage();
                newSettingsPage.InitializeFrom(this.settingsPage);
                this.settingsPage = newSettingsPage;

                // Guard against NavigatingFrom disposing resources during modal navigation
                this.isOpeningSettings = true;
                await Navigation.PushModalAsync(new NavigationPage(this.settingsPage));
                this.isOpeningSettings = false;
            }
        }

        /// <summary>
        /// Handles the start preview button click event.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnStartPreview_Clicked(object sender, EventArgs e)
        {
            await this.startPreview();
        }

        /// <summary>
        /// Handles the stop preview button click event.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStopPreview_Clicked(object sender, EventArgs e)
        {
            this.stopPreview();
        }

        /// <summary>
        /// Handles the start streaming button click event.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnStartStreaming_Clicked(object sender, EventArgs e)
        {
            await this.startStreaming();
        }

        /// <summary>
        /// Handles the stop streaming button click event.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnStopStreaming_Clicked(object sender, EventArgs e)
        {
            await this.stopStreaming();
        }

        /// <summary>
        /// Handles the send log button click event to send diagnostic logs to support.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnSendLog_Clicked(object sender, EventArgs e)
        {
            if (await VAST.Common.License.SendLog("MAUI simple capture issue"))
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "The log has been sent successfully", "OK");
            }
            else
            {
                await MauiProgram.VastDisplayAlert(this, "Error", "Failed to send the log! Please grab it manually and send to support@vastreaming.net.", "OK");
            }
        }

        /// <summary>
        /// Handles audio level meter updates from the device monitor.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The audio level value (0.0 to 1.0).</param>
        private void DeviceMonitor_MeterUpdated(object sender, float e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { this.pbAudioLevel.Progress = e; } catch { }
            });
        }

        /// <summary>
        /// Starts the local preview session with captured video and audio.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// Waits for the video preview control to be ready before creating capture sources.
        /// Subscribes to error events to handle critical failures by stopping preview and showing alerts.
        /// </remarks>
        private async Task startPreview()
        {

            try
            {

                if (this.previewSession != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MauiProgram.VastDisplayAlert(this, "Warning", "Preview is already active", "OK");
                    });
                    return;
                }

                // Wait for video preview control to be ready
                do
                {
                    await Task.Delay(1000);
                }
                while (!this.videoPreview.IsReady);

                // Create preview session with capture sources
                this.previewSession = new VAST.Media.MediaSession();
                await this.createCaptureSources();
                if (this.activeVideoCaptureSource != null) this.previewSession.AddSource(this.activeVideoCaptureSource);
                if (this.activeAudioCaptureSource != null) this.previewSession.AddSource(this.activeAudioCaptureSource);

                // Handle critical errors by stopping preview
                this.previewSession.Error += (object sndr, Media.ErrorEventArgs eventArgs) =>
                {
                    if (eventArgs.IsCritical)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            string s = string.Format("Preview has been stopped:\n\n{0}", eventArgs.ErrorDescription);
                            this.stopPreview();
                            MauiProgram.VastDisplayAlert(this, "Error", $"Preview error: {eventArgs.ErrorDescription}", "OK");
                        });
                    }
                };

                this.previewSession.Start();
                this.updatePreview();

            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MauiProgram.VastDisplayAlert(this, "Error", $"Preview start exception: {ex}", "OK");
                });
                this.stopPreview();
            }


        }

        /// <summary>
        /// Updates the video preview control settings and connects the renderer.
        /// </summary>
        /// <remarks>
        /// - Smooth rendering strategy: smooth playback at the expense of latency (can be several seconds)
        /// - Low latency rendering strategy: minimal latency at the expense of smoothness
        /// </remarks>
        private void updatePreview()
        {

            // Configure preview rendering strategy based on user selection
            this.videoPreview.RenderingStrategy = (this.settingsPage.RenderingStrategyIndex == 0) ? Media.RenderingStrategy.LowLatency : Media.RenderingStrategy.Smooth;
            this.videoPreview.RendererType = this.videoRendererType;

            // Connect the video renderer to the capture source
            if (this.previewSession != null)
            {
                if (this.activeVideoCaptureSource != null)
                {
                    this.activeVideoCaptureSource.Renderer = this.videoPreview.Renderer;
                }
            }

        }

        /// <summary>
        /// Stops the local preview session and releases associated resources.
        /// </summary>
        private void stopPreview()
        {

            try
            {

                // Disconnect renderer before stopping
                if (this.activeVideoCaptureSource != null)
                {
                    this.activeVideoCaptureSource.Renderer = null;
                }

                if (this.previewSession != null)
                {
                    this.previewSession.Dispose();
                    this.previewSession = null;
                }

                this.cleanup();

            }
            catch
            {
            }

        }

        /// <summary>
        /// Starts streaming captured media to the server specified in the settings page.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// Creates a media sink based on the URI scheme (supports RTMP, RTSP, etc.).
        /// Subscribes to error events to handle critical failures by stopping streaming.
        /// </remarks>
        private async Task startStreaming()
        {

            try
            {

                // Prevent multiple concurrent streaming sessions
                if (this.streamingSession != null)
                {
                    await MauiProgram.VastDisplayAlert(this, "Warning", "Streaming is already active", "OK");
                    return;
                }

                // Create streaming session and attach capture sources
                this.streamingSession = new Media.MediaSession();
                await this.createCaptureSources();
                if (this.activeVideoCaptureSource != null) this.streamingSession.AddSource(this.activeVideoCaptureSource);
                if (this.activeAudioCaptureSource != null) this.streamingSession.AddSource(this.activeAudioCaptureSource);

                // Create the appropriate sink based on URI protocol
                VAST.Media.IMediaSink sink = VAST.Media.SinkFactory.Create(this.settingsPage.ServerUri);
                if (sink == null)
                {
                    Uri uri = new Uri(this.settingsPage.ServerUri);
                    throw new ArgumentException("Invalid URI or unsupported protocol");
                }

                sink.Uri = this.settingsPage.ServerUri;
                this.streamingSession.AddSink(sink);

                // Handle critical errors by stopping streaming and notifying user
                this.streamingSession.Error += (object sndr, Media.ErrorEventArgs eventArgs) =>
                {
                    if (eventArgs.IsCritical)
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await this.stopStreaming();
                            await MauiProgram.VastDisplayAlert(this, "Error", $"Streaming error: {eventArgs.ErrorDescription}", "OK");
                        });
                    }
                };

                this.streamingSession.Start();

            }
            catch (Exception ex)
            {
                await this.stopStreaming();
                await MauiProgram.VastDisplayAlert(this, "Error", $"Streaming start exception: {ex}", "OK");
            }

        }

        /// <summary>
        /// Stops streaming and releases associated resources.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// If preview is still active, switches output back to uncompressed to save encoding resources.
        /// </remarks>
        private async Task stopStreaming()
        {

            try
            {

                if (this.streamingSession != null)
                {
                    this.streamingSession.Dispose();
                    this.streamingSession = null;
                }

                this.cleanup();

                if (this.previewSession != null)
                {
                    // Stop output encoding to save resources when only previewing
                    await this.updateCaptureOutput();
                }

            }
            catch
            {
            }

        }

        /// <summary>
        /// Creates video and audio capture sources with configured encoding parameters.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// Sources are reference-counted and can be shared between preview and streaming sessions.
        /// The encoding framework is configured based on the selected option (Media Foundation, FFmpeg, Nvidia, or Builtin).
        /// Also starts the device monitor for audio level metering.
        /// </remarks>
        private async Task createCaptureSources()
        {

            // Create video capture source if not already created
            if (this.allowVideo && this.settingsPage.VideoDevice != null)
            {

                if (this.activeVideoCaptureSource == null)
                {

                    this.activeVideoCaptureSource = VAST.Media.SourceFactory.CreateVideoCapture(this.settingsPage.VideoDevice.DeviceId, this.settingsPage.VideoCaptureMode);
                    this.activeVideoCaptureSource.Rotation = this.videoRotation;
                    this.activeVideoCaptureSource.AddRef();

                    switch (this.settingsPage.EncodingFramework)
                    {
                        case "Media Foundation":
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.AllowHardwareAcceleration = this.settingsPage.HardwareAcceleration;
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.MediaFoundation;
                            break;
                        case "FFmpeg":
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.AllowHardwareAcceleration = this.settingsPage.HardwareAcceleration;
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                            break;
                        case "Nvidia":
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.AllowHardwareAcceleration = true; // Nvidia NVENC requires hardware acceleration, otherwise encoder will not be created
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.CUDA;
                            break;
                        case "Builtin":
                        default:
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.AllowHardwareAcceleration = this.settingsPage.HardwareAcceleration;
                            this.activeVideoCaptureSource.Parameters.VideoEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.Builtin;
                            break;
                    }

                }

            }

            // create audio capture source
            if (this.allowAudio && this.settingsPage.AudioDevice != null)
            {

                if (this.activeAudioCaptureSource == null)
                {

                    this.activeAudioCaptureSource = VAST.Media.SourceFactory.CreateAudioCapture(this.settingsPage.AudioDevice.DeviceId, this.settingsPage.AudioCaptureMode);
                    this.activeAudioCaptureSource.AddRef();

                    switch (this.settingsPage.EncodingFramework)
                    {
                        case "Media Foundation":
                            this.activeAudioCaptureSource.Parameters.AudioEncoderParameters.AllowHardwareAcceleration = false;
                            this.activeAudioCaptureSource.Parameters.AudioEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.MediaFoundation;
                            break;
                        case "FFmpeg":
                            this.activeAudioCaptureSource.Parameters.AudioEncoderParameters.AllowHardwareAcceleration = false;
                            this.activeAudioCaptureSource.Parameters.AudioEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                            break;
                        case "Builtin":
                        default:
                            this.activeAudioCaptureSource.Parameters.AudioEncoderParameters.AllowHardwareAcceleration = false;
                            this.activeAudioCaptureSource.Parameters.AudioEncoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.Builtin;
                            break;
                    }

                    try
                    {
                        // start mic meter
                        this.deviceMonitor = new VAST.Capture.DeviceMonitor(this.activeAudioCaptureSource);
                        this.deviceMonitor.MeterUpdated += DeviceMonitor_MeterUpdated;
                        this.deviceMonitor.Start();
                    }
                    catch
                    {
                    }

                }

            }

            await this.updateCaptureOutput();

        }

        /// <summary>
        /// Updates capture source output type based on whether streaming or just previewing.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <remarks>
        /// For streaming: configures H.264 video encoding with specified bitrate, profile, and level.
        /// For preview only: uses uncompressed output to save encoding resources.
        /// On Android, audio sample rate/channels are taken from capture mode due to lack of resampler.
        /// </remarks>
        private async Task updateCaptureOutput()
        {

            // Determine if output type needs to change
            bool needUpdate = false;
            if (this.streamingSession != null)
            {
                // Streaming active: need encoded output
                needUpdate = !this.isOutputEncoded;
            }
            else
            {
                // Preview only: don't need encoded output
                needUpdate = this.isOutputEncoded;
            }

            if (needUpdate)
            {

                // updating only the output, nothing else
                this.isOutputEncoded = (this.streamingSession != null);

                // Configure video output encoding
                if (this.allowVideo)
                {

                    VAST.Capture.IVideoCaptureSource2 captureSource = this.activeVideoCaptureSource;
                    if (captureSource != null)
                    {

                        var mt = new VAST.Common.MediaType { ContentType = VAST.Common.ContentType.Video };

                        if (this.isOutputEncoded)
                        {

                            // Configure H.264 encoding for streaming
                            // Validate and set output resolution
                            int outputWidth = int.Parse(this.settingsPage.VideoWidth);
                            if (outputWidth < 100 || outputWidth > 3840) throw new Exception("Please enter proper video width");
                            mt.Width = outputWidth;

                            int outputHeight = int.Parse(this.settingsPage.VideoHeight);
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

                            mt.Framerate = new VAST.Common.Rational(int.Parse(this.settingsPage.VideoFramerate));
                            mt.CodecId = VAST.Common.Codec.H264;

                            // Validate and set video bitrate (100kbps to 10Mbps)
                            int videoBitrate = int.Parse(this.settingsPage.VideoBitrate);
                            if (videoBitrate < 100000 || videoBitrate > 10000000) throw new Exception("Please enter proper video bitrate");
                            mt.Bitrate = videoBitrate;

                            // Validate and set keyframe interval (1 to 1000 frames)
                            int videoKeyframeInterval = int.Parse(this.settingsPage.VideoKeyframeInterval);
                            if (videoKeyframeInterval < 1 || videoKeyframeInterval > 1000) throw new Exception("Please enter proper video keyframe interval");
                            mt.Metadata.Add("KeyframeInterval", videoKeyframeInterval.ToString());

                            // Set H.264 profile: 66=Baseline, 77=Main, 100=High
                            if (this.settingsPage.VideoProfileIndex > 0)
                            {
                                switch (this.settingsPage.VideoProfileIndex)
                                {
                                    case 0: // auto
                                        break;
                                    case 1: // baseline
                                        mt.Metadata.Add("Profile", "66");
                                        break;
                                    case 2: // main
                                        mt.Metadata.Add("Profile", "77");
                                        break;
                                    case 3: // high
                                        mt.Metadata.Add("Profile", "100");
                                        break;
                                }
                            }

                            // Set H.264 level (e.g., 3.1, 4.0, 4.1, etc.)
                            if (this.settingsPage.VideoLevelIndex > 0)
                            {
                                mt.Metadata.Add("Level", ((int)(float.Parse(this.settingsPage.VideoLevelValue) * 10)).ToString());
                            }

                        }
                        else
                        {
                            // Use uncompressed output for preview only (lower CPU usage)
                            mt.CodecId = VAST.Common.Codec.Uncompressed;
                            mt.PixelFormat = VAST.Common.PixelFormat.BGRA;
                            if (this.settingsPage.VideoCaptureMode != null)
                            {
                                // Match capture mode parameters to prevent unnecessary conversions
                                mt.Width = this.settingsPage.VideoCaptureMode.Width;
                                mt.Height = this.settingsPage.VideoCaptureMode.Height;
                                mt.Framerate = new VAST.Common.Rational(this.settingsPage.VideoCaptureMode.Framerate);
                                mt.PixelFormat = this.settingsPage.VideoCaptureMode.PixelFormat;
                            }
                        }

                        // Apply encoding configuration to video capture source
                        await captureSource.SetDesiredOutputType(0, mt);

                    }

                }

                // Configure audio output encoding
                if (this.allowAudio)
                {

                    if (this.activeAudioCaptureSource != null)
                    {

                        var mt = new VAST.Common.MediaType { ContentType = VAST.Common.ContentType.Audio };

                        // Android lacks audio resampler, so use capture parameters directly
                        if (DeviceInfo.Current.Platform == DevicePlatform.Android && this.settingsPage.AudioCaptureMode != null)
                        {
                            mt.SampleRate = this.settingsPage.AudioCaptureMode.SampleRate;
                            mt.Channels = this.settingsPage.AudioCaptureMode.Channels;
                        }
                        else
                        {
                            // Other platforms support resampling to user-selected format
                            mt.SampleRate = int.Parse(this.settingsPage.AudioSampleRate);
                            mt.Channels = int.Parse(this.settingsPage.AudioChannels);
                        }

                        if (this.isOutputEncoded)
                        {
                            // Configure AAC encoding for streaming
                            mt.CodecId = VAST.Common.Codec.AAC;
                            int audioBitrate = int.Parse(this.settingsPage.AudioBitrate);
                            if (audioBitrate < 8000 || audioBitrate > 256000) throw new Exception("Please enter proper audio bitrate");
                            mt.Bitrate = audioBitrate;
                        }
                        else
                        {
                            // Use uncompressed PCM for preview only
                            mt.CodecId = VAST.Common.Codec.PCM;
                            mt.SampleFormat = VAST.Common.SampleFormat.S16;
                        }

                        // Apply encoding configuration to audio capture source
                        await this.activeAudioCaptureSource.SetDesiredOutputType(0, mt);

                    }

                }

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
        /// Recreates the video capture source after an orientation change.
        /// </summary>
        /// <remarks>
        /// Stops all active sessions, recreates them with the new rotation setting,
        /// and restarts any sessions that were previously active.
        /// </remarks>
        private async void recreateVideoCaptureSource()
        {

            try
            {

                bool restartPreview = this.previewSession != null;
                bool restartStreaming = this.streamingSession != null;

                // Stop all sessions
                await this.stopStreaming();
                this.stopPreview();

                // Restart sessions with new rotation
                if (restartPreview)
                {
                    await this.startPreview();
                }

                if (restartStreaming)
                {
                    await this.startStreaming();
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

        /// <summary>
        /// Cleans up capture sources and device monitor when no sessions are active.
        /// </summary>
        /// <remarks>
        /// Only releases resources if both preview and streaming sessions are stopped.
        /// Capture sources use reference counting, so this method decrements references.
        /// </remarks>
        private void cleanup()
        {

            // Don't cleanup if any session is still active
            if (this.previewSession != null || this.streamingSession != null)
            {
                return;
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

            // Dispose device monitor
            if (this.deviceMonitor != null)
            {
                this.deviceMonitor.MeterUpdated -= DeviceMonitor_MeterUpdated;
                this.deviceMonitor.Dispose();
                this.deviceMonitor = null;
            }

            this.isOutputEncoded = false;

        }

    }

}
