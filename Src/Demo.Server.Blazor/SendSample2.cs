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

    class SendSample2 : IDisposable
    {

        private volatile bool running = true;
        private Task pushingTask = null;
        private VAST.Media.IMediaSink sink = null;
        private VAST.Common.MediaType videoMediaType = null;
        private VAST.Common.MediaType audioMediaType = null;
        private byte[] videoBuffer = null;
        private byte[] audioBuffer = null;
        private int videoStreamIndex = 0;
        private int audioStreamIndex = 1;

        public SendSample2()
        {

            this.videoBuffer = new byte[Properties.Resources.h264_demo.Length];
            Properties.Resources.h264_demo.CopyTo(videoBuffer, 0);
            this.audioBuffer = new byte[Properties.Resources.audio.Length];
            Properties.Resources.audio.CopyTo(audioBuffer, 0);

            VAST.Codecs.H264.ConfigurationParser h264Parser = new Codecs.H264.ConfigurationParser();
            h264Parser.Parse(this.videoBuffer);

            this.videoMediaType = new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Video,
                CodecId = VAST.Common.Codec.H264,
                Bitrate = 800000,
                Width = h264Parser.Width,
                Height = h264Parser.Height,
                Framerate = h264Parser.FrameRate,
                PixelAspectRatio = h264Parser.AspectRatio,
            };

            VAST.Codecs.H264.ConfigurationParser.GenerateCodecPrivateData(this.videoMediaType, this.videoBuffer);

            this.audioMediaType = new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Audio,
                CodecId = VAST.Common.Codec.AAC,
                SampleRate = 44100,
                Channels = 2,
                Bitrate = 128000,
            };

            VAST.Codecs.AAC.ConfigurationParser.GenerateCodecPrivateData(this.audioMediaType);

            this.sink = new VAST.RTMP.RtmpPublisherSink();
            this.sink.Uri = "rtmp://127.0.0.1/live/test";
            this.sink.Error += sink_Error;
            this.sink.StateChanged += sink_StateChanged;

            // add video media type
            this.sink.AddStream(this.videoStreamIndex, this.videoMediaType);
            // add audio media type
            this.sink.AddStream(this.audioStreamIndex, this.audioMediaType);

            this.sink.Open();

        }

        ~SendSample2()
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
            long videoFrameCount = 0;
            long audioFrameCount = 0;
            int h264BitstreamPosition = 0;
            int audioBitstreamPosition = 0;

            while (this.running)
            {

                // playback time in 100ns units
                long playbackTime = (long)(DateTime.Now - streamingStarted).Ticks;
                // video timestamp converted to 100ns units
                long videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;
                // audio timestamp converted to 100ns units
                long audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                do
                {

                    if (h264BitstreamPosition >= videoBuffer.Length)
                    {
                        // EOS
                        h264BitstreamPosition = 0;
                    }

                    if (audioBitstreamPosition >= audioBuffer.Length)
                    {
                        // EOS
                        audioBitstreamPosition = 0;
                    }

                    if (videoFileTime <= playbackTime)
                    {

                        // find the start of the next frame
                        int bitstreamPosition = h264BitstreamPosition + 4;
                        int startCodeSize = 0;
                        int nextNalPosition = 0;
                        bool keyFrame = false;
                        while ((nextNalPosition = VAST.Codecs.H264.ConfigurationParser.FindNextStartCode(videoBuffer, bitstreamPosition, videoBuffer.Length - bitstreamPosition, out startCodeSize)) >= 0)
                        {
                            VAST.Codecs.H264.NalUnitTypes nalUnit = (Codecs.H264.NalUnitTypes)(videoBuffer[nextNalPosition + startCodeSize] & 0x1F);
                            if (nalUnit == Codecs.H264.NalUnitTypes.AccessUnitDelimiter)
                            {
                                // found next frame
                                break;
                            }
                            else
                            {
                                if (nalUnit == Codecs.H264.NalUnitTypes.SliceIdr)
                                {
                                    keyFrame = true;
                                }
                                bitstreamPosition = nextNalPosition + startCodeSize;
                            }
                        }

                        int frameSize = 0;
                        if (nextNalPosition < 0)
                        {
                            // EOS
                            frameSize = videoBuffer.Length - h264BitstreamPosition;
                        }
                        else
                        {
                            frameSize = nextNalPosition - h264BitstreamPosition;
                        }

                        VAST.Common.VersatileBuffer packet = VAST.Media.MediaGlobal.LockBuffer(frameSize);
                        packet.Append(videoBuffer, h264BitstreamPosition, frameSize);
                        packet.Pts = packet.Dts = videoFileTime;
                        packet.KeyFrame = packet.CleanPoint = keyFrame;
                        packet.StreamIndex = this.videoStreamIndex;

                        this.sink.PushMedia(this.videoStreamIndex, packet);
                        packet.Release();

                        h264BitstreamPosition += frameSize;
                        videoFrameCount++;
                        videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;

                    }

                    if (audioFileTime <= playbackTime)
                    {

                        int frameSize = ((audioBuffer[audioBitstreamPosition + 3] & 0x03) << 11) | (audioBuffer[audioBitstreamPosition + 4] << 3) | ((audioBuffer[audioBitstreamPosition + 5] & 0xE0) >> 5);

                        VAST.Common.VersatileBuffer packet = VAST.Media.MediaGlobal.LockBuffer(frameSize - 7);
                        packet.Append(audioBuffer, audioBitstreamPosition + 7, frameSize - 7);
                        packet.Pts = packet.Dts = audioFileTime;
                        packet.KeyFrame = packet.CleanPoint = true;
                        packet.StreamIndex = this.audioStreamIndex;

                        this.sink.PushMedia(this.audioStreamIndex, packet);
                        packet.Release();

                        audioBitstreamPosition += frameSize;
                        audioFrameCount++;
                        audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                    }

                }
                while (videoFileTime <= playbackTime || audioFileTime <= playbackTime);

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
                        ((VAST.Media.IMediaSink)sender).Start();
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
