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

    using System;
    using System.Threading.Tasks;

    class SendSample1 : IDisposable
    {

        private volatile bool running = true;
        private Task pushingTask = null;
        private VAST.Media.IMediaSink sink = null;
        private VAST.Common.MediaType audioMediaType = null;
        private byte[] audioBuffer = null;

        public SendSample1()
        {

            this.sink = new VAST.RTSP.RtspPublisherSink();
            this.sink.Uri = "rtp://127.0.0.1:10000";
            this.sink.Error += sink_Error;
            this.sink.StateChanged += sink_StateChanged;

            audioBuffer = new byte[Properties.Resources.g711u.Length];
            Properties.Resources.g711u.CopyTo(audioBuffer, 0);

            audioMediaType = new VAST.Common.MediaType
            {
                ContentType = Common.ContentType.Audio,
                CodecId = Common.Codec.G711U,
                SampleRate = 8000,
                Channels = 1,
                Bitrate = 64000,
                FrameSize = 1280
            };

            // add audio media type
            this.sink.AddStream(0, audioMediaType);
            this.sink.Open();

        }

        ~SendSample1()
        {
            Dispose();
        }

        public void Dispose()
        {

            this.running = false;

            if (this.pushingTask != null)
            {
                this.pushingTask.Wait();
                this.pushingTask = null;
            }

            if (this.sink != null)
            {
                this.sink.Dispose();
                this.sink = null;
            }

        }

        private void pushingRoutine()
        {

            DateTime streamingStarted = DateTime.Now;
            long audioFrameCount = 0;
            int audioBitstreamPosition = 0;

            while (this.running)
            {

                // playback time in 100ns units
                long playbackTime = (long)(DateTime.Now - streamingStarted).Ticks;
                // audio timestamp converted to 100ns units
                long audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                do
                {

                    if (audioBitstreamPosition >= audioBuffer.Length)
                    {
                        // EOS
                        audioBitstreamPosition = 0;
                    }

                    if (audioFileTime <= playbackTime)
                    {

                        // prepare buffer
                        VAST.Common.VersatileBuffer packet = VAST.Media.MediaGlobal.LockBuffer(audioMediaType.FrameSize);
                        packet.Append(audioBuffer, audioBitstreamPosition, audioMediaType.FrameSize);
                        packet.Pts = packet.Dts = audioFileTime;
                        packet.KeyFrame = packet.CleanPoint = true;
                        packet.StreamIndex = 0;

                        // push sample to clients if any
                        this.sink.PushMedia(0, packet);
                        packet.Release();

                        audioBitstreamPosition += audioMediaType.FrameSize;
                        audioFrameCount++;
                        audioFileTime = audioFrameCount * 10000000L * audioMediaType.FrameSize * 8 / audioMediaType.Bitrate;

                    }

                }
                while (audioFileTime <= playbackTime);

                Task.Delay(10).Wait();

            }

        }

        private void sink_StateChanged(object sender, Media.MediaState e)
        {
            lock (this)
            {
                switch (e)
                {
                    case VAST.Media.MediaState.Opened:
                        // sink successfully opened, start it
                        this.sink.Start();
                        this.pushingTask = Task.Run(() => pushingRoutine());
                        break;
                    case VAST.Media.MediaState.Started:
                        // sink has been started
                        break;
                    case VAST.Media.MediaState.Closed:
                        // sink has been disconnected
                        break;
                }
            }
        }

        private void sink_Error(object sender, Media.ErrorEventArgs e)
        {
        }

    }

}
