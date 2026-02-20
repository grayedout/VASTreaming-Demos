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
    /// Demo page for extended media playback functionality with advanced features.
    /// Demonstrates media playback with recording, snapshots, track selection,
    /// hardware acceleration, and audio device selection (Windows only).
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class ExtendedPlaybackPage : ContentPage
    {

        /// <summary>
        /// Platform-specific utilities for file paths and other platform-dependent operations.
        /// </summary>
        private PlatformSpecific platformSpecific = new PlatformSpecific();

        /// <summary>
        /// The default URI to use for playback (empty by default).
        /// </summary>
        private string defaultUri = "";

        /// <summary>
        /// Indicates whether the URI-specific controls have been initialized after playback starts.
        /// </summary>
        private bool uriControlsInitialized = false;

        /// <summary>
        /// Flag to prevent recursive updates when programmatically changing the position slider.
        /// </summary>
        private bool preventMediaElementUpdate = false;

        /// <summary>
        /// Indicates whether the user is currently dragging the position slider thumb.
        /// </summary>
        private bool thumbDragging = false;

        /// <summary>
        /// Timer for periodic position updates during playback.
        /// </summary>
        private Timer timer = null;

        /// <summary>
        /// The media source used for playback and recording.
        /// </summary>
        private VAST.Media.IMediaSource mediaSource = null;

        /// <summary>
        /// The media session used for recording playback to a file.
        /// </summary>
        private VAST.Media.MediaSession recordingSession = null;

        /// <summary>
        /// List of available audio output devices (Windows only).
        /// </summary>
        private List<VAST.Capture.AudioCaptureDeviceDescriptor> audioDevices = null;

        /// <summary>
        /// Flag to prevent recursive updates when programmatically changing track pickers.
        /// </summary>
        private bool preventUpdate = false;

        /// <summary>
        /// The file path for recording and snapshot storage.
        /// </summary>
        private string fileRecordingPath = null;

        /// <summary>
        /// Creates a new instance of <see cref="ExtendedPlaybackPage"/>.
        /// </summary>
        /// <remarks>
        /// Initializes player event handlers, rendering strategy, decoder options,
        /// and platform-specific controls (audio device selection on Windows).
        /// </remarks>
        public ExtendedPlaybackPage()
        {

            InitializeComponent();

            // Get platform-specific recording path
            #pragma warning disable CA1416
            this.fileRecordingPath = this.platformSpecific.GetRecordingPath();
            #pragma warning restore CA1416

            // Subscribe to player events
            this.player.CurrentStateChanged += player_CurrentStateChanged;
            this.player.MediaEnded += player_MediaEnded;
            this.player.MediaFailed += player_MediaFailed;

            this.pickerRenderingStrategy.SelectedIndex = 0;

            // Configure decoder options based on platform
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {
                this.pickerDecoder.ItemsSource = new List<string> { "Media Foundation", "FFmpeg", "Nvidia" };
            }
            else
            {
                this.pickerDecoder.ItemsSource = new List<string> { "Builtin", "FFmpeg" };
            }
            this.pickerDecoder.SelectedIndex = 0;

#if WINDOWS
            // Configure Windows-specific controls
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {

                this.pickerVideoRenderer.SelectedIndex = 0;

                Task.Run(async () =>
                {

                    // Enumerate WASAPI audio output devices
                    var t = await VAST.Capture.AudioDeviceEnumerator.Enumerate(new VAST.Capture.AudioDeviceEnumeratorParameters { Framework = VAST.Common.MediaFramework.WASAPI, Direction = VAST.Common.MediaFlowDirection.Output });
                    List<string> deviceNames = new List<string>();
                    if (t != null)
                    {
                        this.audioDevices = new List<VAST.Capture.AudioCaptureDeviceDescriptor>();
                        foreach (var device in t)
                        {
                            if (device.Direction == VAST.Common.MediaFlowDirection.Output)
                            {
                                this.audioDevices.Add(device);
                                deviceNames.Add(device.Name);
                            }
                        }
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Populate audio device picker
                        if (this.audioDevices.Count > 0)
                        {
                            this.pickerAudioDevice.ItemsSource = deviceNames;
                            this.pickerAudioDevice.SelectedIndex = 0;
                        }
                    });

                });

            }
            else
#endif
            {
                // Hide Windows-specific controls on other platforms
                this.layoutWindowsControls.IsVisible = false;
            }

            this.tboxUri.Text = this.defaultUri;

#if VAST_FEATURE_ONVIF

            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {

                // Show the ONVIF button (hidden by default in XAML for builds without ONVIF support)
                this.btnOnvif.IsVisible = true;

                // Wire up ONVIF discovery and scanning events
                this.btnDiscover.Clicked += btnDiscover_Clicked;
                this.pickerOnvifDevice.SelectedIndexChanged += pickerOnvifDevice_SelectedIndexChanged;
                this.btnScanOnvif.Clicked += btnScanOnvif_Clicked;
                this.btnPlayOnvifUri.Clicked += btnPlayOnvifUri_Clicked;

                // Wire up PTZ zoom events (Pressed starts continuous zoom, Released stops it)
                this.btnZoomIn.Pressed += btnZoomIn_Pressed;
                this.btnZoomIn.Released += btnZoomIn_Released;
                this.btnZoomOut.Pressed += btnZoomOut_Pressed;
                this.btnZoomOut.Released += btnZoomOut_Released;

                // Wire up PTZ pan/tilt events (Pressed starts continuous movement, Released stops it)
                this.btnMoveLeft.Pressed += btnMoveLeft_Pressed;
                this.btnMoveLeft.Released += btnMoveLeft_Released;
                this.btnMoveRight.Pressed += btnMoveRight_Pressed;
                this.btnMoveRight.Released += btnMoveRight_Released;
                this.btnMoveUp.Pressed += btnMoveUp_Pressed;
                this.btnMoveUp.Released += btnMoveUp_Released;
                this.btnMoveDown.Pressed += btnMoveDown_Pressed;
                this.btnMoveDown.Released += btnMoveDown_Released;

                // Wire up PTZ preset and snapshot events
                this.btnSaveHome.Clicked += btnSaveHome_Clicked;
                this.btnRecallHome.Clicked += btnRecallHome_Clicked;
                this.btnSavePreset.Clicked += btnSavePreset_Clicked;
                this.btnRecallPreset.Clicked += btnRecallPreset_Clicked;
                this.btnOnvifSnapshot.Clicked += btnOnvifSnapshot_Clicked;

            }

#endif

            //this.btnTest.Clicked += (object sender, EventArgs e) =>
            //{
            //    ++this.uriNo;
            //    if (this.uriNo > 8) this.uriNo = 1;
            //    switch (this.uriNo)
            //    {
            //        case 1:
            //            this.tboxUri.Text = "rtsp://192.168.0.102/loop";
            //            //this.tboxUri.Text = "http://192.168.0.102:8888/hls/loop";
            //            break;
            //        case 2:
            //            this.tboxUri.Text = "rtmp://192.168.0.102/live/video";
            //            break;
            //        case 3:
            //            this.tboxUri.Text = "rtsp://192.168.0.102/audio";
            //            break;
            //        case 4:
            //            this.tboxUri.Text = "http://192.168.0.102:8888/hls/mixing";
            //            break;
            //        case 5:
            //            this.tboxUri.Text = "http://192.168.0.102:8888/mjpeg/mjpeg";
            //            break;
            //        case 6:
            //            this.tboxUri.Text = "http://192.168.0.102:8888/hls/vod1";
            //            break;
            //        case 7:
            //            this.tboxUri.Text = "http://192.168.0.102:8888/hls/vod2";
            //            break;
            //        case 8:
            //            this.tboxUri.Text = "http://192.168.0.102:8888/hls/vod3";
            //            break;
            //    }
            //};

        }

        /// <summary>
        /// Handles the page navigating from event by stopping playback and recording.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {
            this.stop();
            this.stopRecordingSession();
