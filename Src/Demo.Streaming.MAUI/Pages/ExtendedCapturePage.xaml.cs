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

    using SkiaSharp;

    using System.Runtime.Versioning;

    /// <summary>
    /// Demo page for extended video capture with mixing functionality.
    /// Demonstrates advanced capture features including video mixing from multiple sources
    /// (camera, screen, files, streams), overlay text and logo, brightness/contrast adjustments,
    /// and manual frame pushing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class ExtendedCapturePage : ContentPage
    {

        /// <summary>
        /// The default server URI for streaming (empty by default).
        /// </summary>
        private string defaultUri = "";

        /// <summary>
        /// The mixing source that combines multiple video/audio sources.
        /// </summary>
        private VAST.Image.Mixing.MixingSource mixingSource = null;

        /// <summary>
        /// The currently active video capture source from camera.
        /// </summary>
        private VAST.Capture.IVideoCaptureSource2 activeVideoCaptureSource = null;

        /// <summary>
        /// The currently active audio capture source from microphone.
        /// </summary>
        private VAST.Capture.IAudioCaptureSource2 activeAudioCaptureSource = null;

        /// <summary>
        /// The currently active screen capture source (Windows only).
        /// </summary>
        private VAST.Capture.IScreenCaptureSource activeScreenCaptureSource = null;

        /// <summary>
        /// The virtual network source for manual frame pushing.
        /// </summary>
        private VAST.Network.VirtualNetworkSource userSource = null;

        /// <summary>
        /// Index of the video capture source in the mixing source.
        /// </summary>
        private int videoCaptureSourceIndex = -1;

        /// <summary>
        /// Index of the audio capture source in the mixing source.
        /// </summary>
        private int audioCaptureSourceIndex = -1;

        /// <summary>
        /// Index of the screen capture source in the mixing source.
        /// </summary>
        private int screenCaptureSourceIndex = -1;

        /// <summary>
        /// Index of the text overlay source in the mixing source.
        /// </summary>
        private int textOverlaySourceIndex = -1;

        /// <summary>
        /// Index of the logo overlay source in the mixing source.
        /// </summary>
        private int logoOverlaySourceIndex = -1;

        /// <summary>
        /// Index of the user (manual push) source in the mixing source.
        /// </summary>
        private int userSourceIndex = -1;

        /// <summary>
        /// Index of the video file source in the mixing source.
        /// </summary>
        private int videoFileSourceIndex = -1;

        /// <summary>
        /// Index of the streaming source in the mixing source.
        /// </summary>
        private int streamingSourceIndex = -1;

        /// <summary>
        /// The media session used for local preview.
        /// </summary>
        private VAST.Media.MediaSession previewSession = null;

        /// <summary>
        /// The media session used for streaming to a server.
        /// </summary>
        private VAST.Media.MediaSession streamingSession = null;

        /// <summary>
        /// Flag indicating that a streaming session is being initialized and waiting for encoded output.
        /// Set to true when <see cref="startStreaming"/> begins, cleared when the first encoded stream arrives
        /// in <see cref="MixingSource_NewStream"/> or when streaming is stopped.
        /// </summary>
        private bool startingStreamingSession = false;

        /// <summary>
        /// Flag indicating whether the text update task is running.
        /// </summary>
        private volatile bool textUpdateTaskActive = false;

        /// <summary>
        /// Background task for updating text overlay content.
        /// </summary>
        private Task textUpdateTask = null;

        /// <summary>
        /// Flag indicating whether the manual frame pushing task is running.
        /// </summary>
        private volatile bool manualPushingRunning = false;

        /// <summary>
        /// Background task for manual frame pushing.
        /// </summary>
        private Task manualPusher = null;

        /// <summary>
        /// The settings page instance. A new instance is created each time Settings is opened to avoid
        /// Android picker state restoration issues, with state copied from the previous instance.
        /// </summary>
        private ExtendedCaptureSettingsPage settingsPage;

        /// <summary>
        /// Guard flag to prevent <see cref="ContentPage_NavigatingFrom"/> from disposing resources
        /// when navigating to the modal settings page. Set to true before pushing the settings modal
        /// and cleared immediately after the modal returns.
        /// </summary>
        private bool isOpeningSettings = false;

        /// <summary>
        /// The currently selected screen/display for screen capture.
        /// </summary>
        private VAST.Capture.DisplayDescriptor screen = null;

        /// <summary>
        /// The video renderer type to use for preview.
        /// </summary>
        private VAST.Media.VideoRendererType videoRendererType = VAST.Media.VideoRendererType.Best;

        /// <summary>
        /// The output video width in pixels.
        /// </summary>
        private int outputWidth = 1280;

        /// <summary>
        /// The output video height in pixels.
        /// </summary>
        private int outputHeight = 720;

        /// <summary>
        /// The output video frame rate.
        /// </summary>
        private VAST.Common.Rational outputFramerate = new VAST.Common.Rational(30, 1);

        /// <summary>
        /// Indicates whether the output is currently encoded (for streaming) or uncompressed (for preview).
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
        /// Creates a new instance of <see cref="ExtendedCapturePage"/>.
        /// </summary>
        /// <remarks>
        /// Initializes picker controls with default values, configures encoding framework options
        /// based on platform (Windows shows more options including screen capture).
        /// </remarks>
        public ExtendedCapturePage()
        {

            InitializeComponent();

            this.settingsPage = new ExtendedCaptureSettingsPage();
            this.settingsPage.DefaultUri = this.defaultUri;
            this.layoutScreenSource.IsVisible = DeviceInfo.Current.Platform == DevicePlatform.WinUI;

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

            // Rotation changed - need to restart capture/preview with new rotation
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
                await this.settingsPage.EnumerateDevices();
                this.enumerateScreens();
            });
        }

        /// <summary>
        /// Handles the page navigating from event by stopping all active sessions and releasing resources.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {

            if (this.isOpeningSettings) return;

            // Dispose the audio level monitor
            if (this.deviceMonitor != null)
            {
                this.deviceMonitor.Dispose();
                this.deviceMonitor = null;
            }

            // Stop all active sessions
            this.stopStreaming();
            this.stopPreview();

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
        /// Handles the start preview button click to begin local video preview.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnStartPreview_Clicked(object sender, EventArgs e)
        {
            await this.startPreview();
        }

        /// <summary>
        /// Handles the stop preview button click to end local video preview.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStopPreview_Clicked(object sender, EventArgs e)
        {
            this.stopPreview();
        }

        /// <summary>
        /// Handles the start streaming button click to begin streaming to the server.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStartStreaming_Clicked(object sender, EventArgs e)
        {
            this.startStreaming();
        }

        /// <summary>
        /// Handles the stop streaming button click to end server streaming.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStopStreaming_Clicked(object sender, EventArgs e)
        {
            this.stopStreaming();
        }

        /// <summary>
        /// Handles the send log button click to transmit diagnostic logs to support.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnSendLog_Clicked(object sender, EventArgs e)
        {
            if (await VAST.Common.License.SendLog("MAUI extended capture issue"))
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "The log has been sent successfully", "OK");
            }
            else
            {
                await MauiProgram.VastDisplayAlert(this, "Error", "Failed to send the log! Please grab it manually and send to support@vastreaming.net.", "OK");
            }
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
        /// <see cref="ExtendedCaptureSettingsPage.InitializeFrom"/>.
        /// After the modal is dismissed, <see cref="applySettingsToPreview"/> applies
        /// any rendering changes that affect the live preview.
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
                var newSettingsPage = new ExtendedCaptureSettingsPage();
                newSettingsPage.InitializeFrom(this.settingsPage);
                this.settingsPage = newSettingsPage;

                // Guard against NavigatingFrom disposing resources during modal navigation
                this.isOpeningSettings = true;
                await Navigation.PushModalAsync(new NavigationPage(this.settingsPage));
                this.isOpeningSettings = false;

                // Apply any rendering settings that changed
                this.applySettingsToPreview();
            }
        }

        /// <summary>
        /// Handles the Overlays button click to toggle the overlay/source controls panel.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Toggles visibility of the semi-transparent overlay panel that contains source enable/disable
        /// checkboxes (camera, screen, time, logo, manual, video file, streaming). These controls
        /// need to be accessible during live preview, unlike the settings page controls which require
        /// stopping the session.
        /// </remarks>
        private void btnOverlays_Clicked(object sender, EventArgs e)
        {
            this.overlayPanel.IsVisible = !this.overlayPanel.IsVisible;
            this.panelDismissLayer.IsVisible = this.overlayPanel.IsVisible;
        }

        /// <summary>
        /// Handles taps on the transparent dismiss layer to close the overlay panel.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The tap event arguments.</param>
        /// <remarks>
        /// The dismiss layer is a full-screen transparent BoxView that sits between the video preview
        /// and the overlay panel in the z-order. Tapping anywhere outside the panel closes it.
        /// </remarks>
        private void dismissPanel_Tapped(object sender, TappedEventArgs e)
        {
            this.overlayPanel.IsVisible = false;
            this.panelDismissLayer.IsVisible = false;
        }

        /// <summary>
        /// Applies rendering settings from the settings page to the live preview.
        /// Called after the settings modal is dismissed to reflect any changes.
        /// </summary>
        /// <remarks>
        /// Disconnects existing renderers before switching the preview source type.
        /// Two preview modes are supported:
        /// <list type="bullet">
        /// <item><description>Capture (index 0): shows raw camera output with minimal latency.</description></item>
        /// <item><description>Mixing (index 1): shows composited output with all layers, overlays, and effects.</description></item>
        /// </list>
        /// Also refreshes the scene configuration (brightness, contrast, chroma key, etc.)
        /// if a mixing source is active.
        /// </remarks>
        private void applySettingsToPreview()
        {

            // Disconnect current renderers before switching
            if (this.activeVideoCaptureSource != null)
            {
                this.activeVideoCaptureSource.Renderer = null;
            }

            if (this.mixingSource != null)
            {
                this.mixingSource.Renderer = null;
            }

            // Set the preview type based on rendering source selection
            switch (this.settingsPage.RenderingSourceIndex)
            {
                case 0:
                    this.videoPreview.PreviewType = VAST.Controls.VideoPreviewType.Capture;
                    break;
                case 1:
                    this.videoPreview.PreviewType = VAST.Controls.VideoPreviewType.Mixing;
                    break;
            }

            // Reconnect the appropriate renderer
            this.updatePreview();

            // Refresh scene effects if mixing is active
            if (this.mixingSource != null)
            {
                this.updateScene(null);
            }

        }

        /// <summary>
        /// Handles screen/display selection changes for screen capture.
        /// Updates the screen capture source when a different display is selected.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerScreen_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (checkBoxScreenSource.IsChecked)
                {
                    this.screen = VAST.Capture.DisplayHelper.EnumerateDisplays()[this.pickerScreen.SelectedIndex];
                    this.updateSources(null);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Handles camera source checkbox changes to toggle the camera video input in the mixing source.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxCameraSource_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            this.updateSources(null);
        }

        /// <summary>
        /// Handles brightness slider value changes to update the video brightness effect in real time.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The value changed event arguments.</param>
        private void sliderBrightness_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            this.updateScene(null);
        }

        /// <summary>
        /// Handles contrast slider value changes to update the video contrast effect in real time.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The value changed event arguments.</param>
        private void sliderContrast_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            this.updateScene(null);
        }

        /// <summary>
        /// Handles chroma key threshold slider value changes to adjust the color keying sensitivity.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The value changed event arguments.</param>
        /// <remarks>
        /// Higher threshold values remove a wider range of colors around the chroma key color,
        /// producing a more aggressive key effect.
        /// </remarks>
        private void sliderChromaKeyThreshold_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            this.updateScene(null);
        }

        /// <summary>
        /// Handles chroma key smoothing slider value changes to adjust the edge blending of the key effect.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The value changed event arguments.</param>
        /// <remarks>
        /// Higher smoothing values produce softer edges between keyed and non-keyed areas,
        /// reducing hard edges and color fringing artifacts.
        /// </remarks>
        private void sliderChromaKeySmoothing_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            this.updateScene(null);
        }

        /// <summary>
        /// Handles time overlay checkbox changes to toggle the timestamp text overlay.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxOverlayTime_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            this.updateSources(null);
        }

        /// <summary>
        /// Handles logo overlay checkbox changes to toggle the watermark logo overlay.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxOverlayLogo_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            this.updateSources(null);
        }

        /// <summary>
        /// Handles manual source checkbox changes to toggle programmatic frame pushing.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxManualSource_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            this.updateSources(null);
        }

        /// <summary>
        /// Handles video file source checkbox changes to toggle file-based video mixing.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxVideoFileSource_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            this.updateSources(null);
        }

        /// <summary>
        /// Handles streaming source checkbox changes to toggle network stream mixing.
        /// Requires a valid streaming source URI to be enabled.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxStreamingSource_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            // Don't enable streaming source without a valid URI
            if (this.checkBoxStreamingSource.IsChecked)
            {
                if (string.IsNullOrEmpty(this.tboxStreamingSource.Text)) return;
            }
            this.updateSources(null);
        }

        /// <summary>
        /// Handles screen capture source checkbox changes to toggle screen/display capture.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The checkbox event arguments.</param>
        private void checkBoxScreenSource_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            try
            {
                if (checkBoxScreenSource.IsChecked)
                {
                    this.screen = VAST.Capture.DisplayHelper.EnumerateDisplays()[this.pickerScreen.SelectedIndex];
                }
                else
                {
                    this.screen = null;
                }
            }
            catch
            {
            }
            this.updateSources(null);
        }

        /// <summary>
        /// Handles audio level meter updates from the device monitor.
        /// Updates the progress bar to display current microphone input level.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The normalized audio level (0.0 to 1.0).</param>
        private void DeviceMonitor_MeterUpdated(object sender, float e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { this.pbAudioLevel.Progress = e; } catch { }
            });
        }

        /// <summary>
        /// Enumerates available displays/screens and populates the screen picker control.
        /// </summary>
        /// <remarks>
        /// Called from a background thread during page initialization. Each screen is displayed
        /// with its index and bounding rectangle coordinates for identification.
        /// This method is separate from the settings page device enumeration because the screen
        /// picker remains on the main page as part of the overlay panel for quick access.
        /// </remarks>
        private void enumerateScreens()
        {

            // Build display list with index and bounding rectangle
            var screens = VAST.Capture.DisplayHelper.EnumerateDisplays();
            List<string> ss = new List<string>();
            for (int i = 0; i < screens.Count; ++i)
            {
                ss.Add(string.Format("#{0} at [{1}, {2}, {3}, {4}]", i, screens[i].Location.Left, screens[i].Location.Top, screens[i].Location.Right, screens[i].Location.Bottom));
            }

            // Update picker on UI thread and select the first screen
            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.pickerScreen.ItemsSource = ss;
                this.pickerScreen.ItemsSource = this.pickerScreen.GetItemsAsArray();
                this.pickerScreen.SelectedIndex = 0;
            });

        }

        /// <summary>
        /// Starts the local video preview session.
        /// Creates the mixing source, initializes capture devices, and begins rendering to the preview control.
        /// </summary>
        /// <returns>A task representing the asynchronous preview start operation.</returns>
        /// <remarks>
        /// Waits for the video preview control to become ready before starting.
        /// Subscribes to error events to handle critical failures gracefully.
        /// </remarks>
        private async Task startPreview()
        {

            try
            {

                // Prevent multiple concurrent preview sessions
                if (this.previewSession != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MauiProgram.VastDisplayAlert(this, "Warning", "Preview is already active", "OK");
                    });
                    return;
                }

                // Wait for the preview control to initialize
                do
                {
                    await Task.Delay(1000);
                }
                while (!this.videoPreview.IsReady);

                // Create and configure the preview session
                this.previewSession = new VAST.Media.MediaSession();
                this.createMixingSource();
                this.previewSession.AddSource(this.mixingSource);

                // Handle critical errors by stopping preview and notifying user
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

                // Start the session and update UI
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
        /// Updates the preview renderer configuration based on current settings.
        /// Applies the selected rendering strategy and connects the appropriate renderer.
        /// </summary>
        /// <remarks>
        /// In Smooth mode, rendering prioritizes visual quality at the cost of higher latency (up to several seconds).
        /// In LowLatency mode, rendering minimizes delay at the cost of occasional stuttering.
        /// </remarks>
        private void updatePreview()
        {

            // Configure rendering strategy: Smooth for quality, LowLatency for responsiveness
            this.videoPreview.RenderingStrategy = (this.settingsPage.RenderingStrategyIndex == 0) ? Media.RenderingStrategy.LowLatency : Media.RenderingStrategy.Smooth;
            this.videoPreview.RendererType = this.videoRendererType;

            // Connect the appropriate source to the preview renderer
            if (this.previewSession != null)
            {
                if (this.settingsPage.RenderingSourceIndex == 0)
                {
                    // Direct camera preview (lower latency, no effects)
                    if (this.activeVideoCaptureSource != null)
                    {
                        this.activeVideoCaptureSource.Renderer = this.videoPreview.Renderer;
                    }
                }
                else
                {
                    // Mixed output preview (all layers and effects visible)
                    this.mixingSource.Renderer = this.videoPreview.Renderer;
                }
            }

        }

        /// <summary>
        /// Stops the local video preview session and releases associated resources.
        /// </summary>
        /// <remarks>
        /// Disconnects renderers from capture and mixing sources before disposal.
        /// Performs cleanup if no other sessions are using the shared resources.
        /// </remarks>
        private void stopPreview()
        {

            try
            {

                // Disconnect renderers to stop video output
                if (this.activeVideoCaptureSource != null)
                {
                    // stop direct camera renderer
                    this.activeVideoCaptureSource.Renderer = null;
                }

                if (this.mixingSource != null)
                {
                    // stop mixed output renderer
                    this.mixingSource.Renderer = null;
                }

                // Dispose the preview session
                if (this.previewSession != null)
                {
                    this.previewSession.Dispose();
                    this.previewSession = null;
                }

                // Release shared resources if no longer needed
                this.cleanup();

            }
            catch
            {
            }

        }

        /// <summary>
        /// Starts streaming to the server specified in the URI text box.
        /// Creates a media sink for the target protocol and begins transmitting encoded media.
        /// </summary>
        /// <remarks>
        /// Supports various streaming protocols (RTMP, RTSP, SRT, etc.) based on the URI scheme.
        /// Enables output encoding when streaming starts to compress the media for transmission.
        /// </remarks>
        private void startStreaming()
        {

            try
            {

                // Prevent multiple concurrent streaming sessions
                if (this.streamingSession != null)
                {
                    MauiProgram.VastDisplayAlert(this, "Warning", "Streaming is already active", "OK");
                    return;
                }

                // Create and configure the streaming session
                this.streamingSession = new VAST.Media.MediaSession();
                this.startingStreamingSession = true;
                this.createMixingSource();
                this.streamingSession.AddSource(this.mixingSource);

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
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            this.stopStreaming();
                            MauiProgram.VastDisplayAlert(this, "Error", $"Streaming error: {eventArgs.ErrorDescription}", "OK");
                        });
                    }
                };

                // The session will be started in MixingSource_NewStream event handler,
                // once we get a new descriptor with encoded output.

            }
            catch (Exception ex)
            {
                MauiProgram.VastDisplayAlert(this, "Error", $"Streaming start exception: {ex}", "OK");
                this.stopStreaming();
            }

        }

        /// <summary>
        /// Stops the active streaming session and releases associated resources.
        /// </summary>
        /// <remarks>
        /// If preview is still active, switches the mixing source back to uncompressed output
        /// to reduce CPU/GPU usage when streaming is not needed.
        /// </remarks>
        private void stopStreaming()
        {

            try
            {

                // Dispose the streaming session
                if (this.streamingSession != null)
                {
                    this.streamingSession.Dispose();
                    this.streamingSession = null;
                }

                this.startingStreamingSession = false;

                // Release shared resources if no longer needed
                this.cleanup();

                // If preview is still active, disable encoding to save resources
                if (this.mixingSource != null && this.previewSession != null)
                {
                    // stop output encoding to save resources
                    this.updateMixingSourceOutput();
                }

            }
            catch
            {
            }

        }


        /// <summary>
        /// Creates and configures the mixing source that combines multiple video and audio sources.
        /// If a mixing source already exists, updates its output configuration instead.
        /// </summary>
        /// <remarks>
        /// Configures encoder/decoder frameworks based on platform and user selection:
        /// - Media Foundation: Windows native encoder, good balance of quality and performance
        /// - FFmpeg: Cross-platform software encoder, highly configurable
        /// - Nvidia: GPU-accelerated encoding using CUDA (Windows only)
        /// - Builtin: Platform default encoder for maximum compatibility
        ///
        /// The mixing source handles compositing multiple video layers, audio mixing,
        /// text/logo overlays, and optional encoding for streaming output.
        /// </remarks>
        private void createMixingSource()
        {

            // If mixing source exists, just update its output configuration
            if (this.mixingSource != null)
            {
                this.updateMixingSourceOutput();
                return;
            }

            // Determine encoder framework based on user selection
            VAST.Common.MediaFramework videoFramework = Common.MediaFramework.Builtin;
            VAST.Common.MediaFramework audioFramework = Common.MediaFramework.Builtin;
            bool allowHardwareAcceleration = this.settingsPage.HardwareAcceleration;
            switch (this.settingsPage.EncodingFramework)
            {
                case "Media Foundation":
                    videoFramework = VAST.Common.MediaFramework.MediaFoundation;
                    audioFramework = VAST.Common.MediaFramework.MediaFoundation;
                    break;
                case "FFmpeg":
                    videoFramework = VAST.Common.MediaFramework.FFmpeg;
                    audioFramework = VAST.Common.MediaFramework.FFmpeg;
                    break;
                case "Nvidia":
                    // Nvidia CUDA for video, Media Foundation for audio
                    videoFramework = VAST.Common.MediaFramework.CUDA;
                    audioFramework = VAST.Common.MediaFramework.MediaFoundation;
                    allowHardwareAcceleration = true;
                    break;
                case "Builtin":
                default:
                    videoFramework = VAST.Common.MediaFramework.Builtin;
                    audioFramework = VAST.Common.MediaFramework.Builtin;
                    break;
            }

            // Determine output format: encoded for streaming, uncompressed for preview only
            this.isOutputEncoded = (this.streamingSession != null);
            bool allowVideo = this.settingsPage.OutputVideoEnabled;
            bool allowAudio = this.settingsPage.OutputAudioEnabled;

            // Create the mixing source with reference counting for shared use
            this.mixingSource = new VAST.Image.Mixing.MixingSource();
            this.mixingSource.NewStream += MixingSource_NewStream;
            this.mixingSource.AddRef();

            // Configure decoder and encoder parameters for all media types
            this.mixingSource.Parameters.VideoDecoderParameters.PreferredMediaFramework = videoFramework;
            this.mixingSource.Parameters.VideoDecoderParameters.AllowHardwareAcceleration = allowHardwareAcceleration;
            this.mixingSource.Parameters.AudioDecoderParameters.PreferredMediaFramework = audioFramework;
            this.mixingSource.Parameters.AudioDecoderParameters.AllowHardwareAcceleration = false;
            this.mixingSource.Parameters.VideoEncoderParameters.PreferredMediaFramework = videoFramework;
            this.mixingSource.Parameters.VideoEncoderParameters.AllowHardwareAcceleration = allowHardwareAcceleration;
            this.mixingSource.Parameters.AudioEncoderParameters.PreferredMediaFramework = audioFramework;
            this.mixingSource.Parameters.AudioEncoderParameters.AllowHardwareAcceleration = false;

            // Configure the mixing descriptor with processing options
            VAST.Image.Mixing.Descriptor descriptor = new VAST.Image.Mixing.Descriptor
            {
                AllowVideoProcessing = true,
                AllowAbsoluteTimestamps = true,
                Processing = new VAST.Image.Mixing.Processing
                {
                    VideoProcessing = new VAST.Image.Mixing.VideoProcessing
                    {
                        Discard = !allowVideo,
                    },
                    AudioProcessing = new VAST.Image.Mixing.AudioProcessing
                    {
                        Discard = !allowAudio,
                    }
                }
            };

            int trackIndex = 0;

            // Configure video output track if video is enabled
            if (allowVideo)
            {

                var track = new VAST.Image.Mixing.VideoTrack
                {
                    Index = trackIndex,
                };

                try
                {
                    this.updateVideoTrack(track);
                }
                catch (Exception ex)
                {
                    MauiProgram.VastDisplayAlert(this, "Error", ex.Message, "OK");
                    return;
                }

                descriptor.Processing.VideoProcessing.Tracks = new List<VAST.Image.Mixing.VideoTrack>
                (
                    new VAST.Image.Mixing.VideoTrack[] { track }
                );

                trackIndex++;

            }

            // Configure audio output track if audio is enabled
            if (allowAudio)
            {

                var track = new VAST.Image.Mixing.AudioTrack
                {
                    Index = trackIndex
                };

                try
                {
                    this.updateAudioTrack(track);
                }
                catch (Exception ex)
                {
                    MauiProgram.VastDisplayAlert(this, "Error", ex.Message, "OK");
                    return;
                }

                descriptor.Processing.AudioProcessing.Tracks = new List<VAST.Image.Mixing.AudioTrack>
                (
                    new VAST.Image.Mixing.AudioTrack[] { track }
                );

                trackIndex++;

            }

            // Apply sources and scene configuration, then start preview
            this.updateSources(descriptor);
            this.updatePreview();

        }

        /// <summary>
        /// Handles new stream events from the mixing source to detect when encoded output is ready.
        /// </summary>
        /// <param name="sender">The event sender (the mixing source).</param>
        /// <param name="e">The new stream event arguments containing stream index and media type info.</param>
        /// <remarks>
        /// When starting a streaming session, the mixing source must first produce encoded (compressed)
        /// output before the streaming session can begin transmitting. This handler watches for the
        /// first video stream (index 0) with a non-uncompressed codec, then starts the streaming session.
        /// This two-phase startup ensures the encoder pipeline is initialized before the network sink
        /// begins consuming frames.
        /// </remarks>
        private void MixingSource_NewStream(object sender, VAST.Media.NewStreamEventArgs e)
        {
            if (this.startingStreamingSession && this.streamingSession != null)
            {
                if (e.StreamIndex == 0 && e.MediaType.CodecId != VAST.Common.Codec.Uncompressed)
                {
                    // Encoded output is available, safe to start the streaming session
                    this.startingStreamingSession = false;
                    this.streamingSession.Start();
                }
            }
        }

        /// <summary>
        /// Updates the video track configuration from UI control values.
        /// </summary>
        /// <param name="track">The video track to configure.</param>
        /// <exception cref="Exception">Thrown when video parameters are out of valid range.</exception>
        /// <remarks>
        /// When streaming is active (isOutputEncoded=true), configures H.264 encoding with
        /// bitrate, keyframe interval, profile, and level from UI controls.
        /// When only previewing, uses uncompressed output for lower CPU usage.
        /// Supported resolution range: 100x100 to 3840x2160 (4K).
        /// </remarks>
        private void updateVideoTrack(VAST.Image.Mixing.VideoTrack track)
        {

            // Parse and validate video dimensions
            this.outputWidth = int.Parse(this.settingsPage.VideoWidth);
            if (this.outputWidth < 100 || this.outputWidth > 3840) throw new Exception("Please enter proper video width");
            track.Width = this.outputWidth;

            this.outputHeight = int.Parse(this.settingsPage.VideoHeight);
            if (this.outputHeight < 100 || this.outputHeight > 2160) throw new Exception("Please enter proper video height");
            track.Height = this.outputHeight;

            this.outputFramerate = new VAST.Common.Rational(int.Parse(this.settingsPage.VideoFramerate));
            track.Framerate = this.outputFramerate;

            if (this.isOutputEncoded)
            {
                // Configure H.264 encoding for streaming
                track.Codec = VAST.Common.Codec.H264;

                // Parse and validate bitrate (100kbps to 10Mbps)
                int videoBitrate = int.Parse(this.settingsPage.VideoBitrate);
                if (videoBitrate < 100000 || videoBitrate > 10000000) throw new Exception("Please enter proper video bitrate");
                track.Bitrate = videoBitrate;

                // Parse and validate keyframe interval (1 to 1000 frames)
                int videoKeyframeInterval = int.Parse(this.settingsPage.VideoKeyframeInterval);
                if (videoKeyframeInterval < 1 || videoKeyframeInterval > 1000) throw new Exception("Please enter proper video keyframe interval");
                track.KeyframeInterval = videoKeyframeInterval;

                // Set H.264 profile: 66=Baseline, 77=Main, 100=High
                if (this.settingsPage.VideoProfileIndex > 0)
                {
                    switch (this.settingsPage.VideoProfileIndex)
                    {
                        case 0: // auto
                            break;
                        case 1: // baseline
                            track.Profile = 66;
                            break;
                        case 2: // main
                            track.Profile = 77;
                            break;
                        case 3: // high
                            track.Profile = 100;
                            break;
                    }
                }

                // Set H.264 level (e.g., 3.1, 4.0, 4.1, etc.)
                if (this.settingsPage.VideoLevelIndex > 0)
                {
                    double f = double.Parse(this.settingsPage.VideoLevelValue);
                    track.Level = (int)(f * 10);
                }

            }
            else
            {
                // Use uncompressed output for preview only (lower CPU usage)
                track.Codec = VAST.Common.Codec.Uncompressed;
                track.PixelFormat = VAST.Common.PixelFormat.None;
            }

        }

        /// <summary>
        /// Updates the audio track configuration from UI control values.
        /// </summary>
        /// <param name="track">The audio track to configure.</param>
        /// <exception cref="Exception">Thrown when audio bitrate is out of valid range.</exception>
        /// <remarks>
        /// On Android, uses the capture device's native sample rate and channels
        /// because audio resampling is not available on that platform.
        /// When streaming is active, encodes audio as AAC.
        /// When only previewing, outputs uncompressed PCM audio.
        /// </remarks>
        private void updateAudioTrack(VAST.Image.Mixing.AudioTrack track)
        {

            // Android lacks audio resampler, so use capture parameters directly
            if (DeviceInfo.Current.Platform == DevicePlatform.Android && this.settingsPage.AudioCaptureMode != null)
            {
                // there is no audio resampler on Android, using capture mode parameters
                track.SampleRate = this.settingsPage.AudioCaptureMode.SampleRate;
                track.Channels = this.settingsPage.AudioCaptureMode.Channels;
            }
            else
            {
                // Other platforms support resampling to user-selected format
                track.SampleRate = int.Parse(this.settingsPage.AudioSampleRate);
                track.Channels = int.Parse(this.settingsPage.AudioChannels);
            }

            if (this.isOutputEncoded)
            {
                // Configure AAC encoding for streaming
                track.Codec = VAST.Common.Codec.AAC;
                int audioBitrate = int.Parse(this.settingsPage.AudioBitrate);
                if (audioBitrate < 8000 || audioBitrate > 256000) throw new Exception("Please enter proper audio bitrate");
                track.Bitrate = audioBitrate;
            }
            else
            {
                // Use uncompressed PCM for preview only
                track.Codec = VAST.Common.Codec.PCM;
                track.SampleFormat = VAST.Common.SampleFormat.Unknown;
            }

        }

        /// <summary>
        /// Updates the mixing source output encoding state based on current session requirements.
        /// Switches between encoded output (for streaming) and uncompressed output (for preview only).
        /// </summary>
        /// <remarks>
        /// When streaming starts, enables H.264/AAC encoding.
        /// When streaming stops but preview remains active, disables encoding to reduce CPU/GPU load.
        /// Only updates if the encoding state actually needs to change.
        /// </remarks>
        private void updateMixingSourceOutput()
        {

            // Determine if encoding state needs to change
            bool needUpdate = false;
            if (this.streamingSession != null)
            {
                // Streaming active: need encoded output
                needUpdate = !this.isOutputEncoded;
            }
            else
            {
                // Only preview active: don't need encoded output
                needUpdate = this.isOutputEncoded;
            }

            if (needUpdate)
            {

                // Update only the output encoding, preserving sources and scene
                this.isOutputEncoded = (this.streamingSession != null);
                bool allowVideo = this.settingsPage.OutputVideoEnabled;
                bool allowAudio = this.settingsPage.OutputAudioEnabled;

                VAST.Image.Mixing.Descriptor descriptor = new VAST.Image.Mixing.Descriptor
                {
                    Processing = new VAST.Image.Mixing.Processing
                    {
                        VideoProcessing = new VAST.Image.Mixing.VideoProcessing
                        {
                            Discard = !allowVideo,
                        },
                        AudioProcessing = new VAST.Image.Mixing.AudioProcessing
                        {
                            Discard = !allowAudio,
                        }
                    }
                };

                int trackIndex = 0;

                // Reconfigure video track with new encoding settings
                if (allowVideo)
                {

                    var track = new VAST.Image.Mixing.VideoTrack
                    {
                        Index = trackIndex,
                    };

                    try
                    {
                        this.updateVideoTrack(track);
                    }
                    catch (Exception ex)
                    {
                        MauiProgram.VastDisplayAlert(this, "Error", ex.Message, "OK");
                        return;
                    }

                    descriptor.Processing.VideoProcessing.Tracks = new List<VAST.Image.Mixing.VideoTrack>
                    (
                        new VAST.Image.Mixing.VideoTrack[] { track }
                    );

                    trackIndex++;

                }

                // Reconfigure audio track with new encoding settings
                if (allowAudio)
                {

                    var track = new VAST.Image.Mixing.AudioTrack
                    {
                        Index = trackIndex
                    };

                    try
                    {
                        this.updateAudioTrack(track);
                    }
                    catch (Exception ex)
                    {
                        MauiProgram.VastDisplayAlert(this, "Error", ex.Message, "OK");
                        return;
                    }

                    descriptor.Processing.AudioProcessing.Tracks = new List<VAST.Image.Mixing.AudioTrack>
                    (
                        new VAST.Image.Mixing.AudioTrack[] { track }
                    );

                }

                // Apply the updated configuration
                this.mixingSource.Update(descriptor);

            }

        }

        /// <summary>
        /// Updates the list of media sources in the mixing configuration.
        /// Creates, updates, or removes capture sources based on current UI settings.
        /// </summary>
        /// <param name="descriptor">
        /// The mixing descriptor to update, or null to create a new descriptor for source-only updates.
        /// </param>
        /// <remarks>
        /// Thread-safe via locking to prevent race conditions with background update tasks.
        /// Manages the lifecycle of multiple source types:
        /// - Video capture (camera)
        /// - Audio capture (microphone)
        /// - Screen capture (display)
        /// - Video file playback
        /// - Network stream input
        /// - Manual frame pushing (user source)
        /// - Text overlay (dynamic timestamp)
        /// - Logo overlay (static watermark)
        ///
        /// Each source is assigned an index for layer composition in updateScene().
        /// </remarks>
        private void updateSources(VAST.Image.Mixing.Descriptor descriptor)
        {

            // Lock to prevent race condition between UI calls and text update task calls
            lock (this)
            {

                if (this.mixingSource == null) return;

                // Create new descriptor if this is a source-only update
                if (descriptor == null)
                {
                    // this is a source update
                    descriptor = new VAST.Image.Mixing.Descriptor();
                }

                // Reset source list and indices
                descriptor.Sources = new List<VAST.Image.Mixing.Source>();

                this.videoCaptureSourceIndex = -1;
                this.audioCaptureSourceIndex = -1;
                this.screenCaptureSourceIndex = -1;
                this.textOverlaySourceIndex = -1;
                this.logoOverlaySourceIndex = -1;
                this.userSourceIndex = -1;
                this.videoFileSourceIndex = -1;
                this.streamingSourceIndex = -1;

                // Dispose and recreate device monitor for audio level metering
                if (this.deviceMonitor != null)
                {
                    this.deviceMonitor.MeterUpdated -= DeviceMonitor_MeterUpdated;
                    this.deviceMonitor.Dispose();
                    this.deviceMonitor = null;
                }

                // Create/update video capture source
                if (this.activeVideoCaptureSource != null)
                {
                    if (this.settingsPage.VideoDevice == null ||
                        this.settingsPage.VideoDevice.DeviceId != this.activeVideoCaptureSource.DeviceId ||
                        this.settingsPage.VideoCaptureMode != this.activeVideoCaptureSource.CaptureMode)
                    {
                        // video capture disabled or capture device changed
                        this.activeVideoCaptureSource.Release();
                        this.activeVideoCaptureSource = null;
                    }
                }

                if (this.settingsPage.VideoDevice != null && this.settingsPage.OutputVideoEnabled && this.checkBoxCameraSource.IsChecked)
                {
                    if (this.activeVideoCaptureSource == null)
                    {
                        this.activeVideoCaptureSource = VAST.Media.SourceFactory.CreateVideoCapture(this.settingsPage.VideoDevice.DeviceId, this.settingsPage.VideoCaptureMode);
                        this.activeVideoCaptureSource.Rotation = this.videoRotation;
                        this.activeVideoCaptureSource.AddRef();
                    }
                    this.videoCaptureSourceIndex = descriptor.Sources.Count;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source { MediaSource = this.activeVideoCaptureSource });
                }

                // create/update audio capture source
                if (this.activeAudioCaptureSource != null)
                {
                    if (this.settingsPage.AudioDevice == null ||
                        this.settingsPage.AudioDevice.DeviceId != this.activeAudioCaptureSource.DeviceId ||
                        this.settingsPage.AudioCaptureMode != this.activeAudioCaptureSource.CaptureMode)
                    {
                        // audio capture disabled or capture device changed
                        this.activeAudioCaptureSource.Release();
                        this.activeAudioCaptureSource = null;
                    }
                }

                if (this.settingsPage.AudioDevice != null && this.settingsPage.OutputAudioEnabled)
                {
                    if (this.activeAudioCaptureSource == null)
                    {
                        this.activeAudioCaptureSource = VAST.Media.SourceFactory.CreateAudioCapture(this.settingsPage.AudioDevice.DeviceId, this.settingsPage.AudioCaptureMode);
                        this.activeAudioCaptureSource.AddRef();
                    }
                    this.audioCaptureSourceIndex = descriptor.Sources.Count;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source { MediaSource = this.activeAudioCaptureSource });
                }

                // create/update screen capture source
                if (this.activeScreenCaptureSource != null)
                {
                    if (this.screen == null || this.screen.DeviceId != this.activeScreenCaptureSource.DeviceId)
                    {
                        // screen capture disabled or captured screen changed
                        this.activeScreenCaptureSource.Release();
                        this.activeScreenCaptureSource = null;
                    }
                }

                if (this.screen != null && this.settingsPage.OutputVideoEnabled)
                {
                    if (this.activeScreenCaptureSource == null)
                    {
                        this.activeScreenCaptureSource = VAST.Media.SourceFactory.CreateScreenCapture();
                        this.activeScreenCaptureSource.DeviceId = this.screen.DeviceId;
                        this.activeScreenCaptureSource.Region = new VAST.Common.Rect { Left = this.screen.Location.Left, Top = this.screen.Location.Top, Right = this.screen.Location.Right, Bottom = this.screen.Location.Bottom };
                        this.activeScreenCaptureSource.ShowMouse = true;
                        this.activeScreenCaptureSource.AddRef();
                    }
                    this.screenCaptureSourceIndex = descriptor.Sources.Count;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source { MediaSource = this.activeScreenCaptureSource });
                }

                // create/update video file source
                if (this.checkBoxVideoFileSource.IsChecked && this.settingsPage.OutputVideoEnabled)
                {

#if VAST_FEATURE_FILE_ISO

                    this.videoFileSourceIndex = descriptor.Sources.Count;
                    var fileSource = new VAST.File.ISO.IsoSource();
                    var t = FileSystem.OpenAppPackageFileAsync("video.mp4");
                    t.Wait();

                    Stream s = t.Result;
                    if (!t.Result.CanSeek)
                    {
                        // stream must be seekable but it's not on Android
                        s = new MemoryStream();
                        t.Result.CopyTo(s);
                        t.Result.Dispose();
                    }

                    fileSource.Stream = s;
                    fileSource.PlaybackRate = 1;
                    fileSource.Loop = true;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source { MediaSource = fileSource });

