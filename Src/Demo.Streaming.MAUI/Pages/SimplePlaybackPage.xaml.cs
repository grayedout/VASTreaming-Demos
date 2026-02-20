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
    /// Demo page for simple media playback functionality.
    /// Demonstrates basic media playback features including play, pause, stop, seek,
    /// fast forward, rewind, and track selection.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class SimplePlaybackPage : ContentPage
    {

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
        /// Flag to prevent recursive updates when programmatically changing track pickers.
        /// </summary>
        private bool preventUpdate = false;

        /// <summary>
        /// Creates a new instance of <see cref="SimplePlaybackPage"/>.
        /// </summary>
        public SimplePlaybackPage()
        {

            InitializeComponent();

            // Subscribe to player events
            this.player.CurrentStateChanged += player_CurrentStateChanged;
            this.player.MediaEnded += player_MediaEnded;
            this.player.MediaFailed += player_MediaFailed;

            this.tboxUri.Text = this.defaultUri;

        }

        /// <summary>
        /// Handles the page navigating from event by stopping playback.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The navigation event arguments.</param>
        private void ContentPage_NavigatingFrom(object sender, NavigatingFromEventArgs e)
        {
            this.stop();
        }

        /// <summary>
        /// Handles the Controls button click event to toggle the playback controls overlay panel.
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
        /// The dismiss layer is a full-screen transparent BoxView that sits between the video player
        /// and the overlay panel in the z-order. Tapping anywhere outside the panel closes it.
        /// </remarks>
        private void dismissPanel_Tapped(object sender, TappedEventArgs e)
        {
            this.controlsPanel.IsVisible = false;
            this.panelDismissLayer.IsVisible = false;
        }

        /// <summary>
        /// Handles the play button click event to start or resume playback.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// If playback is already active with a modified playback rate, resets to normal speed.
        /// Otherwise, starts playback from the URI specified in the text box.
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

            // Configure automatic track selection and start playback
            this.player.AudioStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;
            this.player.VideoStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;
            this.player.Source = new Uri(this.tboxUri.Text);
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

                // update video tracks
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
                    // workaround to fill picker with track list, otherwise it doesn't update
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
                    // workaround to fill picker with track list, otherwise it doesn't update
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

                this.timer = new System.Threading.Timer(e => timer_Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
                this.uriControlsInitialized = true;
                this.preventUpdate = false;

            });

        }

        /// <summary>
        /// Stops playback and resets all UI controls to their default state.
        /// </summary>
        /// <remarks>
        /// Stops the player, clears the media source, disables track pickers,
        /// resets playback control buttons, and disposes the position update timer.
        /// </remarks>
        private void stop()
        {

            try
            {

                // Stop player and clear media source
                this.player.Stop();
                this.player.SourceMedia = null;

                // Reset track pickers
                this.pickerVideoTrack.ItemsSource = null;
                this.pickerVideoTrack.IsEnabled = false;
                this.pickerAudioTrack.ItemsSource = null;
                this.pickerAudioTrack.IsEnabled = false;

                // Reset playback controls
                this.btnPause.IsEnabled = false;
                this.btnPause.Text = "Pause";
                this.btnRewind.IsEnabled = false;
                this.btnFastForward.IsEnabled = false;
                this.sliderPosition.Value = 0;
                this.sliderPosition.IsEnabled = false;
                this.labelPosition.Text = string.Empty;
                this.uriControlsInitialized = false;

                // Dispose position update timer
                if (this.timer != null)
                {
                    this.timer.Dispose();
                    this.timer = null;
                }

            }
            catch (Exception ex)
            {
                MauiProgram.VastDisplayAlert(this, "Error", "Playback stop exception:\n" + ex.ToString(), "OK");
            }

        }

    }

}
