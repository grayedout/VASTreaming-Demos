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

    class FileWriterSample2 : IDisposable
    {

        private volatile bool running = false;
        private Task pushingTask = null;
        private string filePath = null;

        public FileWriterSample2(string filePath)
        {
            this.filePath = filePath;
            this.running = true;
            this.pushingTask = Task.Run(() => pushingRoutine());
        }

        ~FileWriterSample2()
        {
            Dispose();
        }

        public void Dispose()
        {
            this.running = true;
            lock (this)
            {
                if (this.pushingTask != null)
                {
                    this.pushingTask.Wait();
                    this.pushingTask = null;
                }
            }
        }

        private void pushingRoutine()
        {

            byte[] videoBuffer = new byte[Properties.Resources.h264_demo.Length];
            Properties.Resources.h264_demo.CopyTo(videoBuffer, 0);
            byte[] audioBuffer = new byte[Properties.Resources.audio.Length];
            Properties.Resources.audio.CopyTo(audioBuffer, 0);

            using (VAST.Media.MediaSession writerSession = new VAST.Media.MediaSession())
            {

                // create virtual network source
                VAST.Network.VirtualNetworkSource source = new VAST.Network.VirtualNetworkSource();

                VAST.Codecs.H264.ConfigurationParser h264Parser = new Codecs.H264.ConfigurationParser();
                h264Parser.Parse(videoBuffer);

                VAST.Common.MediaType videoMediaType = new VAST.Common.MediaType
                {
                    ContentType = VAST.Common.ContentType.Video,
                    CodecId = VAST.Common.Codec.H264,
                    Bitrate = 800000,
                    Width = h264Parser.Width,
                    Height = h264Parser.Height,
                    Framerate = h264Parser.FrameRate,
                    PixelAspectRatio = h264Parser.AspectRatio,
                };

                VAST.Codecs.H264.ConfigurationParser.GenerateCodecPrivateData(videoMediaType, videoBuffer);

                // add video media type
                int videoStreamIndex = source.AddStream(videoMediaType);

                VAST.Common.MediaType audioMediaType = new VAST.Common.MediaType
                {
                    ContentType = VAST.Common.ContentType.Audio,
                    CodecId = VAST.Common.Codec.AAC,
                    SampleRate = 44100,
                    Channels = 2,
                    Bitrate = 128000,
                };

                VAST.Codecs.AAC.ConfigurationParser.GenerateCodecPrivateData(audioMediaType);

                // add audio media type
                int audioStreamIndex = source.AddStream(audioMediaType);

                VAST.Media.IMediaSink fileSink = new VAST.File.ISO.IsoSink();
                fileSink.Uri = filePath;

                // add source and sink to a writer session and start it
                writerSession.AddSource(source);
                writerSession.AddSink(fileSink);
                writerSession.Error += (object sender, Media.ErrorEventArgs e) =>
                {
                    // TODO: process error
                };

                writerSession.Start();

                long videoFrameCount = 0;
                long audioFrameCount = 0;
                int h264BitstreamPosition = 0;
                int audioBitstreamPosition = 0;

                while (this.running && (h264BitstreamPosition < videoBuffer.Length || audioBitstreamPosition >= audioBuffer.Length))
                {

                    // video timestamp converted to 100ns units
                    long videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;
                    // audio timestamp converted to 100ns units
                    long audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                    if (h264BitstreamPosition < videoBuffer.Length)
                    {

                        // find the start of the next frame
                        int bitstreamPosition = h264BitstreamPosition + 4;
                        int startCodeSize = 0;
                        int nextNalPosition = 0;
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

                        //VAST.Common.Log.DebugFormat("Writing sample {0}, pts {1}, size {2}", VAST.Common.ContentType.Video, videoFileTime, frameSize);
                        source.PushMedia(videoStreamIndex, videoBuffer, h264BitstreamPosition, frameSize, videoFileTime, videoFileTime);

                        h264BitstreamPosition += frameSize;
                        videoFrameCount++;
                        videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;

                    }

                    if (audioBitstreamPosition < audioBuffer.Length)
                    {

                        // this loop is to keep audio stream in sync with video stream
                        // video and audio samples must be pushed with more or less matching times, eg +/- few hundred milliseconds,
                        // there must be no big differences between them
                        // and because gaps between video and audio frames are different, you have to catch up one stream to another
                        while (audioFileTime <= videoFileTime)
                        {

                            int frameSize = ((audioBuffer[audioBitstreamPosition + 3] & 0x03) << 11) | (audioBuffer[audioBitstreamPosition + 4] << 3) | ((audioBuffer[audioBitstreamPosition + 5] & 0xE0) >> 5);

                            //VAST.Common.Log.DebugFormat("Writing sample {0}, pts {1}, size {2}", VAST.Common.ContentType.Audio, videoFileTime, frameSize);
                            source.PushMedia(audioStreamIndex, audioBuffer, audioBitstreamPosition + 7, frameSize - 7, audioFileTime, audioFileTime);

                            audioBitstreamPosition += frameSize;
                            audioFrameCount++;
                            audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                        }

                    }

                }

            }

        }

    }

}
