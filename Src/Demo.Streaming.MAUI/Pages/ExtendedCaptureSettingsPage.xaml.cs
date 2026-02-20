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

    using System.Collections;
    using System.Linq;
    using System.Runtime.Versioning;

    /// <summary>
    /// Modal settings page for the extended capture demo.
    /// Contains device selection, encoding parameters, rendering options, and server URI configuration.
    /// </summary>
    /// <remarks>
    /// This page is presented modally from <see cref="ExtendedCapturePage"/> when the user taps Settings.
    /// A new instance is created each time to work around Android picker state restoration issues.
    /// State from the previous instance is copied via <see cref="InitializeFrom"/> and device picker
    /// state is deferred to <see cref="OnAppearing"/> because Android pickers don't work correctly
    /// when ItemsSource/SelectedIndex are set before the page is in the visual tree.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("ios14.2")]
    [SupportedOSPlatform("maccatalyst13.1")]
    [SupportedOSPlatform("android")]
    public partial class ExtendedCaptureSettingsPage : ContentPage
    {

        /// <summary>
        /// List of available video capture devices discovered during enumeration.
        /// </summary>
        private List<VAST.Capture.VideoCaptureDeviceDescriptor> videoDevices = null;

        /// <summary>
        /// The currently selected video capture device, or null if "No Video" is selected.
        /// </summary>
        private VAST.Capture.VideoCaptureDeviceDescriptor videoDevice = null;

        /// <summary>
        /// The currently selected video capture mode (resolution, framerate, pixel format).
        /// </summary>
        private VAST.Capture.VideoCaptureMode videoCaptureMode = null;

        /// <summary>
        /// List of available audio capture devices discovered during enumeration.
        /// </summary>
        private List<VAST.Capture.AudioCaptureDeviceDescriptor> audioDevices = null;

        /// <summary>
        /// The currently selected audio capture device, or null if "No Audio" is selected.
        /// </summary>
        private VAST.Capture.AudioCaptureDeviceDescriptor audioDevice = null;

        /// <summary>
        /// The currently selected audio capture mode (sample rate, channels, sample format).
        /// </summary>
        private VAST.Capture.AudioCaptureMode audioCaptureMode = null;

        // Deferred state for device pickers (applied in OnAppearing for Android compatibility).
        // On Android, setting ItemsSource/SelectedIndex on Picker controls before the page
        // is in the visual tree causes the values to be silently ignored. These fields store
        // the picker state from the previous settings page instance so it can be applied
        // once the page becomes visible.
        private IList _savedVideoDeviceItems;
        private int _savedVideoDeviceIndex = -1;
        private bool _savedVideoDeviceEnabled;
        private bool _savedVideoCaptureModeEnabled;
        private int _savedVideoCaptureModeIndex = -1;
        private IList _savedAudioDeviceItems;
        private int _savedAudioDeviceIndex = -1;
        private bool _savedAudioDeviceEnabled;
        private bool _savedAudioCaptureModeEnabled;
        private int _savedAudioCaptureModeIndex = -1;
        private bool _hasDeferredState = false;

        /// <summary>
        /// Creates a new instance of <see cref="ExtendedCaptureSettingsPage"/>.
        /// </summary>
        /// <remarks>
        /// Initializes picker controls with default values:
        /// - Rendering: Low Latency strategy, Mixed Output source
        /// - Video: Baseline profile, Auto level, 44100 Hz sample rate, 2 channels
        /// - Encoding framework options differ by platform: Windows offers Media Foundation,
        ///   FFmpeg, and Nvidia CUDA; other platforms offer Builtin and FFmpeg.
        /// </remarks>
        public ExtendedCaptureSettingsPage()
        {

            InitializeComponent();

            // Set default picker selections
            this.pickerRenderingStrategy.SelectedIndex = 0;   // Low Latency
            this.pickerRenderingSource.SelectedIndex = 1;     // Mixed Output
            this.pickerVideoProfile.SelectedIndex = 1;        // Baseline
            this.pickerVideoLevel.SelectedIndex = 0;          // Auto
            this.pickerAudioSampleRate.SelectedIndex = 1;     // 44100 Hz
            this.pickerAudioChannels.SelectedIndex = 1;       // 2 (stereo)

            // Configure encoding framework options based on platform capabilities
            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {
                // Windows: Media Foundation (native), FFmpeg (software), Nvidia CUDA (GPU)
                this.pickerEncodingFramework.ItemsSource = new List<string> { "Media Foundation", "FFmpeg", "Nvidia" };
                this.pickerEncodingFramework.SelectedIndex = 0;
                this.layoutEncodingFramework.IsVisible = true;
            }
            else
            {
                // iOS/Android/Mac: Builtin (platform default), FFmpeg (software)
                this.pickerEncodingFramework.ItemsSource = new List<string> { "Builtin", "FFmpeg" };
                this.pickerEncodingFramework.SelectedIndex = 0;
                this.layoutEncodingFramework.IsVisible = true;
            }

        }

        /// <summary>
        /// Called when the page becomes visible. Applies deferred device picker state if available.
        /// </summary>
        /// <remarks>
        /// On Android, Picker controls ignore ItemsSource and SelectedIndex changes when
        /// the page is not yet in the visual tree. This override applies the saved state
        /// from <see cref="InitializeFrom"/> once the page is actually visible.
        /// Setting pickerVideoDevice.SelectedIndex triggers its SelectedIndexChanged handler,
        /// which populates the capture mode picker — so the saved capture mode index must be
        /// set after the device index to ensure the mode list is available.
        /// </remarks>
        protected override void OnAppearing()
        {

            base.OnAppearing();

            if (this._hasDeferredState)
            {

                this._hasDeferredState = false;

                // Apply video device picker state
                this.pickerVideoDevice.ItemsSource = this._savedVideoDeviceItems;
                this.pickerVideoDevice.IsEnabled = this._savedVideoDeviceEnabled;
                this.pickerVideoCaptureMode.IsEnabled = this._savedVideoCaptureModeEnabled;
                this.pickerVideoDevice.SelectedIndex = this._savedVideoDeviceIndex;
                // Video capture mode picker is populated by the SelectedIndexChanged handler
                // triggered above, now set the saved capture mode index
                this.pickerVideoCaptureMode.SelectedIndex = this._savedVideoCaptureModeIndex;

                // Apply audio device picker state
                this.pickerAudioDevice.ItemsSource = this._savedAudioDeviceItems;
                this.pickerAudioDevice.IsEnabled = this._savedAudioDeviceEnabled;
                this.pickerAudioCaptureMode.IsEnabled = this._savedAudioCaptureModeEnabled;
                this.pickerAudioDevice.SelectedIndex = this._savedAudioDeviceIndex;
                this.pickerAudioCaptureMode.SelectedIndex = this._savedAudioCaptureModeIndex;

            }

        }

        #region Public properties for the main page to read

        /// <summary>Gets the currently selected video capture device descriptor, or null if no video device is selected.</summary>
        public VAST.Capture.VideoCaptureDeviceDescriptor VideoDevice => this.videoDevice;

        /// <summary>Gets the currently selected video capture mode (resolution, framerate, pixel format).</summary>
        public VAST.Capture.VideoCaptureMode VideoCaptureMode => this.videoCaptureMode;

        /// <summary>Gets the currently selected audio capture device descriptor, or null if no audio device is selected.</summary>
        public VAST.Capture.AudioCaptureDeviceDescriptor AudioDevice => this.audioDevice;

        /// <summary>Gets the currently selected audio capture mode (sample rate, channels, sample format).</summary>
        public VAST.Capture.AudioCaptureMode AudioCaptureMode => this.audioCaptureMode;

        /// <summary>Gets the selected rendering strategy index (0 = Low Latency, 1 = Smooth).</summary>
        public int RenderingStrategyIndex => this.pickerRenderingStrategy.SelectedIndex;

        /// <summary>Gets the selected rendering source index (0 = Camera, 1 = Mixed Output).</summary>
        public int RenderingSourceIndex => this.pickerRenderingSource.SelectedIndex;

        /// <summary>Gets whether video output is enabled.</summary>
        public bool OutputVideoEnabled => this.checkBoxOutputVideo.IsChecked;

        /// <summary>Gets whether audio output is enabled.</summary>
        public bool OutputAudioEnabled => this.checkBoxOutputAudio.IsChecked;

        /// <summary>Gets the selected encoding framework name (e.g., "Media Foundation", "FFmpeg", "Nvidia", "Builtin").</summary>
        public string EncodingFramework => (string)this.pickerEncodingFramework.SelectedItem;

        /// <summary>Gets whether hardware acceleration is enabled for encoding.</summary>
        public bool HardwareAcceleration => this.checkBoxHardwareAcceleration.IsChecked;

        /// <summary>Gets the output video width as a string (parsed to int by the caller).</summary>
        public string VideoWidth => this.tboxVideoWidth.Text;

        /// <summary>Gets the output video height as a string (parsed to int by the caller).</summary>
        public string VideoHeight => this.tboxVideoHeight.Text;

        /// <summary>Gets the output video bitrate in bits per second as a string.</summary>
        public string VideoBitrate => this.tboxVideoBitrate.Text;

        /// <summary>Gets the output video framerate as a string (parsed to int by the caller).</summary>
        public string VideoFramerate => this.tboxVideoFramerate.Text;

        /// <summary>Gets the keyframe interval in frames as a string.</summary>
        public string VideoKeyframeInterval => this.tboxVideoKeyframeInterval.Text;

        /// <summary>Gets the selected H.264 profile index (0 = Auto, 1 = Baseline, 2 = Main, 3 = High).</summary>
        public int VideoProfileIndex => this.pickerVideoProfile.SelectedIndex;

        /// <summary>Gets the selected H.264 level index (0 = Auto, 1+ = specific levels).</summary>
        public int VideoLevelIndex => this.pickerVideoLevel.SelectedIndex;

        /// <summary>Gets the selected H.264 level as a string (e.g., "3.1", "4.0"), or "Auto".</summary>
        public string VideoLevelValue => (string)this.pickerVideoLevel.SelectedItem;

        /// <summary>Gets the output audio bitrate in bits per second as a string.</summary>
        public string AudioBitrate => this.tboxAudioBitrate.Text;

        /// <summary>Gets the selected audio sample rate as a string (e.g., "48000", "44100").</summary>
        public string AudioSampleRate => (string)this.pickerAudioSampleRate.SelectedItem;

        /// <summary>Gets the selected number of audio channels as a string ("1" or "2").</summary>
        public string AudioChannels => (string)this.pickerAudioChannels.SelectedItem;

        /// <summary>Gets the server URI for streaming output.</summary>
        public string ServerUri => this.tboxServerUri.Text;

        /// <summary>Sets the default server URI displayed in the text box on first load.</summary>
        public string DefaultUri { set { this.tboxServerUri.Text = value; } }

        #endregion

        /// <summary>
        /// Enumerates available video and audio capture devices and populates the device picker controls.
        /// </summary>
        /// <returns>A task representing the asynchronous enumeration operation.</returns>
        /// <remarks>
        /// Called from a background thread during page initialization. Only runs once per instance —
        /// subsequent calls return immediately if devices have already been enumerated.
        /// Each device name is prefixed with its media framework tag (e.g., [MF], [DS], [NDI], [WASAPI])
        /// to help identify the capture API being used.
        /// The first item in each picker is always "No Video" or "No Audio" to allow disabling capture.
        /// On mobile, the "Front Camera" is auto-selected if available; otherwise the first device is chosen.
        /// </remarks>
        public async Task EnumerateDevices()
        {

            // Early return if already enumerated (prevents re-enumeration on settings reopen)
            if (this.videoDevices != null)
                return;

            // Enumerate video capture devices across all available frameworks
            this.videoDevices = await VAST.Capture.VideoDeviceEnumerator.Enumerate(VAST.Common.MediaFramework.Unknown);
            if (this.videoDevices.Count > 0)
            {

                // Build display names with framework prefix tags
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

                    // Auto-select "Front Camera" on mobile, otherwise first device
                    int selectedIndex = -1;
                    for (int i = 0; i < this.videoDevices.Count; ++i)
                    {
                        if (this.videoDevices[i].Name == "Front Camera")
                        {
                            selectedIndex = i + 1; // +1 for "No Video" entry
                            break;
                        }
                    }

                    if (selectedIndex < 0) selectedIndex = 1;
                    this.pickerVideoDevice.SelectedIndex = selectedIndex;

                });

            }
            else
            {
                // No video devices found — disable pickers
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.pickerVideoDevice.ItemsSource = new List<string> { "No Video" };
                    this.pickerVideoDevice.ItemsSource = this.pickerVideoDevice.GetItemsAsArray();
                    this.pickerVideoDevice.IsEnabled = false;
                    this.pickerVideoCaptureMode.IsEnabled = false;
                });
            }

            // Enumerate audio capture devices across all available frameworks
            this.audioDevices = await VAST.Capture.AudioDeviceEnumerator.Enumerate(VAST.Common.MediaFramework.Unknown);
            if (this.audioDevices.Count > 0)
            {

                // Build display names with framework prefix tags
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
                // No audio devices found — disable pickers
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
        /// Handles video device picker selection changes.
        /// Updates the video device descriptor and populates available capture modes for the selected device.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Index 0 in the picker represents "No Video", so the device list index is offset by -1.
        /// When a device is selected, its available capture modes are listed with resolution, framerate,
        /// and pixel format. The default selection prefers modes up to 1080p to avoid overloading
        /// mobile devices with 4K capture.
        /// </remarks>
        private void pickerVideoDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Offset by -1 because index 0 is the "No Video" entry
            int index = this.pickerVideoDevice.SelectedIndex - 1;
            this.videoDevice = (index >= 0 && this.videoDevices != null && this.videoDevices.Count > index) ? this.videoDevices[index] : null;
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
                // No device selected or device has no modes — clear the mode picker
                this.pickerVideoCaptureMode.ItemsSource = new List<string>();
                this.pickerVideoCaptureMode.ItemsSource = this.pickerVideoCaptureMode.GetItemsAsArray();
                this.pickerVideoCaptureMode.SelectedIndex = -1;
            }
        }

        /// <summary>
        /// Handles video capture mode picker selection changes.
        /// Updates the selected capture mode descriptor used by the main page for capture source creation.
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
        /// Handles audio device picker selection changes.
        /// Updates the audio device descriptor and populates available capture modes for the selected device.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <remarks>
        /// Index 0 in the picker represents "No Audio", so the device list index is offset by -1.
        /// Each capture mode is displayed with sample rate, channel count, and sample format.
        /// The device's default capture mode is auto-selected.
        /// </remarks>
        private void pickerAudioDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Offset by -1 because index 0 is the "No Audio" entry
            int index = this.pickerAudioDevice.SelectedIndex - 1;
            this.audioDevice = (index >= 0 && this.audioDevices != null && this.audioDevices.Count > index) ? this.audioDevices[index] : null;
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
                // No device selected or device has no modes — clear the mode picker
                this.pickerAudioCaptureMode.ItemsSource = new List<string>();
                this.pickerAudioCaptureMode.ItemsSource = this.pickerAudioCaptureMode.GetItemsAsArray();
                this.pickerAudioCaptureMode.SelectedIndex = -1;
            }
        }

        /// <summary>
        /// Handles audio capture mode picker selection changes.
        /// Updates the selected capture mode descriptor used by the main page for capture source creation.
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
        /// Copies all settings from another settings page instance into this one.
        /// </summary>
        /// <param name="other">The previous settings page instance to copy state from.</param>
        /// <remarks>
        /// Device picker state (ItemsSource, SelectedIndex, IsEnabled) is saved to deferred fields
        /// rather than applied directly, because on Android these values are silently ignored when
        /// set before the page is in the visual tree. The deferred state is applied in
        /// <see cref="OnAppearing"/> once the page becomes visible.
        ///
        /// All other settings (encoding parameters, rendering options, server URI, checkboxes)
        /// are copied directly since XAML-defined pickers and entries with static ItemsSource
        /// work correctly before the page is displayed.
        /// </remarks>
        public void InitializeFrom(ExtendedCaptureSettingsPage other)
        {

            // Copy device descriptor lists (shared, not re-enumerated)
            this.videoDevices = other.videoDevices;
            this.audioDevices = other.audioDevices;

            // Save device picker state for deferred application in OnAppearing
            // (on Android, setting ItemsSource/SelectedIndex before the page is in the visual tree doesn't work)
            this._savedVideoDeviceItems = other.pickerVideoDevice.ItemsSource;
            this._savedVideoDeviceEnabled = other.pickerVideoDevice.IsEnabled;
            this._savedVideoCaptureModeEnabled = other.pickerVideoCaptureMode.IsEnabled;
            this._savedVideoDeviceIndex = other.pickerVideoDevice.SelectedIndex;
            this._savedVideoCaptureModeIndex = other.pickerVideoCaptureMode.SelectedIndex;

            this._savedAudioDeviceItems = other.pickerAudioDevice.ItemsSource;
            this._savedAudioDeviceEnabled = other.pickerAudioDevice.IsEnabled;
            this._savedAudioCaptureModeEnabled = other.pickerAudioCaptureMode.IsEnabled;
            this._savedAudioDeviceIndex = other.pickerAudioDevice.SelectedIndex;
            this._savedAudioCaptureModeIndex = other.pickerAudioCaptureMode.SelectedIndex;

            this._hasDeferredState = true;

            // Copy rendering settings
            this.pickerRenderingStrategy.SelectedIndex = other.pickerRenderingStrategy.SelectedIndex;
            this.pickerRenderingSource.SelectedIndex = other.pickerRenderingSource.SelectedIndex;

            // Copy output enable flags
            this.checkBoxOutputVideo.IsChecked = other.checkBoxOutputVideo.IsChecked;
            this.checkBoxOutputAudio.IsChecked = other.checkBoxOutputAudio.IsChecked;

            // Copy video encoding parameters
            this.tboxVideoWidth.Text = other.tboxVideoWidth.Text;
            this.tboxVideoHeight.Text = other.tboxVideoHeight.Text;
            this.tboxVideoBitrate.Text = other.tboxVideoBitrate.Text;
            this.tboxVideoFramerate.Text = other.tboxVideoFramerate.Text;
            this.tboxVideoKeyframeInterval.Text = other.tboxVideoKeyframeInterval.Text;
            this.pickerVideoProfile.SelectedIndex = other.pickerVideoProfile.SelectedIndex;
            this.pickerVideoLevel.SelectedIndex = other.pickerVideoLevel.SelectedIndex;

            // Copy audio encoding parameters
            this.tboxAudioBitrate.Text = other.tboxAudioBitrate.Text;
            this.pickerAudioSampleRate.SelectedIndex = other.pickerAudioSampleRate.SelectedIndex;
            this.pickerAudioChannels.SelectedIndex = other.pickerAudioChannels.SelectedIndex;

            // Copy encoding framework and hardware acceleration
            this.pickerEncodingFramework.SelectedIndex = other.pickerEncodingFramework.SelectedIndex;
            this.checkBoxHardwareAcceleration.IsChecked = other.checkBoxHardwareAcceleration.IsChecked;

            // Copy server URI
            this.tboxServerUri.Text = other.tboxServerUri.Text;

        }

        /// <summary>
        /// Handles the Done button click to dismiss the modal settings page and return to the main capture page.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private async void btnDone_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }

    }

}