#if VAST_FEATURE_ONVIF
            this.cleanupOnvif();
#endif
        }

        /// <summary>
        /// Handles the Controls button click event to toggle the playback controls overlay panel.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Toggles visibility of both the controls panel and the transparent dismiss layer behind it.
        /// Hides the ONVIF panel when the controls panel is opened to ensure mutual exclusivity
        /// between overlay panels.
        /// </remarks>
        private void btnControls_Clicked(object sender, EventArgs e)
        {
            this.controlsPanel.IsVisible = !this.controlsPanel.IsVisible;
            this.onvifPanel.IsVisible = false;
            this.panelDismissLayer.IsVisible = this.controlsPanel.IsVisible;
        }

        /// <summary>
        /// Handles taps on the transparent dismiss layer to close all overlay panels.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The tap event arguments.</param>
        /// <remarks>
        /// The dismiss layer is a full-screen transparent BoxView that sits between the video player
        /// and the overlay panels in the z-order. Tapping anywhere outside the panels closes both
        /// the controls panel and the ONVIF panel.
        /// </remarks>
        private void dismissPanel_Tapped(object sender, TappedEventArgs e)
        {
            this.controlsPanel.IsVisible = false;
            this.onvifPanel.IsVisible = false;
            this.panelDismissLayer.IsVisible = false;
        }

        /// <summary>
        /// Handles the play button click event to start or resume playback.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Configures playback parameters including rendering strategy, decoder framework,
        /// hardware acceleration, video renderer type, and audio output device before starting playback.
        /// If playback is already active with a modified playback rate, resets to normal speed.
        /// </remarks>
        private void btnPlay_Clicked(object sender, EventArgs e)
        {

            // If already playing with modified speed, reset to normal
            if (this.player.CurrentState != VAST.Media.PlayerState.Closed)
            {
                if (this.player.PlaybackRate != 1.0)
                {
                    this.player.PlaybackRate = 1.0;
                    return;
                }
            }

            // Configure automatic track selection
            this.player.AudioStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;
            this.player.VideoStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;

            // Configure rendering strategy
            switch (this.pickerRenderingStrategy.SelectedIndex)
            {
                case 0:
                    this.player.PlaybackParameters.RenderingStrategy = VAST.Media.RenderingStrategy.LowLatency;
                    break;
                case 1:
                    this.player.PlaybackParameters.RenderingStrategy = VAST.Media.RenderingStrategy.Smooth;
                    break;
            }

            // Configure decoder framework
            switch (this.pickerDecoder.SelectedIndex)
            {
                case 0: // Builtin
                default:
                    this.player.PlaybackParameters.VideoDecoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.Builtin;
                    this.player.PlaybackParameters.AudioDecoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.Builtin;
                    break;
                case 1: // FFmpeg
                    this.player.PlaybackParameters.VideoDecoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                    this.player.PlaybackParameters.AudioDecoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                    break;
                case 2: // Nvidia
                    this.player.PlaybackParameters.VideoDecoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.CUDA;
                    this.player.PlaybackParameters.AudioDecoderParameters.PreferredMediaFramework = VAST.Common.MediaFramework.Builtin;
                    break;
            }

            // Configure Windows-specific playback parameters
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {

                // CUDA requires hardware acceleration
                if (this.player.PlaybackParameters.VideoDecoderParameters.PreferredMediaFramework == VAST.Common.MediaFramework.CUDA)
                {
                    this.player.PlaybackParameters.VideoDecoderParameters.AllowHardwareAcceleration = true;
                }
                else
                {
                    this.player.PlaybackParameters.VideoDecoderParameters.AllowHardwareAcceleration = this.checkBoxHardwareAcceleration.IsChecked;
                }

                // Configure video renderer type
                switch (this.pickerVideoRenderer.SelectedIndex)
                {
                    case 0:
                        this.player.PlaybackParameters.VideoRendererType = VAST.Media.VideoRendererType.Auto;
                        break;
                    case 1:
                        this.player.PlaybackParameters.VideoRendererType = VAST.Media.VideoRendererType.Best;
                        break;
                    case 2:
                        this.player.PlaybackParameters.VideoRendererType = VAST.Media.VideoRendererType.Compatible;
                        break;
                }

                // Set audio output device
                if (this.pickerAudioDevice.SelectedIndex >= 0 && this.audioDevices != null && this.audioDevices.Count > 0)
                {
                    this.player.PlaybackParameters.AudioOutputDeviceId = this.audioDevices[this.pickerAudioDevice.SelectedIndex].DeviceId;
                }

            }

            // Create media source and start playback
            this.createMediaSource();
            this.player.SourceMedia = this.mediaSource;
            this.player.Play();

        }

        /// <summary>
        /// Handles the stop button click event to stop playback.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStop_Clicked(object sender, EventArgs e)
        {
            this.stop();
        }

        /// <summary>
        /// Handles the pause button click event to toggle between paused and playing states.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnPause_Click(object sender, EventArgs e)
        {
            switch (this.player.CurrentState)
            {
                case Media.PlayerState.Playing:
                    this.player.Pause();
                    this.btnPause.Text = "Resume";
                    break;
                case Media.PlayerState.Paused:
                    this.player.Play();
                    this.btnPause.Text = "Pause";
                    break;
            }
        }

        /// <summary>
        /// Handles the rewind button click event to play backwards at increasing speeds.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// If currently playing forward, switches to -1x speed.
        /// If already playing backwards, doubles the rewind speed.
        /// </remarks>
        private void btnRewind_Clicked(object sender, EventArgs e)
        {
            if (this.player.PlaybackRate > 0)
            {
                this.player.PlaybackRate = -1.0;
            }
            else
            {
                this.player.PlaybackRate *= 2;
            }
        }

        /// <summary>
        /// Handles the fast forward button click event to play forward at increasing speeds.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// If currently playing backwards, switches to normal 1x speed.
        /// If already playing forward, doubles the playback speed.
        /// </remarks>
        private void btnFastForward_Clicked(object sender, EventArgs e)
        {
            if (this.player.PlaybackRate < 0)
            {
                this.player.PlaybackRate = 1.0;
            }
            else
            {
                this.player.PlaybackRate *= 2;
            }
        }

        /// <summary>
        /// Handles the start record button click event to begin recording.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStartRecord_Click(object sender, EventArgs e)
        {
            this.createRecordingSession();
        }

        /// <summary>
        /// Handles the stop record button click event to stop recording.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnStopRecord_Click(object sender, EventArgs e)
        {
            this.stopRecordingSession();
        }

        /// <summary>
        /// Handles the take snapshot button click event to capture a JPEG image from the current video frame.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Saves the snapshot as a JPEG file with a timestamp-based filename to the recording path.
        /// </remarks>
        private void btnTakeSnapshot_Click(object sender, EventArgs e)
        {

            if (string.IsNullOrEmpty(this.fileRecordingPath))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MauiProgram.VastDisplayAlert(this, "Error", "File path is not specified", "OK");
                });
                return;
            }

            // Generate snapshot filename with timestamp
            string snapshotPath = System.IO.Path.Combine(this.fileRecordingPath, $"snapshot-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.jpg");

            // Capture and save snapshot asynchronously
            Task.Run(async () =>
            {
                try
                {
                    using (var jpegStream = await this.player.TakeSnapshot())
                    {
                        if (jpegStream != null)
                        {
                            using (var fs = new System.IO.FileStream(snapshotPath, System.IO.FileMode.OpenOrCreate))
                            {
                                jpegStream.Position = 0;
                                await jpegStream.CopyToAsync(fs);
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    MauiProgram.VastDisplayAlert(this, "Information", $"Snapshot successfully saved to {snapshotPath}", "OK");
                                });
                            }
                        }
                        else
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                MauiProgram.VastDisplayAlert(this, "Error", "Failed to obtain snapshot", "OK");
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MauiProgram.VastDisplayAlert(this, "Error", $"Failed to obtain snapshot:\n{ex}", "OK");
                    });
                }
            });

        }

        /// <summary>
        /// Handles the send log button click event to send diagnostic logs to support.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnSendLog_Clicked(object sender, EventArgs e)
        {
            if (await VAST.Common.License.SendLog("MAUI extended playback issue"))
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "The log has been sent successfully", "OK");
            }
            else
            {
                await MauiProgram.VastDisplayAlert(this, "Error", "Failed to send the log! Please grab it manually and send to support@vastreaming.net.", "OK");
            }
        }

        /// <summary>
        /// Handles video track selection changes from the picker control.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerVideoTrack_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!this.preventUpdate)
            {
                this.player.VideoStreamIndex = this.pickerVideoTrack.SelectedIndex;
            }
        }

        /// <summary>
        /// Handles audio track selection changes from the picker control.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void pickerAudioTrack_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!this.preventUpdate)
            {
                this.player.AudioStreamIndex = this.pickerAudioTrack.SelectedIndex;
            }
        }

        /// <summary>
        /// Handles player state changes to initialize controls when playback starts.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="playerState">The new player state.</param>
        private void player_CurrentStateChanged(object sender, VAST.Media.PlayerState playerState)
        {
            switch (playerState)
            {
                case VAST.Media.PlayerState.Playing:
                    if (!this.uriControlsInitialized)
                    {
                        this.initializeControlsFromUri();
                    }
                    break;
            }
        }

        /// <summary>
        /// Handles the media ended event when playback reaches the end of the media.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void player_MediaEnded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.stop();
                MauiProgram.VastDisplayAlert(this, "Information", "Media ended", "OK");
            });
        }

        /// <summary>
        /// Handles playback failures by stopping and displaying an error message.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The error event arguments containing the error description.</param>
        private void player_MediaFailed(object sender, VAST.Media.ErrorEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.stop();
                MauiProgram.VastDisplayAlert(this, "Error", $"Source error: {e.ErrorDescription}", "OK");
            });
        }

        /// <summary>
        /// Handles position slider value changes to update the position label.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The value changed event arguments.</param>
        private void sliderPosition_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            TimeSpan position = new TimeSpan((long)this.sliderPosition.Value * 10000);
            this.labelPosition.Text = string.Format("{0:h\\:mm\\:ss\\.fff}", position);
        }

        /// <summary>
        /// Handles the start of slider thumb dragging.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void sliderPosition_DragStarted(object sender, EventArgs e)
        {
            this.thumbDragging = true;
        }

        /// <summary>
        /// Handles the completion of slider thumb dragging by seeking to the selected position.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void sliderPosition_DragCompleted(object sender, EventArgs e)
        {
            this.thumbDragging = false;
            if (!this.preventMediaElementUpdate)
            {
                TimeSpan position = new TimeSpan((long)this.sliderPosition.Value * 10000);
                this.player.Position = position;
            }
        }

        /// <summary>
        /// Timer callback for periodic position updates during playback.
        /// </summary>
        /// <remarks>
        /// For seekable media, updates the position slider and label with current position.
        /// For live streams with absolute time, displays the absolute timestamp and latency statistics.
        /// Position updates are skipped while the user is dragging the slider thumb.
        /// </remarks>
        private void timer_Tick()
        {
            if (this.player.CanSeek)
            {
                // Skip updates while user is dragging the thumb
                if (this.thumbDragging) return;
                TimeSpan pos = this.player.Position;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.preventMediaElementUpdate = true;
                    this.sliderPosition.Value = (int)pos.TotalMilliseconds;
                    this.labelPosition.Text = string.Format("{0:h\\:mm\\:ss\\.fff}", pos);
                    this.preventMediaElementUpdate = false;
                });
            }
            else
            {
                // For live streams, display absolute time and latency
                if (this.player.CurrentState == Media.PlayerState.Playing && this.player.AbsolutePlaybackTime.HasValue)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var stat = this.player.CurrentStatistics;
                        try { this.labelPosition.Text = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff}{1}", this.player.AbsolutePlaybackTime.Value.ToLocalTime(), ((stat != null && stat.AverageLatency != null) ? $", latency {stat.AverageLatency.Value.TotalMilliseconds:0}±{stat.AverageJitter.Value.TotalMilliseconds:0} ms" : "")); } catch { }
                    });
                }
            }
        }

        /// <summary>
        /// Creates the media source from the URI text box if not already created.
        /// </summary>
        /// <remarks>
        /// The media source is reference-counted and can be shared between the player and recording session.
        /// </remarks>
        private void createMediaSource()
        {
            if (this.mediaSource == null)
            {
                this.mediaSource = VAST.Media.SourceFactory.Create(this.tboxUri.Text);
                if (this.mediaSource != null)
                {
                    this.mediaSource.Uri = this.tboxUri.Text;
                    this.mediaSource.AddRef();
                }
                else
                {
                    MauiProgram.VastDisplayAlert(this, "Error", $"Protocol not supported or the source is down!", "OK");
                }
            }
        }

        /// <summary>
        /// Initializes track selection controls and playback controls based on the loaded media.
        /// </summary>
        /// <remarks>
        /// Populates video and audio track pickers with available tracks from the media source.
        /// For seekable media, enables pause, rewind, fast forward, and position slider controls.
        /// Starts a timer for periodic position updates.
        /// </remarks>
        private void initializeControlsFromUri()
        {

            MainThread.BeginInvokeOnMainThread(() =>
            {

                this.preventUpdate = true;

                // Populate video track picker
                List<string> tracks = new List<string>();
                for (int i = 0; i < this.player.VideoStreamCount; ++i)
                {
                    VAST.Common.MediaType mt = this.player.GetVideoMediaType(i);
                    string s = mt.ToString().Substring(6);
                    tracks.Add(s);
                }

                if (tracks.Count > 0)
                {
                    this.pickerVideoTrack.IsEnabled = true;
                    this.pickerVideoTrack.ItemsSource = tracks;
                    // Workaround: reassign to force picker refresh
                    this.pickerVideoTrack.ItemsSource = this.pickerVideoTrack.GetItemsAsArray();
                    if (this.player.VideoStreamIndex < 0 || this.player.VideoStreamIndex > tracks.Count)
                    {
                        this.pickerVideoTrack.SelectedIndex = 0;
                    }
                    else
                    {
                        this.pickerVideoTrack.SelectedIndex = this.player.VideoStreamIndex;
                    }
                }

                // Populate audio track picker
                tracks = new List<string>();
                for (int i = 0; i < this.player.AudioStreamCount; ++i)
                {
                    VAST.Common.MediaType mt = this.player.GetAudioMediaType(i);
                    string s = mt.ToString().Substring(6);
                    tracks.Add(s);
                }

                if (tracks.Count > 0)
                {
                    this.pickerAudioTrack.IsEnabled = true;
                    this.pickerAudioTrack.ItemsSource = tracks;
                    // Workaround: reassign to force picker refresh
                    this.pickerAudioTrack.ItemsSource = this.pickerAudioTrack.GetItemsAsArray();
                    if (this.player.AudioStreamIndex < 0 || this.player.AudioStreamIndex > tracks.Count)
                    {
                        this.pickerAudioTrack.SelectedIndex = 0;
                    }
                    else
                    {
                        this.pickerAudioTrack.SelectedIndex = this.player.AudioStreamIndex;
                    }
                }

                this.labelPosition.Text = string.Empty;

                // Enable seek controls for seekable media
                if (this.player.CanSeek)
                {

                    this.btnPause.IsEnabled = true;
                    this.btnRewind.IsEnabled = true;
                    this.btnFastForward.IsEnabled = true;
                    this.sliderPosition.IsEnabled = true;

                    this.sliderPosition.Minimum = 0;
                    this.sliderPosition.Maximum = (int)this.player.Duration.TotalMilliseconds;
                    this.preventMediaElementUpdate = true;
                    this.sliderPosition.Value = 0;
                    this.preventMediaElementUpdate = false;

                }

                // Start position update timer
                this.timer = new System.Threading.Timer(e => timer_Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
                this.uriControlsInitialized = true;
                this.preventUpdate = false;

            });

        }

        /// <summary>
        /// Stops playback and resets all UI controls to their default state.
        /// </summary>
        /// <remarks>
        /// Stops the player, clears the media source (if not used by recording), disables track pickers,
        /// resets playback control buttons, and disposes the position update timer.
        /// </remarks>
        private void stop()
        {

            try
            {

                this.player.Stop();
                this.player.SourceMedia = null;

                this.pickerVideoTrack.ItemsSource = null;
                this.pickerVideoTrack.IsEnabled = false;
                this.pickerAudioTrack.ItemsSource = null;
                this.pickerAudioTrack.IsEnabled = false;

                this.btnPause.IsEnabled = false;
                this.btnPause.Text = "Pause";
                this.btnRewind.IsEnabled = false;
                this.btnFastForward.IsEnabled = false;
                this.sliderPosition.Value = 0;
                this.sliderPosition.IsEnabled = false;
                this.labelPosition.Text = string.Empty;
                this.uriControlsInitialized = false;

                if (this.timer != null)
                {
                    this.timer.Dispose();
                    this.timer = null;
                }

                if (this.mediaSource != null && this.player.SourceMedia == null && this.recordingSession == null)
                {
                    // both player and file writer stopped, release the source
                    this.mediaSource.Release();
                    this.mediaSource = null;
                }

            }
            catch (Exception ex)
            {
                MauiProgram.VastDisplayAlert(this, "Error", "Playback stop exception:\n" + ex.ToString(), "OK");
            }

        }

        /// <summary>
        /// Creates a recording session to save the current playback stream to an MP4 file.
        /// </summary>
        /// <remarks>
        /// Uses the ISO sink to write MP4 files with media data at the end for optimized seeking.
        /// The recording shares the same media source as the player for efficient resource usage.
        /// Requires the VAST_FEATURE_FILE_ISO feature flag to be enabled.
        /// </remarks>
        private void createRecordingSession()
        {

#if VAST_FEATURE_FILE_ISO

            if (string.IsNullOrEmpty(this.fileRecordingPath))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MauiProgram.VastDisplayAlert(this, "Error", "File path is not specified", "OK");
                });
                return;
            }

            // Create file sink for recording with optimized mdat placement
            VAST.File.ISO.IsoSink fileSink = new VAST.File.ISO.IsoSink(new VAST.File.ISO.ParsingParameters { WriteMediaDataLast = true });
            fileSink.Uri = System.IO.Path.Combine(this.fileRecordingPath, $"rec-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.mp4");

            // Create or reuse existing media source
            this.createMediaSource();

            // Create and start media session for recording
            VAST.Media.MediaSession recordingSession = new VAST.Media.MediaSession();
            recordingSession.AddSource(this.mediaSource);
            recordingSession.AddSink(fileSink);
            recordingSession.Start();
            this.recordingSession = recordingSession;

