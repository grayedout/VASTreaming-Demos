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
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    class FileReaderSample : IDisposable
    {

        private class StreamContext
        {
            public VAST.Common.MediaType InputMediaType { get; set; }
            public bool PerformDecoding { get; set; }
            public VAST.Media.IDecoder Decoder { get; set; }
            public bool FirstFrame { get; set; } = true;
        }

        private volatile bool running = true;
        private VAST.Media.IInteractiveMediaSource source = null;
        private List<StreamContext> streams = new List<StreamContext>();
        // set to false if you want to enforce CPU decoding
        private bool allowHardwareAcceleration = true;
        // set to true if you want to utilize FFmpeg for decoding
        private bool useFFmpeg = false;

        public FileReaderSample(string filePath)
        {

            this.source = new VAST.File.ISO.IsoSource(new File.ISO.ParsingParameters { PreserveNals = true });
            if (this.source == null)
            {
                throw new Exception("Unsupported protocol");
            }

            this.source.Uri = filePath;

            // read content as fast as we can
            this.source.PlaybackRate = double.MaxValue;

            this.source.NewStream += source_NewStream;
            this.source.NewSample += source_NewSample;
            this.source.Error += source_Error;
            this.source.StateChanged += source_StateChanged;

            // start opening
            this.source.Open();

            // TODO: uncomment to run image extractor
            //Task.Run(async () =>
            //{

            //    var extractor = new VAST.File.Utility.ImageExtractor();
            //    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            //    {
            //        extractor.DecoderParameters.PreferredMediaFramework = useFFmpeg ? VAST.Common.MediaFramework.FFmpeg : VAST.Common.MediaFramework.CUDA;
            //    }
            //    else
            //    {
            //        extractor.DecoderParameters.PreferredMediaFramework = useFFmpeg ? VAST.Common.MediaFramework.FFmpeg : VAST.Common.MediaFramework.Builtin;
            //    }

            //    var image = await extractor.Execute(filePath, TimeSpan.FromSeconds(30));
            //    if (image != null)
            //    {
            //        using (var stream = System.IO.File.OpenWrite(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath), "thumbnail.jpg")))
            //        {
            //            image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80).SaveTo(stream);
            //        }
            //    }

            //});

        }

        ~FileReaderSample()
        {
            Dispose();
        }

        public void Dispose()
        {

            this.running = false;
            Monitor.Enter(this);

            try
            {

                if (this.source != null)
                {
                    this.source.Error -= source_Error;
                    this.source.StateChanged -= source_StateChanged;
                    this.source.NewStream -= source_NewStream;
                    this.source.NewSample -= source_NewSample;
                    this.source.Dispose();
                    this.source = null;
                }

                foreach (var stream in this.streams)
                {
                    if (stream.Decoder != null) stream.Decoder.Dispose();
                }

                this.streams.Clear();

            }
            catch (Exception ex)
            {
                VAST.Common.Log.ErrorFormat("Unexpected exception: {0}", ex);
            }

        }

        /// <summary>
        /// Event handler is called for each media stream detected in the source
        /// you can use e.MediaType to check a detailed stream information
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void source_NewStream(object sender, Media.NewStreamEventArgs e)
        {

            while (!Monitor.TryEnter(this, 10))
            {
                if (!this.running)
                {
                    return;
                }
            }

            try
            {

                while (this.streams.Count < e.StreamCount)
                {
                    this.streams.Add(new StreamContext());
                }

                this.streams[e.StreamIndex].InputMediaType = e.MediaType;

                VAST.Media.DecoderParameters decoderParameters = new VAST.Media.DecoderParameters
                {
                    PreferredMediaFramework = useFFmpeg ? VAST.Common.MediaFramework.FFmpeg : VAST.Common.MediaFramework.Builtin,
                    AllowHardwareAcceleration = allowHardwareAcceleration,
                };

                switch (e.MediaType.ContentType)
                {

                    case VAST.Common.ContentType.Video:
                        {

                            if (e.MediaType.CodecId == VAST.Common.Codec.Uncompressed && e.MediaType.PixelFormat == VAST.Common.PixelFormat.BGRA)
                            {
                                // no decoding/pixel format conversion necessary, use received samples as is
                                this.streams[e.StreamIndex].PerformDecoding = false;
                            }
                            else
                            {

                                // decoding or pixel format conversion necessary
                                VAST.Common.MediaType desiredVideoMediaType = new VAST.Common.MediaType
                                {
                                    ContentType = VAST.Common.ContentType.Video,
                                    CodecId = VAST.Common.Codec.Uncompressed,
                                    PixelFormat = VAST.Common.PixelFormat.BGRA,
                                    Width = e.MediaType.Width,
                                    Height = e.MediaType.Height
                                };

                                this.streams[e.StreamIndex].Decoder = VAST.Media.DecoderFactory.Create(e.MediaType, desiredVideoMediaType, decoderParameters);
                                this.streams[e.StreamIndex].PerformDecoding = true;

                            }

                            break;

                        }

                    case VAST.Common.ContentType.Audio:
                        {

                            if (e.MediaType.CodecId == VAST.Common.Codec.PCM && e.MediaType.SampleFormat == VAST.Common.SampleFormat.S16)
                            {
                                // no decoding/sample format conversion necessary, use received samples as is
                                this.streams[e.StreamIndex].PerformDecoding = false;
                            }
                            else
                            {

                                VAST.Common.MediaType desiredAudioMediaType = new VAST.Common.MediaType
                                {
                                    ContentType = VAST.Common.ContentType.Audio,
                                    CodecId = VAST.Common.Codec.PCM,
                                    SampleFormat = VAST.Common.SampleFormat.S16,
                                    SampleRate = e.MediaType.SampleRate,
                                    Channels = e.MediaType.Channels
                                };

                                this.streams[e.StreamIndex].Decoder = VAST.Media.DecoderFactory.Create(e.MediaType, desiredAudioMediaType, decoderParameters);
                                this.streams[e.StreamIndex].PerformDecoding = true;

                            }

                            break;

                        }

                    default:
                        // no decoding of everything else
                        break;

                }

            }
            catch (Exception ex)
            {
                VAST.Common.Log.ErrorFormat("Failed to create decoder for stream {0}, type {1}: {2}", e.StreamIndex, e.MediaType, ex);
            }
            finally
            {
                Monitor.Exit(this);
            }

        }

        /// <summary>
        /// Event handler is called when new sample is received for one of the media streams
        /// e.Sample contains the raw sample data in e.Sample.Buffer,
        /// e.Sample.ActualSize contains the actual size of the data in e.Sample.Buffer
        /// DTS and PTS timestamps are also present in e.Sample
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void source_NewSample(object sender, VAST.Media.NewSampleEventArgs e)
        {

            while (!Monitor.TryEnter(this, 10))
            {
                if (!this.running)
                {
                    return;
                }
            }

            try
            {

                if (e.Sample.StreamIndex < 0 || e.Sample.StreamIndex >= this.streams.Count)
                {
                    // unexpected stream index
                    return;
                }

                //VAST.Common.Log.DebugFormat("Received sample {0} size {1}", e.Sample.Pts, e.Sample.ActualSize);
                VAST.Common.VersatileBuffer inputSample = e.Sample;

                StreamContext streamContext = this.streams[inputSample.StreamIndex];

                if (streamContext.PerformDecoding)
                {

                    if (streamContext.Decoder == null || !streamContext.Decoder.IsUsable)
                    {
                        // unsupported decoder
                        return;
                    }

                    if (streamContext.FirstFrame)
                    {
                        if (inputSample.KeyFrame)
                        {
                            streamContext.FirstFrame = false;
                        }
                        else
                        {
                            // drop anything till the first keyframe
                            return;
                        }
                    }

                    streamContext.Decoder.Write(inputSample);

                    while (true)
                    {

                        VAST.Common.VersatileBuffer decodedSample = streamContext.Decoder.Read();
                        if (decodedSample == null)
                        {
                            break;
                        }

                        //VAST.Common.Log.DebugFormat("Decoded sample {0} size {1}", decodedSample.Pts, decodedSample.ActualSize);
                        this.processData(decodedSample);
                        decodedSample.Release();

                    }

                }
                else
                {
                    this.processData(inputSample);
                }

            }
            catch (Exception ex)
            {
                VAST.Common.Log.ErrorFormat("Failed to receive sample of stream {0} DTS {1}: {2}", e.Sample.StreamIndex, e.Sample.Dts, ex);
            }
            finally
            {
                Monitor.Exit(this);
            }

        }

        private void processData(VAST.Common.VersatileBuffer sample)
        {
            // TODO: process decoded, uncompressed data
            // In case of video sample.Buffer contains raw pixel array which can be used to create bitmap or process data directly
            // streamContext.Decoder.OutputMediaType.PixelFormat contains pixel format of the pixel buffer.
            // In case of audio sample.Buffer contains raw PCM buffer
            // streamContext.Decoder.OutputMediaType.SampleFormat contains sample format of the PCM buffer
        }

        private void source_StateChanged(object sender, Media.MediaState e)
        {
            lock (this)
            {
                switch (e)
                {
                    case VAST.Media.MediaState.Opened:
                        // source successfully opened
                        // let's seek to the middle of the file
                        this.source.Position = new TimeSpan(this.source.Duration.Ticks / 2);
                        this.source.Start();
                        break;
                    case VAST.Media.MediaState.Started:
                        // source has been started, samples will come soon
                        break;
                    case VAST.Media.MediaState.Closed:
                        // source has been disconnected
                        break;
                }
            }
        }

        private void source_Error(object sender, Media.ErrorEventArgs e)
        {
            // source error occurred
            VAST.Common.Log.ErrorFormat("Error occurred: {0}", e.ErrorDescription);
        }

    }

}
