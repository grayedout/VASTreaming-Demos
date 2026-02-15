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
    using System.Threading;
    using System.Threading.Tasks;

    class IsoFragmentWriterSample : IDisposable
    {

        private class StreamContext
        {
            public VAST.Common.MediaType InputMediaType { get; set; }
        }

        private volatile bool running = true;
        private VAST.Media.IMediaSource source = null;
        private List<StreamContext> streams = new List<StreamContext>();
        private Queue<VAST.Common.VersatileBuffer> inputSampleQueue = new Queue<Common.VersatileBuffer>();
        private Task pushingTask = null;
        private VAST.File.ISO.IsoFormatParser writer = null;
        private System.IO.Stream initializationFragment = null;
        private System.IO.Stream currentFragmentStream = null;
        private bool waitingForKeyframe = true;

        public IsoFragmentWriterSample(string uri)
        {

            this.source = VAST.Media.SourceFactory.Create(uri);
            if (this.source == null)
            {
                throw new Exception("Unsupported protocol");
            }

            this.source.Uri = uri;
            this.source.NewSample += source_NewSample;
            this.source.Error += source_Error;
            this.source.StateChanged += source_StateChanged;

            // start opening
            this.source.Open();

            this.pushingTask = Task.Factory.StartNew(() => pushingRoutine(), TaskCreationOptions.LongRunning);

        }

        ~IsoFragmentWriterSample()
        {
            Dispose();
        }

        public void Dispose()
        {

            this.running = false;
            Monitor.Enter(this);

            try
            {

                // wait for the decoding thread to finish
                if (this.pushingTask != null)
                {
                    this.pushingTask.Wait();
                    this.pushingTask = null;
                }

                if (this.source != null)
                {
                    this.source.Error -= source_Error;
                    this.source.StateChanged -= source_StateChanged;
                    this.source.NewSample -= source_NewSample;
                    this.source.Dispose();
                    this.source = null;
                }

                if (this.writer != null)
                {
                    this.writer.Dispose();
                    this.writer = null;
                }

                if (this.initializationFragment != null)
                {
                    this.initializationFragment.Dispose();
                    this.initializationFragment = null;
                }

                if (this.currentFragmentStream != null)
                {
                    this.currentFragmentStream.Dispose();
                    this.currentFragmentStream = null;
                }

                while (this.inputSampleQueue.Count > 0)
                {
                    this.inputSampleQueue.Dequeue().Release();
                }

            }
            catch (Exception ex)
            {
                VAST.Common.Log.ErrorFormat("Unexpected exception: {0}", ex);
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

                this.inputSampleQueue.Enqueue(e.Sample);
                e.Sample.AddRef();

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

        private void source_StateChanged(object sender, Media.MediaState e)
        {

            lock (this)
            {

                switch (e)
                {

                    case VAST.Media.MediaState.Opened:
                        {

                            // source successfully opened

                            // initialize fragment writer
                            this.writer = new VAST.File.ISO.IsoFormatParser(false);
                            this.writer.Parameters = new VAST.File.ISO.ParsingParameters { UseSegments = true };

                            // set output stream
                            var outputStream = new VAST.Common.VersatileBufferStream { AutoGrowth = true };
                            outputStream.AutoGrowth = true;
                            this.writer.SetOutputStream(outputStream);

                            // prepare stream descriptor
                            var sd = new List<VAST.Common.MediaType>();
                            for (int i = 0; i < this.source.StreamCount; i++)
                            {
                                VAST.Common.MediaType mt = this.source.GetMediaType(i);
                                this.streams.Add(new StreamContext { InputMediaType = mt });
                                sd.Add(mt);
                            }
                            this.writer.StreamDescriptor = sd;

                            // flush initialization fragment
                            this.writer.Flush();
                            this.initializationFragment = outputStream;
                            this.initializationFragment.SetLength(this.initializationFragment.Position);
                            this.initializationFragment.Seek(0, System.IO.SeekOrigin.Begin);

                            // TODO: process initializationFragment accordingly

                            // call start to start receiving samples
                            this.source.Start();
                            break;

                        }

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

        private async void pushingRoutine()
        {

            while (this.running)
            {

                VAST.Common.VersatileBuffer originalInputSample = null;

                while (!Monitor.TryEnter(this, 10))
                {
                    if (!this.running)
                    {
                        return;
                    }
                }

                try
                {
                    if (this.inputSampleQueue.Count > 0)
                    {
                        originalInputSample = this.inputSampleQueue.Dequeue();
                    }
                }
                finally
                {
                    Monitor.Exit(this);
                }

                if (originalInputSample != null)
                {

                    try
                    {

                        StreamContext streamContext = this.streams[originalInputSample.StreamIndex];
                        if (this.waitingForKeyframe)
                        {
                            if (originalInputSample.KeyFrame && streamContext.InputMediaType.ContentType == VAST.Common.ContentType.Video)
                            {
                                this.waitingForKeyframe = false;
                            }
                            else
                            {
                                // ignore any samples until we get the first video keyframe
                                continue;
                            }
                        }

                        if (originalInputSample.KeyFrame && streamContext.InputMediaType.ContentType == VAST.Common.ContentType.Video)
                        {

                            if (this.currentFragmentStream != null)
                            {

                                // we've got keyframe, start new fragment
                                this.writer.Flush();

                                if (this.currentFragmentStream.Position > 0)
                                {
                                    this.currentFragmentStream.SetLength(this.currentFragmentStream.Position);
                                    this.currentFragmentStream.Seek(0, System.IO.SeekOrigin.Begin);
                                    VAST.Common.Log.DebugFormat("Created mp4 fragment, {0} bytes", this.currentFragmentStream.Length);
                                    // TODO: currentFragmentStream contains fragment, process it accordingly
                                }
                                else
                                {
                                    // fragment is empty
                                }

                                this.currentFragmentStream.Dispose();
                                this.currentFragmentStream = null;

                            }

                            // create new stream for the next fragment
                            this.currentFragmentStream = new VAST.Common.VersatileBufferStream { AutoGrowth = true };
                            this.writer.SetOutputStream(this.currentFragmentStream);

                        }

                        this.writer.Encode(originalInputSample, originalInputSample.StreamIndex);

                    }
                    catch (Exception ex)
                    {
                        VAST.Common.Log.ErrorFormat("Error occurred: {0}", ex);
                    }
                    finally
                    {
                        originalInputSample.Release();
                    }

                }
                else
                {
                    // input queue is empty, we can sleep
                    await Task.Delay(10);
                }

            }

        }

    }

}