#endif

                }

                // create/update streaming source
                if (this.checkBoxStreamingSource.IsChecked && !string.IsNullOrEmpty(this.tboxStreamingSource.Text) && this.settingsPage.OutputVideoEnabled)
                {
                    this.streamingSourceIndex = descriptor.Sources.Count;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source { Uri = this.tboxStreamingSource.Text });
                }

                // create/update user source
                if (this.checkBoxManualSource.IsChecked && this.settingsPage.OutputVideoEnabled)
                {

                    if (this.userSource == null)
                    {
                        this.userSource = new VAST.Network.VirtualNetworkSource();
                        this.userSource.AddRef();
                        this.userSource.AddStream(new VAST.Common.MediaType
                        {
                            ContentType = VAST.Common.ContentType.Video,
                            CodecId = VAST.Common.Codec.Uncompressed,
                            PixelFormat = VAST.Common.PixelFormat.BGRA,
                            Width = this.outputWidth,
                            Height = this.outputHeight,
                            Framerate = this.outputFramerate,
                        });
                    }

                    this.userSourceIndex = descriptor.Sources.Count;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source { MediaSource = this.userSource });

                }

                // create/update text overlay source
                if (this.checkBoxOverlayTime.IsChecked && this.settingsPage.OutputVideoEnabled)
                {
                    this.textOverlaySourceIndex = descriptor.Sources.Count;
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source
                    {
                        Content = DateTime.Now.ToString(),
                        Format = "text",
                        HorizontalAlignment = VAST.Common.HorizontalAlignment.Left,
                        VerticalAlignment = VAST.Common.VerticalAlignment.Top,
                        Decoration = new VAST.Image.Mixing.Decoration
                        {
                            FontFamily = "Calibri",
                            Size = 30,
                            Bold = true,
                            Italic = true,
                            Color = "#FFFFFF00",
                            OutlineColor = "#FF000000",
                            OutlineWidth = 1,
                        }
                    });
                }

                // create/update logo source
                if (this.checkBoxOverlayLogo.IsChecked && this.settingsPage.OutputVideoEnabled)
                {
                    this.logoOverlaySourceIndex = descriptor.Sources.Count;
                    var t = FileSystem.OpenAppPackageFileAsync("watermark.png");
                    t.Wait();
                    descriptor.Sources.Add(new VAST.Image.Mixing.Source
                    {
                        // read resource stream
                        Content = t.Result,
                        Format = "image"
                    });
                }

                this.updateScene(descriptor);

                if (this.audioCaptureSourceIndex >= 0)
                {
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

                if (this.textOverlaySourceIndex >= 0)
                {
                    if (this.textUpdateTask == null)
                    {
                        this.textUpdateTaskActive = true;
                        this.textUpdateTask = Task.Run(async () =>
                        {
                            while (this.textUpdateTaskActive && this.textOverlaySourceIndex >= 0)
                            {
                                await Task.Delay(1000);
                                MainThread.BeginInvokeOnMainThread(() => this.updateSources(null));
                            }
                        });
                    }
                }
                else
                {
                    if (this.textUpdateTask != null)
                    {
                        this.textUpdateTaskActive = true;
                        this.textUpdateTask = null;
                    }
                }

                if (this.userSourceIndex >= 0)
                {
                    if (this.manualPusher == null)
                    {
                        // start manual push
                        this.manualPushingRunning = true;
                        this.manualPusher = Task.Run(() => manualPushingRoutine());
                    }
                }
                else
                {
                    if (this.manualPusher != null)
                    {
                        this.manualPushingRunning = false;
                        this.manualPusher = null;
                    }
                }

            }

        }

        /// <summary>
        /// Updates the scene composition including layer arrangement and visual effects.
        /// Configures how video sources are composited and what effects are applied.
        /// </summary>
        /// <param name="descriptor">
        /// The mixing descriptor to update, or null to create a new descriptor for scene-only updates.
        /// </param>
        /// <remarks>
        /// Configures video mixing layers with:
        /// - Layer ordering (z-order) for source compositing
        /// - Manual layout positioning for picture-in-picture effects
        /// - Brightness and contrast adjustments
        /// - Chroma key (green screen) color removal
        /// - Text overlay positioning
        /// - Logo watermark positioning
        ///
        /// Audio mixing is configured as single-source (microphone only).
        /// </remarks>
        private void updateScene(VAST.Image.Mixing.Descriptor descriptor)
        {

            if (this.mixingSource == null) return;

            // Create new descriptor if this is a scene-only update
            if (descriptor == null)
            {
                // this is a scene update
                descriptor = new VAST.Image.Mixing.Descriptor();
            }

            // Ensure processing section exists
            if (descriptor.Processing == null)
            {
                descriptor.Processing = new VAST.Image.Mixing.Processing();
            }

            bool allowVideo = this.settingsPage.OutputVideoEnabled;
            bool allowAudio = this.settingsPage.OutputAudioEnabled;

            if (allowVideo)
            {

                if (descriptor.Processing.VideoProcessing == null)
                {
                    descriptor.Processing.VideoProcessing = new VAST.Image.Mixing.VideoProcessing();
                }

                descriptor.Processing.VideoProcessing.Mixing = new VAST.Image.Mixing.VideoMixing
                {
                    Type = VAST.Image.Mixing.VideoMixingType.All,
                    Layers = new List<VAST.Image.Mixing.Layer>()
                };

                if (this.screenCaptureSourceIndex >= 0)
                {
                    if (this.checkBoxScreenSourceBehind.IsChecked)
                    {
                        this.addScreenCaptureSource(descriptor);
                        this.addVideoCaptureSource(descriptor);
                    }
                    else
                    {
                        this.addVideoCaptureSource(descriptor);
                        this.addScreenCaptureSource(descriptor);
                    }
                }
                else
                {
                    this.addVideoCaptureSource(descriptor);
                }

                if (this.videoFileSourceIndex >= 0)
                {
                    descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
                    {
                        Sources = new List<int>(new int[] { this.videoFileSourceIndex }),
                        Layout = VAST.Image.Mixing.LayoutType.Manual,
                        Stretch = VAST.Image.Mixing.StretchType.Preserve,
                        Location = new VAST.Common.Rect(50, this.outputHeight / 2 + 50, this.outputWidth / 2 - 50, this.outputHeight - 50)
                    });
                }

                if (this.streamingSourceIndex >= 0)
                {
                    descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
                    {
                        Sources = new List<int>(new int[] { this.streamingSourceIndex }),
                        Layout = VAST.Image.Mixing.LayoutType.Manual,
                        Stretch = VAST.Image.Mixing.StretchType.Preserve,
                        Location = new VAST.Common.Rect(this.outputWidth / 2 + 50, this.outputHeight / 2 + 50, this.outputWidth - 50, this.outputHeight - 50)
                    });
                }

                if (this.textOverlaySourceIndex >= 0)
                {
                    descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
                    {
                        Sources = new List<int>(new int[] { this.textOverlaySourceIndex }),
                        Layout = VAST.Image.Mixing.LayoutType.Manual,
                        Location = new VAST.Common.Rect(50, this.outputHeight - 100, this.outputWidth, this.outputHeight)
                    });
                }

                if (this.logoOverlaySourceIndex >= 0)
                {
                    descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
                    {
                        Sources = new List<int>(new int[] { this.logoOverlaySourceIndex }),
                        Layout = VAST.Image.Mixing.LayoutType.Manual,
                        Location = new VAST.Common.Rect(50, 50, 50 + 245, 50 + 59)
                    });
                }

                if (this.userSourceIndex >= 0)
                {
                    descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
                    {
                        Sources = new List<int>(new int[] { this.userSourceIndex }),
                        Layout = VAST.Image.Mixing.LayoutType.Manual,
                        Location = new VAST.Common.Rect(0, 0, this.outputWidth, this.outputHeight)
                    });
                }

            }

            if (allowAudio)
            {

                if (descriptor.Processing.AudioProcessing == null)
                {
                    descriptor.Processing.AudioProcessing = new VAST.Image.Mixing.AudioProcessing();
                }

                descriptor.Processing.AudioProcessing.Mixing = new VAST.Image.Mixing.AudioMixing
                {
                    Type = VAST.Image.Mixing.AudioMixingType.Single,
                    SourceIndex = this.audioCaptureSourceIndex,
                };

            }

            this.mixingSource.Update(descriptor);

        }

        /// <summary>
        /// Adds the video capture (camera) source as a mixing layer.
        /// Applies brightness, contrast, and optional chroma key (green screen) effects.
        /// </summary>
        /// <param name="descriptor">The mixing descriptor to add the layer to.</param>
        /// <remarks>
        /// The camera layer is positioned to fill the entire output frame.
        /// Chroma key parameters are derived from UI slider values with base offsets
        /// to provide reasonable default behavior.
        /// </remarks>
        private void addVideoCaptureSource(VAST.Image.Mixing.Descriptor descriptor)
        {
            descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
            {
                Sources = new List<int>(new int[] { this.videoCaptureSourceIndex }),
                Layout = VAST.Image.Mixing.LayoutType.Manual,
                Stretch = VAST.Image.Mixing.StretchType.Preserve,
                Location = new VAST.Common.Rect(0, 0, this.outputWidth, this.outputHeight),
                BrightnessAdjustment = (float)this.sliderBrightness.Value,
                ContrastAdjustment = (float)this.sliderContrast.Value,
                BlurSize = 0,
                // Chroma key parameters: base value + slider adjustment
                ChromaKeyColor = string.IsNullOrEmpty(this.tboxChromaKeyColor.Text) ? null : this.tboxChromaKeyColor.Text,
                ChromaKeyThreshold = 0.07f + (float)this.sliderChromaKeyThreshold.Value / 200f,
                ChromaKeySmoothing = 0.05f + (float)this.sliderChromaKeySmoothing.Value / 200f,
            });
        }

        /// <summary>
        /// Adds the screen capture (display) source as a mixing layer.
        /// </summary>
        /// <param name="descriptor">The mixing descriptor to add the layer to.</param>
        /// <remarks>
        /// The screen capture layer fills the entire output frame with preserved aspect ratio.
        /// Layer ordering with camera is controlled by the "behind" checkbox option.
        /// </remarks>
        private void addScreenCaptureSource(VAST.Image.Mixing.Descriptor descriptor)
        {
            descriptor.Processing.VideoProcessing.Mixing.Layers.Add(new VAST.Image.Mixing.Layer
            {
                Sources = new List<int>(new int[] { this.screenCaptureSourceIndex }),
                Layout = VAST.Image.Mixing.LayoutType.Manual,
                Stretch = VAST.Image.Mixing.StretchType.Preserve,
                Location = new VAST.Common.Rect(0, 0, this.outputWidth, this.outputHeight),
            });
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
        /// Called when device orientation changes during active capture.
        /// </summary>
        /// <remarks>
        /// Thread-safe via locking to prevent race conditions with background tasks.
        /// Disposes the existing capture source to force recreation with new rotation.
        /// Restarts preview if it was active before the rotation change.
        /// </remarks>
        private void recreateVideoCaptureSource()
        {
            if (this.mixingSource != null && this.videoCaptureSourceIndex >= 0)
            {
                try
                {
                    // Lock to prevent race condition with textUpdateTask
                    lock (this)
                    {
                        if (this.activeVideoCaptureSource != null)
                        {
                            // Dispose to force recreation with new rotation
                            this.activeVideoCaptureSource.Dispose();
                            this.activeVideoCaptureSource = null;
                        }
                        this.updateSources(null);

                        // Restart preview with new rotation
                        if (this.previewSession != null)
                        {
                            // need to re-create preview
                            this.stopPreview();
                            Task t = this.startPreview();
                        }
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

        /// <summary>
        /// Releases all shared resources when no sessions are using them.
        /// Called after preview or streaming stops to free capture devices and background tasks.
        /// </summary>
        /// <remarks>
        /// Uses reference counting to ensure resources are only released when
        /// all sessions (preview and streaming) have stopped.
        /// Releases: mixing source, capture sources, background tasks, and device monitor.
        /// </remarks>
        private void cleanup()
        {

            // Skip if already cleaned up
            if (this.mixingSource == null)
            {
                // already cleaned up
                return;
            }

            // Don't clean up if other sessions are still using the mixing source
            if (this.mixingSource.RefCount > 1)
            {
                // one or more media sessions are still active
                return;
            }

            // Release the mixing source
            this.mixingSource.NewStream -= MixingSource_NewStream;
            this.mixingSource.Release();
            this.mixingSource = null;

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

            // Release screen capture source
            if (this.activeScreenCaptureSource != null)
            {
                this.activeScreenCaptureSource.Release();
                this.activeScreenCaptureSource = null;
            }

            // Stop manual pushing background task
            if (this.manualPusher != null)
            {
                this.manualPushingRunning = false;
                this.manualPusher = null;
            }

            // Stop text update background task
            if (this.textUpdateTask != null)
            {
                this.textUpdateTaskActive = false;
                this.textUpdateTask = null;
            }

            // Dispose audio level monitor
            if (this.deviceMonitor != null)
            {
                this.deviceMonitor.MeterUpdated -= DeviceMonitor_MeterUpdated;
                this.deviceMonitor.Dispose();
                this.deviceMonitor = null;
            }

            this.isOutputEncoded = false;

        }

        /// <summary>
        /// Updates UI control enabled states based on current session activity.
        /// Disables encoding parameter controls while capture/streaming is active.
        /// </summary>
        /// <remarks>
        /// Encoding parameters (resolution, bitrate, codec settings) cannot be changed
        /// while sessions are active because they require mixing source recreation.
        /// </remarks>
        private void updateControls()
        {
        }

        /// <summary>
        /// Helper function for HSL to RGB color conversion.
        /// Calculates an RGB component value based on HSL intermediate values.
        /// </summary>
        /// <param name="v1">The first intermediate value from HSL conversion.</param>
        /// <param name="v2">The second intermediate value from HSL conversion.</param>
        /// <param name="vH">The adjusted hue value (0-1 range).</param>
        /// <returns>The RGB component value (0-1 range).</returns>
        private static float HueToRGB(float v1, float v2, float vH)
        {
            // Normalize hue to 0-1 range
            if (vH < 0) vH += 1;
            if (vH > 1) vH -= 1;

            // Convert hue to RGB based on position in color wheel
            if ((6 * vH) < 1) return (v1 + (v2 - v1) * 6 * vH);
            if ((2 * vH) < 1) return v2;
            if ((3 * vH) < 2) return (v1 + (v2 - v1) * ((2.0f / 3) - vH) * 6);
            return v1;
        }

        /// <summary>
        /// Converts HSL (Hue, Saturation, Lightness) color values to BGRA format.
        /// </summary>
        /// <param name="h">Hue value in degrees (0-360).</param>
        /// <param name="s">Saturation value (0-1).</param>
        /// <param name="l">Lightness value (0-1).</param>
        /// <returns>A 32-bit BGRA color value with full alpha (0xFF).</returns>
        private static uint HSLtoBGRA(float h, float s, float l)
        {

            uint r = 0;
            uint g = 0;
            uint b = 0;

            if (s == 0.0)
            {
                // Achromatic (gray) - all components equal
                r = g = b = (uint)(l * 255.0 + 0.5);
            }
            else
            {
                // Chromatic - calculate RGB from HSL
                float v1, v2;
                float hue = h / 360;
                v2 = (l < 0.5) ? (l * (1 + s)) : ((l + s) - (l * s));
                v1 = 2 * l - v2;
                r = (uint)(255 * HueToRGB(v1, v2, hue + (1.0f / 3)));
                g = (uint)(255 * HueToRGB(v1, v2, hue));
                b = (uint)(255 * HueToRGB(v1, v2, hue - (1.0f / 3)));
            }

            // Pack as BGRA with full alpha
            return (uint)(0xFF000000 | (r << 24) | (g << 16) | (b << 8));

        }

        /// <summary>
        /// Background routine that generates and pushes animated video frames.
        /// Demonstrates programmatic frame generation for the user-provided source.
        /// </summary>
        /// <remarks>
        /// Creates a bouncing rectangle with cycling colors that overlays on other video sources.
        /// Uses SkiaSharp for frame rendering at 30 fps.
        /// Frame timing uses 100-nanosecond ticks for precise timestamp control.
        /// The rectangle bounces off frame edges with randomized direction changes.
        /// </remarks>
        private void manualPushingRoutine()
        {

            SKBitmap bmpImage = null;
            SKCanvas canvas = null;

            try
            {

                // Wait for the user source to start
                while (this.manualPushingRunning && this.userSource.State < Media.MediaState.Started)
                {
                    Task.Delay(10).Wait();
                }

                // Verify source is running before proceeding
                if (!this.manualPushingRunning || this.userSource.State > Media.MediaState.Started)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MauiProgram.VastDisplayAlert(this, "Error", $"Manual pushing source is not running!", "OK");
                    });
                    return;
                }

                // Create bitmap and canvas for frame rendering
                bmpImage = new SKBitmap(this.outputWidth, this.outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                canvas = new SKCanvas(bmpImage);
                SKFont font = new SKFont(SKTypeface.Default, 20);

                // Animation state variables
                int hue = 0;
                long streamingStarted = VAST.Common.MediaTime.Get();
                long currentVideoTimestamp = streamingStarted;
                int rectWidth = 200;
                int rectHeight = 120;
                long videoFrameCount = 0;
                int framerate = 30;
                float positionX = 0;
                float positionY = 0;
                float incrementX = 0.5f;
                float incrementY = 0.5f;
                float delta = 2;
                Random rnd = new Random();
                int imageSize = bmpImage.Width * bmpImage.Height * 4;

                // Frame generation loop
                while (this.manualPushingRunning && this.userSourceIndex >= 0)
                {

                    // Generate cycling colors using HSL color space
                    uint color = HSLtoBGRA((float)hue, 1.0f, 0.5f);
                    uint fontColor = HSLtoBGRA((float)(360 - hue), 1.0f, 0.5f);
                    hue = (hue + 2) % 360;

                    // Render frame with bouncing rectangle and text
                    canvas.Clear(SKColors.Transparent);
                    using (var paint = new SKPaint { Color = new SKColor(color), Style = SKPaintStyle.Fill })
                    {
                        canvas.DrawRect(positionX, positionY, rectWidth, rectHeight, paint);
                    }

                    using (var paint = new SKPaint { Color = new SKColor(fontColor), Style = SKPaintStyle.Fill })
                    {
                        canvas.DrawText("Smooth user", positionX + 10 + 20, positionY + 10 + 40, font, paint);
                        canvas.DrawText("provided source", positionX + 10 + 20, positionY + 10 + 70, font, paint);
                    }

                    canvas.Flush();

                    // Push the frame to the mixing source
                    IntPtr ptr = bmpImage.GetPixels();
                    VAST.Common.VersatileBuffer vb = VAST.Media.MediaGlobal.LockBuffer(imageSize);
                    vb.Append(bmpImage.GetPixels(), imageSize);
                    vb.Pts = vb.Dts = currentVideoTimestamp;
                    vb.StreamIndex = 0;
                    this.userSource.PushMedia(vb);
                    vb.Release();

                    // Calculate next frame timestamp
                    videoFrameCount++;
                    long playbackTime = VAST.Common.MediaTime.Get() - streamingStarted;
                    long nextVideoFrameTime = videoFrameCount * 10000000L / framerate;
                    currentVideoTimestamp = streamingStarted + nextVideoFrameTime;

                    // Update animation position with edge bouncing
                    positionX += delta * incrementX;
                    positionY += delta * incrementY;

                    // Bounce off left edge
                    if (positionX <= 0)
                    {
                        incrementX = (float)rnd.NextDouble();
                        incrementY = (1 - Math.Abs(incrementX)) * ((rnd.Next(2) == 0) ? 1 : (-1));
                    }
                    // Bounce off right edge
                    else if (positionX + rectWidth >= outputWidth)
                    {
                        incrementX = -(float)rnd.NextDouble();
                        incrementY = (1 - Math.Abs(incrementX)) * ((rnd.Next(2) == 0) ? 1 : (-1));
                    }
                    // Bounce off top edge
                    else if (positionY <= 0)
                    {
                        incrementY = (float)rnd.NextDouble();
                        incrementX = (1 - Math.Abs(incrementY)) * ((rnd.Next(2) == 0) ? 1 : (-1));
                    }
                    // Bounce off bottom edge
                    else if (positionY + rectHeight >= outputHeight)
                    {
                        incrementY = -(float)rnd.NextDouble();
                        incrementX = (1 - Math.Abs(incrementY)) * ((rnd.Next(2) == 0) ? 1 : (-1));
                    }

                    // Throttle to maintain target framerate
                    if (nextVideoFrameTime > playbackTime)
                    {
                        int sleepTimeMs = (int)((nextVideoFrameTime - playbackTime) / 10000 / 2);
                        if (sleepTimeMs > 0)
                        {
                            Task.Delay(sleepTimeMs).Wait();
                        }
                    }

                }

            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MauiProgram.VastDisplayAlert(this, "Error", $"Manual pushing exception: {ex}", "OK");
                });
            }
            finally
            {
                // Clean up graphics resources
                if (canvas != null) try { canvas.Dispose(); } catch { }
                if (bmpImage != null) try { bmpImage.Dispose(); } catch { }

                // Release the user source
                if (this.userSource != null)
                {
                    this.userSource.Release();
                    this.userSource = null;
                }
            }

        }

    }

}
