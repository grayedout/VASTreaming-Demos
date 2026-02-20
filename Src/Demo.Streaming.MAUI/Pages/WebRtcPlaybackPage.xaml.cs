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
    /// Demo page for WebRTC playback functionality.
    /// Demonstrates receiving media streams via WebRTC protocol using the VASTreaming signaling client.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class WebRtcPlaybackPage : ContentPage
    {

        /// <summary>
        /// The WebRTC signaling client used to establish and manage the playback connection.
        /// </summary>
        private VastSignalingPlaybackClient signalingClient = null;

        /// <summary>
        /// Creates a new instance of <see cref="WebRtcPlaybackPage"/>.
        /// </summary>
        public WebRtcPlaybackPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handles the page navigating from event by stopping any active playback.
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
        /// Handles the play button click event to start WebRTC playback.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// If a signaling client already exists, it is stopped and disposed before creating a new one.
        /// The player is configured with automatic track selection before starting playback.
        /// </remarks>
        private void btnPlay_Clicked(object sender, EventArgs e)
        {

            // Clean up existing signaling client if present
            if (this.signalingClient != null)
            {
                this.signalingClient.Stop();
                this.signalingClient.Dispose();
                this.signalingClient = null;
            }

            // Configure player for automatic track selection
            this.player.AudioStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;
            this.player.VideoStreamIndex = VAST.Player.MediaPlayer.TRACK_AUTO;

            // Create and start the WebRTC signaling client
            this.signalingClient = new VastSignalingPlaybackClient(this.player, this.textUri.Text, this.textPublishingPath.Text);
            this.signalingClient.Error += SignalingClient_Error;
            this.signalingClient.Start();

        }

        /// <summary>
        /// Handles the stop button click event to stop WebRTC playback.
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
            if (await VAST.Common.License.SendLog("MAUI WebRTC playback issue"))
            {
                await MauiProgram.VastDisplayAlert(this, "Information", "The log has been sent successfully", "OK");
            }
            else
            {
                await MauiProgram.VastDisplayAlert(this, "Error", "Failed to send the log! Please grab it manually and send to support@vastreaming.net.", "OK");
            }
        }

        /// <summary>
        /// Handles critical errors from the signaling client by stopping playback and displaying an error message.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The error event arguments containing error details.</param>
        private void SignalingClient_Error(object sender, VAST.Media.ErrorEventArgs e)
        {
            if (e.IsCritical)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    this.stop();
                    string s = string.Format("Playback error:\n\n{0}", e.ErrorDescription);
                    await MauiProgram.VastDisplayAlert(this, "Error", s, "OK");
                });
            }
        }

        /// <summary>
        /// Stops the WebRTC playback session and releases all associated resources.
        /// </summary>
        private void stop()
        {
            try
            {
                if (this.signalingClient != null)
                {
                    this.signalingClient.Stop();
                    this.signalingClient.Dispose();
                    this.signalingClient = null;
                }
            }
            catch (Exception ex)
            {
                MauiProgram.VastDisplayAlert(this, "Error", "Playback stop exception:\n" + ex.ToString(), "OK");
            }
        }

    }

}
