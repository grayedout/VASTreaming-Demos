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
    using System.Net;

    class SimpleRtmpServer : IDisposable
    {

        private class PublishedStream
        {

            public PublishedStream(string publishName)
            {
                this.PublishName = publishName;
                this.MediaStreams = new List<Common.MediaType>();
                this.ConnectedClients = new Dictionary<EndPoint, VAST.Media.IMediaSink>();
            }

            public string PublishName { get; set; }
            public List<VAST.Common.MediaType> MediaStreams { get; set; }
            public Dictionary<EndPoint, VAST.Media.IMediaSink> ConnectedClients { get; set; }

        }

        private VAST.RTMP.RtmpServer server = null;
        private Dictionary<string, PublishedStream> publishedStreams = new Dictionary<string, PublishedStream>();
        private Dictionary<EndPoint, PublishedStream> connectedPublishers = new Dictionary<EndPoint, PublishedStream>();
        private Dictionary<EndPoint, PublishedStream> connectedClients = new Dictionary<EndPoint, PublishedStream>();
        private Dictionary<Guid, PublishedStream> activeWriters = new Dictionary<Guid, PublishedStream>();

        public SimpleRtmpServer()
        {

            // initialize RTMP
            VAST.RTMP.RtmpGlobal.RtmpDefaultAckWindowSize = 2500000;
            VAST.RTMP.RtmpGlobal.RtmpDefaultOurChunkSize = 1024;
            VAST.RTMP.RtmpGlobal.RtmpSessionInactivityTimeoutMs = 30000;
            VAST.RTMP.RtmpGlobal.RtmpApplication = "live";

            // prepare server parameters
            var pars = new VAST.RTMP.RtmpServerParameters();
            pars.RtmpEndPoints.Add(new IPEndPoint(IPAddress.Any, 1935));
            // uncomment RTMPS end point and certificate thumbprint assignments if you want to run secure protocol
            //pars.RtmpsEndPoints.Add(new IPEndPoint(IPAddress.Any, 1936));
            //pars.CertificateThumbprint = "YOUR_CERTIFICATE_THUMBPRINT";
            pars.RtmpApplication = "live";

            // create RTMP server
            this.server = new VAST.RTMP.RtmpServer(pars);
            this.server.Connected += Server_Connected;
            this.server.Disconnected += Server_Disconnected;
            this.server.PublisherConnected += Server_PublisherConnected;
            this.server.ClientConnected += Server_ClientConnected;

        }

        ~SimpleRtmpServer()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (this.server != null)
            {
                this.server.Stop();
                this.server = null;
            }
        }

        private void Server_Connected(object sender, VAST.Transport.TransportArgs e)
        {
            // new peer connected, it's unknown yet whether it's a client or publisher
        }

        private void Server_Disconnected(object sender, VAST.Transport.TransportArgs e)
        {

            lock (this)
            {

                if (this.connectedPublishers.ContainsKey(e.EndPoint))
                {

                    PublishedStream publishedStream = this.connectedPublishers[e.EndPoint];
                    VAST.Common.Log.DebugFormat("Disconnected publisher {0} publishing point {1}", e.EndPoint, publishedStream.PublishName);
                    foreach (KeyValuePair<EndPoint, VAST.Media.IMediaSink> entry in publishedStream.ConnectedClients)
                    {
                        this.connectedClients.Remove(entry.Key);
                        entry.Value.Stop();
                    }

                    this.publishedStreams.Remove(publishedStream.PublishName);
                    this.connectedPublishers.Remove(e.EndPoint);

                }
                else if (this.connectedClients.ContainsKey(e.EndPoint))
                {

                    PublishedStream publishedStream = this.connectedClients[e.EndPoint];
                    if (publishedStream.ConnectedClients.ContainsKey(e.EndPoint))
                    {
                        VAST.Common.Log.DebugFormat("Disconnected client {0} publishing point {1}", e.EndPoint, publishedStream.PublishName);
                        publishedStream.ConnectedClients.Remove(e.EndPoint);
                    }
                    else
                    {
                        VAST.Common.Log.ErrorFormat("Disconnected client {0} not found in publishing point {1}", e.EndPoint, publishedStream.PublishName);
                    }

                    this.connectedClients.Remove(e.EndPoint);

                }
                else
                {
                    VAST.Common.Log.ErrorFormat("Disconnected peer {0} not found", e.EndPoint);
                }

            }

        }

        private void Server_PublisherConnected(object sender, VAST.Network.INetworkSource publisher)
        {

            lock (this)
            {

                if (this.publishedStreams.ContainsKey(publisher.PublishingPath))
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} already published", publisher.PublishingPath);
                    publisher.Accept = true;
                    return;
                }

                // accept any stream name
                publisher.Accept = true;

                publisher.NewStream += Publisher_NewStream;
                publisher.NewSample += Publisher_NewSample;

                PublishedStream publishedStream = new PublishedStream(publisher.PublishingPath);
                this.publishedStreams.Add(publisher.PublishingPath, publishedStream);
                this.connectedPublishers.Add(publisher.EndPoint, publishedStream);

            }

        }

        private void Server_ClientConnected(object sender, VAST.Network.INetworkSink client)
        {

            lock (this)
            {

                if (!this.publishedStreams.ContainsKey(client.PublishingPath))
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} is not published", client.PublishingPath);
                    return;
                }

                PublishedStream publishedStream = this.publishedStreams[client.PublishingPath];

                // accept any client
                client.Accept = true;

                int index = 0;
                foreach (VAST.Common.MediaType mt in publishedStream.MediaStreams)
                {
                    client.AddStream(index++, mt);
                }

                publishedStream.ConnectedClients.Add(client.EndPoint, client);
                this.connectedClients.Add(client.EndPoint, publishedStream);

            }

        }

        private void Publisher_NewStream(object sender, Media.NewStreamEventArgs e)
        {

            lock (this)
            {

                VAST.Network.INetworkSource publisher = sender as VAST.Network.INetworkSource;
                if (!this.publishedStreams.ContainsKey(publisher.PublishingPath))
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} is not published", publisher.PublishingPath);
                    return;
                }

                PublishedStream publishedStream = this.publishedStreams[publisher.PublishingPath];

                // save MediaType of the media stream, it'll be necessary to send to connected client
                while (publishedStream.MediaStreams.Count < e.StreamCount)
                {
                    publishedStream.MediaStreams.Add(null);
                }

                // save MediaType of the media stream, it'll be necessary to send to connected client
                publishedStream.MediaStreams[e.StreamIndex] = e.MediaType.Clone();
                VAST.Common.Log.DebugFormat("Publisher {0} new media type {1}: {2}", publisher.PublishingPath, e.StreamIndex, e.MediaType.ToString());

                bool descriptorReady = true;
                for (int i = 0; i < e.StreamCount; ++i)
                {
                    if (publishedStream.MediaStreams[i] == null)
                    {
                        descriptorReady = false;
                        break;
                    }
                }

                if (descriptorReady)
                {
                    // TODO: start file recording if necessary
                    //this.startRecording(publishedStream, @"RECORDING_FILE_PATH");
                }

            }

        }

        private void Publisher_NewSample(object sender, Media.NewSampleEventArgs e)
        {

            lock (this)
            {

                VAST.Network.INetworkSource publisher = sender as VAST.Network.INetworkSource;
                if (!this.publishedStreams.ContainsKey(publisher.PublishingPath))
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} is not published", publisher.PublishingPath);
                    return;
                }

                PublishedStream publishedStream = this.publishedStreams[publisher.PublishingPath];
                //VAST.Common.Log.ErrorFormat("Publisher {0} new sample {1}: PTS {2} size {3}", publisher.PublishingPath, mediaStreamId, mediaSample.Pts, mediaSample.ActualSize);

                foreach (VAST.Media.IMediaSink client in publishedStream.ConnectedClients.Values)
                {
                    client.PushMedia(e.Sample.StreamIndex, e.Sample);
                }

            }

        }

        private void startRecording(PublishedStream publishedStream, string path)
        {

            VAST.Media.IMediaSink fileWriter = VAST.Media.SinkFactory.Create(path);
            if (fileWriter == null)
            {
                throw new Exception("File type is not supported");
            }

#if VAST_FEATURE_FILE_ISO
            if (fileWriter is VAST.File.ISO.IsoSink)
            {
                // additional parameters for MP4 writer
                ((VAST.File.ISO.IsoSink)fileWriter).ParsingParameters = new VAST.File.ISO.ParsingParameters { WriteMediaDataLast = true };
            }
#endif

            fileWriter.Error += FileWriter_Error;
            fileWriter.Uri = path;
            fileWriter.Open();

            int streamIndex = 0;
            foreach (VAST.Common.MediaType mt in publishedStream.MediaStreams)
            {
                fileWriter.AddStream(streamIndex++, mt);
            }

            fileWriter.Start();
            publishedStream.ConnectedClients.Add(new IPEndPoint(0, 0), fileWriter);
            this.activeWriters.Add(fileWriter.UniqueId, publishedStream);

        }

        private void FileWriter_Error(object sender, Media.ErrorEventArgs e)
        {

            // handle recording error
            VAST.Media.IMediaSink fileWriter = sender as VAST.Media.IMediaSink;
            PublishedStream publishedStream = this.activeWriters[fileWriter.UniqueId];
            EndPoint endPoint = new IPEndPoint(0, 0);
            if (publishedStream.ConnectedClients.ContainsKey(endPoint))
            {
                publishedStream.ConnectedClients.Remove(endPoint);
            }

            this.activeWriters.Remove(fileWriter.UniqueId);

        }

    }

}