#endif

        }

        /// <summary>
        /// Stops the recording session and releases associated resources.
        /// </summary>
        /// <remarks>
        /// If both the player and recording session are stopped, the shared media source is released.
        /// </remarks>
        private void stopRecordingSession()
        {

            if (this.recordingSession != null)
            {
                this.recordingSession.Stop();
                this.recordingSession.Dispose();
                this.recordingSession = null;
            }

            // Release media source if no longer in use
            if (this.mediaSource != null && this.player.SourceMedia == null && this.recordingSession == null)
            {
                this.mediaSource.Release();
                this.mediaSource = null;
            }

        }

        #region ONVIF

        /// <summary>
        /// Handles the ONVIF button click event to toggle the ONVIF discovery and control panel.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// This method is defined outside of the VAST_FEATURE_ONVIF conditional block because the
        /// XAML-defined Clicked attribute requires the handler to exist at compile time regardless of
        /// feature flags. The ONVIF button itself is hidden by default in XAML and only made visible
        /// in the constructor when VAST_FEATURE_ONVIF is defined.
        /// Opening the ONVIF panel automatically closes the controls panel (and vice versa).
        /// </remarks>
        private void btnOnvif_Clicked(object sender, EventArgs e)
        {

            // Toggle ONVIF panel visibility and close the controls panel if open
            this.onvifPanel.IsVisible = !this.onvifPanel.IsVisible;
            this.controlsPanel.IsVisible = false;

            // Show/hide the transparent dismiss layer behind the panel
            this.panelDismissLayer.IsVisible = this.onvifPanel.IsVisible;

        }

#if VAST_FEATURE_ONVIF

        /// <summary>
        /// List of ONVIF devices found during network discovery.
        /// </summary>
        private List<VAST.ONVIF.DiscoveredDevice> discoveredDevices = null;

        /// <summary>
        /// The ONVIF client used to communicate with the selected camera.
        /// </summary>
        private VAST.ONVIF.IOnvifClient onvifClient = null;

        /// <summary>
        /// The camera control interface for PTZ operations.
        /// </summary>
        private VAST.Capture.ICameraControl cameraControl = null;

        /// <summary>
        /// Pan/tilt speed for PTZ camera movements.
        /// </summary>
        private float panSpeed = 1.0f;

        /// <summary>
        /// Zoom speed for PTZ camera zoom operations.
        /// </summary>
        private float zoomSpeed = 1.0f;

        /// <summary>
        /// Handles the Discover button click to search for ONVIF devices on the local network.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Performs an asynchronous ONVIF WS-Discovery broadcast with a 5-second timeout.
        /// Found devices are displayed in the device picker by their IP address.
        /// Any previously established ONVIF client connection is disposed before starting a new discovery.
        /// The Discover button is disabled during the search to prevent concurrent discoveries.
        /// </remarks>
        private void btnDiscover_Clicked(object sender, EventArgs e)
        {

            // Dispose any existing ONVIF client connection from a previous scan
            this.cleanupOnvif();

            // Reset all UI controls to their initial state
            this.pickerOnvifDevice.ItemsSource = null;
            this.pickerOnvifDevice.SelectedIndex = -1;
            this.pickerOnvifUri.ItemsSource = null;
            this.pickerOnvifUri.SelectedIndex = -1;
            this.btnScanOnvif.IsEnabled = false;
            this.btnPlayOnvifUri.IsEnabled = false;
            this.layoutPtzControls.IsVisible = false;
            this.lblOnvifState.Text = "Discovering ONVIF devices...";
            this.btnDiscover.IsEnabled = false;
            this.discoveredDevices = null;

            // Run discovery on a background thread to avoid blocking the UI
            Task.Run(async () =>
            {
                try
                {
                    // Perform WS-Discovery broadcast with 5-second timeout
                    this.discoveredDevices = await VAST.ONVIF.OnvifDiscovery.SearchAsync(5000);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (this.discoveredDevices != null && this.discoveredDevices.Count > 0)
                        {
                            // Populate the device picker with discovered IP addresses
                            var deviceNames = new List<string>();
                            foreach (var device in this.discoveredDevices)
                            {
                                deviceNames.Add(device.IPAddress.ToString());
                            }
                            this.pickerOnvifDevice.ItemsSource = deviceNames;
                            this.lblOnvifState.Text = "Select a device...";
                        }
                        else
                        {
                            this.lblOnvifState.Text = "No ONVIF devices found";
                        }
                        this.btnDiscover.IsEnabled = true;
                    });
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        this.btnDiscover.IsEnabled = true;
                        MauiProgram.VastDisplayAlert(this, "Error", $"ONVIF discovery failed:\n{ex}", "OK");
                    });
                }
            });

        }

        /// <summary>
        /// Handles device picker selection changes to reset scan results and enable the scan button.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// When a different device is selected, any existing ONVIF client connection is disposed
        /// and the URI list and PTZ controls are cleared. The user must re-scan the newly selected device.
        /// </remarks>
        private void pickerOnvifDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Dispose existing connection since the user selected a different device
            this.cleanupOnvif();

            // Clear previous scan results
            this.pickerOnvifUri.ItemsSource = null;
            this.pickerOnvifUri.SelectedIndex = -1;
            this.btnPlayOnvifUri.IsEnabled = false;
            this.layoutPtzControls.IsVisible = false;

            if (this.discoveredDevices != null && this.pickerOnvifDevice.SelectedIndex >= 0)
            {
                // Valid device selected — prompt user to enter credentials and scan
                this.lblOnvifState.Text = "Enter credentials and press Scan...";
                this.btnScanOnvif.IsEnabled = true;
            }
            else
            {
                // No device selected (picker cleared)
                this.lblOnvifState.Text = "";
                this.btnScanOnvif.IsEnabled = false;
            }
        }

        /// <summary>
        /// Handles the Scan button click to connect to the selected ONVIF device and retrieve streaming URIs.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Attempts to create an OnvifClient2 (ONVIF Profile 2) first, falling back to OnvifClient1 if that fails.
        /// On success, populates the URI picker and shows PTZ controls.
        /// </remarks>
        private void btnScanOnvif_Clicked(object sender, EventArgs e)
        {

            if (this.discoveredDevices == null || this.pickerOnvifDevice.SelectedIndex < 0) return;

            // Dispose any existing ONVIF client connection from a previous scan
            this.cleanupOnvif();

            // Reset scan results and disable controls during the scan operation
            this.pickerOnvifUri.ItemsSource = null;
            this.pickerOnvifUri.SelectedIndex = -1;
            this.btnPlayOnvifUri.IsEnabled = false;
            this.layoutPtzControls.IsVisible = false;
            this.lblOnvifState.Text = "Scanning ONVIF services...";
            this.btnDiscover.IsEnabled = false;
            this.pickerOnvifDevice.IsEnabled = false;
            this.btnScanOnvif.IsEnabled = false;

            // Capture the selected device and credentials for use on the background thread
            VAST.ONVIF.DiscoveredDevice device = this.discoveredDevices[this.pickerOnvifDevice.SelectedIndex];
            string username = this.txtOnvifUsername.Text;
            string password = this.txtOnvifPassword.Text;

            // Run ONVIF service scanning on a background thread to avoid blocking the UI
            Task.Run(async () =>
            {
                try
                {

                    // Try ONVIF Profile 2 client first (newer, preferred)
                    try
                    {
                        this.onvifClient = new VAST.ONVIF.OnvifClient2(device.OnvifAddress, username, password);
                        await this.onvifClient.OpenAsync();
                    }
                    catch (Exception)
                    {
                        // Profile 2 failed — fall back to Profile 1 (legacy) client
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            this.lblOnvifState.Text = "Profile 2 failed, trying Profile 1...";
                        });
                        try
                        {
                            this.onvifClient = new VAST.ONVIF.OnvifClient1(device.OnvifAddress, username, password);
                            await this.onvifClient.OpenAsync();
                        }
                        catch (Exception)
                        {
                            // Both profiles failed — device is unreachable or credentials are wrong
                            this.onvifClient = null;
                        }
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Re-enable discovery and device selection controls
                        this.btnDiscover.IsEnabled = true;
                        this.pickerOnvifDevice.IsEnabled = true;
                        this.btnScanOnvif.IsEnabled = true;

                        if (this.onvifClient == null)
                        {
                            this.lblOnvifState.Text = "ONVIF service unavailable or wrong credentials";
                        }
                        else
                        {
                            // Populate the URI picker with discovered streaming URIs
                            var uriList = new List<string>();
                            foreach (var uri in this.onvifClient.StreamingUris)
                            {
                                uriList.Add(uri.ToString());
                            }
                            this.pickerOnvifUri.ItemsSource = uriList;

                            // Auto-select the first URI if available
                            if (uriList.Count > 0)
                            {
                                this.pickerOnvifUri.SelectedIndex = 0;
                                this.btnPlayOnvifUri.IsEnabled = true;
                            }

                            this.lblOnvifState.Text = "Select URI and press Play";

                            // Show PTZ controls and enable/disable based on camera capabilities
                            this.layoutPtzControls.IsVisible = true;
                            this.updateOnvifUI();
                        }
                    });

                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Re-enable controls on failure so the user can retry
                        this.btnDiscover.IsEnabled = true;
                        this.pickerOnvifDevice.IsEnabled = true;
                        this.btnScanOnvif.IsEnabled = true;
                        MauiProgram.VastDisplayAlert(this, "Error", $"ONVIF scan failed:\n{ex}", "OK");
                    });
                }
            });

        }

        /// <summary>
        /// Handles the Play URI button click to start playback of the selected ONVIF stream.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Constructs a URI with embedded credentials if username and password are provided,
        /// stops any existing playback, sets the URI, and starts playback.
        /// </remarks>
        private void btnPlayOnvifUri_Clicked(object sender, EventArgs e)
        {

            if (this.onvifClient == null || this.pickerOnvifUri.SelectedIndex < 0) return;

            // Get the raw streaming URI from the ONVIF client
            string uri = this.onvifClient.StreamingUris[this.pickerOnvifUri.SelectedIndex].Uri;

            // Embed ONVIF credentials into the URI if both username and password are provided.
            // This allows the RTSP/HTTP client to authenticate without a separate credential prompt.
            // Credentials are URL-encoded to handle special characters safely.
            if (!string.IsNullOrEmpty(this.txtOnvifUsername.Text) && !string.IsNullOrEmpty(this.txtOnvifPassword.Text))
            {
                Uri u = new Uri(uri);
                uri = $"{u.Scheme}://{Uri.EscapeDataString(this.txtOnvifUsername.Text)}:{Uri.EscapeDataString(this.txtOnvifPassword.Text)}@{u.Host}{((u.Port == 0) ? "" : (":" + u.Port.ToString()))}{u.PathAndQuery}";
            }

            // Stop any existing playback, set the URI, and start playing
            this.stop();
            this.tboxUri.Text = uri;
            this.btnPlay_Clicked(null, null);

            // Close the ONVIF panel so the user can see the video playback
            this.onvifPanel.IsVisible = false;
            this.panelDismissLayer.IsVisible = false;

        }

        /// <summary>
        /// Handles the Zoom In button press to start continuous zoom-in at the configured speed.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// PTZ zoom uses a continuous motion model: pressing the button starts the movement
        /// and releasing the button stops it. The camera zooms at <see cref="zoomSpeed"/> rate.
        /// Positive speed values zoom in, negative values zoom out.
        /// </remarks>
        private void btnZoomIn_Pressed(object sender, EventArgs e)
        {
            if (this.cameraControl != null && this.cameraControl.IsZoomSpeedSupported)
                this.cameraControl.SetZoomSpeed(this.zoomSpeed);
        }

        /// <summary>
        /// Handles the Zoom In button release to stop the continuous zoom movement.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnZoomIn_Released(object sender, EventArgs e)
        {
            if (this.cameraControl != null)
                this.cameraControl.StopSmoothZoom();
        }

        /// <summary>
        /// Handles the Zoom Out button press to start continuous zoom-out at the configured speed.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Uses a negative zoom speed to zoom out. See <see cref="btnZoomIn_Pressed"/> for the zoom model.
        /// </remarks>
        private void btnZoomOut_Pressed(object sender, EventArgs e)
        {
            if (this.cameraControl != null && this.cameraControl.IsZoomSpeedSupported)
                this.cameraControl.SetZoomSpeed(-this.zoomSpeed);
        }

        /// <summary>
        /// Handles the Zoom Out button release to stop the continuous zoom movement.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnZoomOut_Released(object sender, EventArgs e)
        {
            if (this.cameraControl != null)
                this.cameraControl.StopSmoothZoom();
        }

        /// <summary>
        /// Handles the Move Left button press to start continuous pan to the left.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// PTZ pan/tilt uses a continuous motion model: pressing the button starts the movement
        /// and releasing the button stops it. The first parameter is pan speed (positive = left),
        /// the second parameter is tilt speed (positive = up).
        /// </remarks>
        private void btnMoveLeft_Pressed(object sender, EventArgs e)
        {
            // Positive pan speed moves the camera to the left
            if (this.cameraControl != null && this.cameraControl.IsPanTiltSupported)
                this.cameraControl.SetPanTiltSpeed(this.panSpeed, 0);
        }

        /// <summary>
        /// Handles the Move Left button release to stop the continuous pan movement.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnMoveLeft_Released(object sender, EventArgs e)
        {
            if (this.cameraControl != null)
                this.cameraControl.StopPanTilt();
        }

        /// <summary>
        /// Handles the Move Right button press to start continuous pan to the right.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Negative pan speed moves the camera to the right. See <see cref="btnMoveLeft_Pressed"/> for the pan/tilt model.
        /// </remarks>
        private void btnMoveRight_Pressed(object sender, EventArgs e)
        {
            // Negative pan speed moves the camera to the right
            if (this.cameraControl != null && this.cameraControl.IsPanTiltSupported)
                this.cameraControl.SetPanTiltSpeed(-this.panSpeed, 0);
        }

        /// <summary>
        /// Handles the Move Right button release to stop the continuous pan movement.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnMoveRight_Released(object sender, EventArgs e)
        {
            if (this.cameraControl != null)
                this.cameraControl.StopPanTilt();
        }

        /// <summary>
        /// Handles the Move Up button press to start continuous tilt upward.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Positive tilt speed tilts the camera upward. See <see cref="btnMoveLeft_Pressed"/> for the pan/tilt model.
        /// </remarks>
        private void btnMoveUp_Pressed(object sender, EventArgs e)
        {
            // Positive tilt speed tilts the camera upward
            if (this.cameraControl != null && this.cameraControl.IsPanTiltSupported)
                this.cameraControl.SetPanTiltSpeed(0, this.panSpeed);
        }

        /// <summary>
        /// Handles the Move Up button release to stop the continuous tilt movement.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnMoveUp_Released(object sender, EventArgs e)
        {
            if (this.cameraControl != null)
                this.cameraControl.StopPanTilt();
        }

        /// <summary>
        /// Handles the Move Down button press to start continuous tilt downward.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Negative tilt speed tilts the camera downward. See <see cref="btnMoveLeft_Pressed"/> for the pan/tilt model.
        /// </remarks>
        private void btnMoveDown_Pressed(object sender, EventArgs e)
        {
            // Negative tilt speed tilts the camera downward
            if (this.cameraControl != null && this.cameraControl.IsPanTiltSupported)
                this.cameraControl.SetPanTiltSpeed(0, -this.panSpeed);
        }

        /// <summary>
        /// Handles the Move Down button release to stop the continuous tilt movement.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void btnMoveDown_Released(object sender, EventArgs e)
        {
            if (this.cameraControl != null)
                this.cameraControl.StopPanTilt();
        }

        /// <summary>
        /// Handles the Store Home button click to save the current PTZ position as the home preset.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// The home preset uses the special index -1 in the ONVIF preset API.
        /// </remarks>
        private void btnSaveHome_Clicked(object sender, EventArgs e)
        {
            // Index -1 is the reserved home preset position
            if (this.cameraControl != null && this.cameraControl.IsPresetSupported)
                this.cameraControl.StorePreset(-1);
        }

        /// <summary>
        /// Handles the Recall Home button click to move the camera to the home preset position.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// The camera moves to the home position at the configured <see cref="panSpeed"/>.
        /// The home preset uses the special index -1 in the ONVIF preset API.
        /// </remarks>
        private void btnRecallHome_Clicked(object sender, EventArgs e)
        {
            // Index -1 recalls the home preset; panSpeed controls how fast the camera moves there
            if (this.cameraControl != null && this.cameraControl.IsPresetSupported)
                this.cameraControl.RecallPreset(-1, this.panSpeed);
        }

        /// <summary>
        /// Handles the Store Preset button click to save the current PTZ position as a numbered preset.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// The preset number is parsed from the <c>txtPresetNumber</c> text entry.
        /// If the text is not a valid integer, the store operation is silently skipped.
        /// </remarks>
        private void btnSavePreset_Clicked(object sender, EventArgs e)
        {
            if (this.cameraControl != null && this.cameraControl.IsPresetSupported)
            {
                // Parse preset number from the text entry; skip silently if not a valid integer
                if (int.TryParse(this.txtPresetNumber.Text, out int preset))
                    this.cameraControl.StorePreset(preset);
            }
        }

        /// <summary>
        /// Handles the Recall Preset button click to move the camera to a numbered preset position.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// The preset number is parsed from the <c>txtPresetNumber</c> text entry.
        /// The camera moves to the preset position at the configured <see cref="panSpeed"/>.
        /// If the text is not a valid integer, the recall operation is silently skipped.
        /// </remarks>
        private void btnRecallPreset_Clicked(object sender, EventArgs e)
        {
            if (this.cameraControl != null && this.cameraControl.IsPresetSupported)
            {
                // Parse preset number from the text entry; skip silently if not a valid integer
                if (int.TryParse(this.txtPresetNumber.Text, out int preset))
                    this.cameraControl.RecallPreset(preset, this.panSpeed);
            }
        }

        /// <summary>
        /// Handles the ONVIF Snapshot button click to capture a JPEG image from the camera via ONVIF.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Saves the snapshot as a JPEG file with a timestamp-based filename to the recording path.
        /// </remarks>
        private void btnOnvifSnapshot_Clicked(object sender, EventArgs e)
        {

            if (this.onvifClient == null) return;

            // Verify that the recording path is configured (platform-specific, set in constructor)
            if (string.IsNullOrEmpty(this.fileRecordingPath))
            {
                MauiProgram.VastDisplayAlert(this, "Error", "File path is not specified", "OK");
                return;
            }

            // Generate snapshot filename with timestamp to avoid overwrites
            string snapshotPath = System.IO.Path.Combine(this.fileRecordingPath, $"onvif-snapshot-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.jpg");

            // Capture the snapshot on a background thread to avoid blocking the UI
            Task.Run(async () =>
            {
                try
                {
                    // Request a JPEG snapshot from the camera via the ONVIF client
                    using (var jpegStream = await this.onvifClient.TakeSnapshot())
                    {
                        if (jpegStream != null)
                        {
                            // Save the JPEG stream to disk
                            using (var fs = new System.IO.FileStream(snapshotPath, System.IO.FileMode.OpenOrCreate))
                            {
                                jpegStream.Position = 0;
                                await jpegStream.CopyToAsync(fs);
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    MauiProgram.VastDisplayAlert(this, "Information", $"Snapshot saved to {snapshotPath}", "OK");
                                });
                            }
                        }
                        else
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                MauiProgram.VastDisplayAlert(this, "Error", "Failed to obtain ONVIF snapshot", "OK");
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        MauiProgram.VastDisplayAlert(this, "Error", $"Failed to obtain snapshot:\n{ex}", "OK");
                    });
                }
            });

        }

        /// <summary>
        /// Updates the enabled state of PTZ controls based on camera capabilities.
        /// </summary>
        /// <remarks>
        /// Queries the ONVIF client's camera control interface and enables/disables
        /// zoom, pan/tilt, preset, and snapshot buttons accordingly.
        /// </remarks>
        private void updateOnvifUI()
        {
            if (this.onvifClient == null)
            {
                this.layoutPtzControls.IsVisible = false;
                return;
            }

            // Obtain the camera control interface from the ONVIF client (may be null for non-PTZ cameras)
            this.cameraControl = this.onvifClient.CameraControl;
            if (this.cameraControl == null)
            {
                // Camera does not support PTZ — disable all PTZ-related buttons
                this.btnZoomIn.IsEnabled = false;
                this.btnZoomOut.IsEnabled = false;
                this.btnMoveLeft.IsEnabled = false;
                this.btnMoveUp.IsEnabled = false;
                this.btnMoveRight.IsEnabled = false;
                this.btnMoveDown.IsEnabled = false;
                this.btnSaveHome.IsEnabled = false;
                this.btnRecallHome.IsEnabled = false;
                this.btnSavePreset.IsEnabled = false;
                this.btnRecallPreset.IsEnabled = false;
                this.btnOnvifSnapshot.IsEnabled = false;
            }
            else
            {
                // Enable zoom buttons only if the camera supports zoom
                this.btnZoomIn.IsEnabled = this.cameraControl.IsZoomSupported;
                this.btnZoomOut.IsEnabled = this.cameraControl.IsZoomSupported;

                // Enable pan/tilt buttons only if the camera supports pan/tilt
                this.btnMoveLeft.IsEnabled = this.cameraControl.IsPanTiltSupported;
                this.btnMoveUp.IsEnabled = this.cameraControl.IsPanTiltSupported;
                this.btnMoveRight.IsEnabled = this.cameraControl.IsPanTiltSupported;
                this.btnMoveDown.IsEnabled = this.cameraControl.IsPanTiltSupported;

                // Enable preset buttons only if the camera supports presets
                this.btnSaveHome.IsEnabled = this.cameraControl.IsPresetSupported;
                this.btnRecallHome.IsEnabled = this.cameraControl.IsPresetSupported;
                this.btnSavePreset.IsEnabled = this.cameraControl.IsPresetSupported;
                this.btnRecallPreset.IsEnabled = this.cameraControl.IsPresetSupported;

                // Snapshot is always available when the ONVIF client is connected
                this.btnOnvifSnapshot.IsEnabled = true;
            }
        }

        /// <summary>
        /// Disposes the ONVIF client and releases camera control resources.
        /// </summary>
        private void cleanupOnvif()
        {
            if (this.onvifClient != null)
            {
                this.onvifClient.Dispose();
                this.onvifClient = null;
                this.cameraControl = null;
            }
        }

#endif

        #endregion

    }

}
