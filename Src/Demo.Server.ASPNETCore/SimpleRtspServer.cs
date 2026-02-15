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

    class SimpleRtspServer : IDisposable
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

        private VAST.RTSP.RtspServer server = null;
        private Dictionary<string, PublishedStream> publishedStreams = new Dictionary<string, PublishedStream>();
        private Dictionary<EndPoint, PublishedStream> connectedPublishers = new Dictionary<EndPoint, PublishedStream>();
        private Dictionary<EndPoint, PublishedStream> connectedClients = new Dictionary<EndPoint, PublishedStream>();
        private string pullSourcePath = null;

        public SimpleRtspServer()
        {

            // prepare server parameters
            var pars = new VAST.RTSP.RtspServerParameters();
            pars.AllowedRtpTransports = VAST.RTP.RtpTransportType.Any;
            pars.RtspEndPoints.Add(new IPEndPoint(IPAddress.Any, 554));
            // uncomment RTSPS end point and certificate thumbprint assignments if you want to run secure protocol
            //pars.RtspsEndPoints.Add(new IPEndPoint(IPAddress.Any, 555));
            //pars.CertificateThumbprint = "YOUR_CERTIFICATE_THUMBPRINT";

            // create RTSP server
            this.server = new VAST.RTSP.RtspServer(100, pars);
            this.server.Connected += Server_Connected;
            this.server.Disconnected += Server_Disconnected;
            this.server.PublisherConnected += Server_PublisherConnected;
            this.server.ClientConnected += Server_ClientConnected;
            this.server.Start();

            ///////////////////////////////////////////////////////////////////
            // 1. Pull source
            ///////////////////////////////////////////////////////////////////

            //// create published stream for pull source
            //this.pullSourcePath = "pull";
            //PublishedStream publishedStream = new PublishedStream(this.pullSourcePath);
            //this.publishedStreams.Add(this.pullSourcePath, publishedStream);

            //// create image source
            //VAST.RTSP.RtspClientSource source = new VAST.RTSP.RtspClientSource();
            //source.Uri = "rtsp://...";
            //source.NewStream += Source_NewStream;
            //source.NewSample += Source_NewSample;
            //source.StateChanged += Source_StateChanged;
            //source.Error += Source_Error;

            //// open source
            //source.Open();

            ///////////////////////////////////////////////////////////////////
            // 2. Image source
            ///////////////////////////////////////////////////////////////////

            //// create published stream for image source
            //this.pullSourcePath = "image";
            //PublishedStream publishedStream = new PublishedStream(this.pullSourcePath);
            //this.publishedStreams.Add(this.pullSourcePath, publishedStream);

            //// create image source
            //VAST.File.ImageSource source = new VAST.File.ImageSource();
            //// source image can be set via file path, stream or Bitmap
            //source.SetImage(@"IMAGE_FILE_PATH");
            //source.NewStream += Source_NewStream;
            //source.NewSample += Source_NewSample;
            //source.Error += Source_Error;

            //// set video encoding parameters
            //source.Open();
            //VAST.Common.MediaType mt = new VAST.Common.MediaType
            //{
            //    ContentType = VAST.Common.ContentType.Video,
            //    CodecId = VAST.Common.Codec.H264,
            //    Bitrate = 1000000,
            //    Width = 1280,
            //    Height = 720,
            //    Framerate = new VAST.Common.Rational(30),
            //};
            //source.SetDesiredOutputType(0, mt);

            //// start pushing
            //source.Start();

        }

        ~SimpleRtspServer()
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

                publisher.NewStream += Source_NewStream;
                publisher.NewSample += Source_NewSample;

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

        private void Source_NewStream(object sender, Media.NewStreamEventArgs e)
        {

            lock (this)
            {

                string publishingPath = (sender is VAST.Network.INetworkSource) ? ((VAST.Network.INetworkSource)sender).PublishingPath : this.pullSourcePath;
                if (!this.publishedStreams.ContainsKey(publishingPath))
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} is not published", publishingPath);
                    return;
                }

                PublishedStream publishedStream = this.publishedStreams[publishingPath];

                // save MediaType of the media stream, it'll be necessary to send to connected client
                while (publishedStream.MediaStreams.Count < e.StreamCount)
                {
                    publishedStream.MediaStreams.Add(null);
                }

                // save MediaType of the media stream, it'll be necessary to send to connected client
                publishedStream.MediaStreams[e.StreamIndex] = e.MediaType.Clone();
                VAST.Common.Log.DebugFormat("Publisher {0} new media type {1}: {2}", publishingPath, e.StreamIndex, e.MediaType.ToString());

            }

        }

        private void Source_NewSample(object sender, Media.NewSampleEventArgs e)
        {

            lock (this)
            {

                string publishingPath = (sender is VAST.Network.INetworkSource) ? ((VAST.Network.INetworkSource)sender).PublishingPath : this.pullSourcePath;
                if (!this.publishedStreams.ContainsKey(publishingPath))
                {
                    VAST.Common.Log.ErrorFormat("Stream {0} is not published", publishingPath);
                    return;
                }

                PublishedStream publishedStream = this.publishedStreams[publishingPath];
                //VAST.Common.Log.ErrorFormat("Publisher {0} new sample {1}: PTS {2} size {3}", publishingPath, e.Sample.StreamIndex, e.Sample.Pts, e.Sample.ActualSize);

                foreach (VAST.Media.IMediaSink client in publishedStream.ConnectedClients.Values)
                {
                    client.PushMedia(e.Sample.StreamIndex, e.Sample);
                }

            }

        }

        private void Source_StateChanged(object sender, Media.MediaState e)
        {

            lock (this)
            {

                switch (e)
                {
                    case VAST.Media.MediaState.Opened:
                        if (sender is VAST.RTSP.RtspClientSource)
                        {
                            // pull source has been successfully opened, start it
                            VAST.RTSP.RtspClientSource source = (VAST.RTSP.RtspClientSource)sender;
                            source.Start();
                        }
                        break;
                }

            }

        }

        private void Source_Error(object sender, Media.ErrorEventArgs e)
        {

            lock (this)
            {

                // this is an image source
                VAST.Common.Log.DebugFormat("Image source error {0} publishing point {1}", e.ErrorDescription, this.pullSourcePath);
                PublishedStream publishedStream = this.publishedStreams[this.pullSourcePath];
                foreach (KeyValuePair<EndPoint, VAST.Media.IMediaSink> entry in publishedStream.ConnectedClients)
                {
                    this.connectedClients.Remove(entry.Key);
                    entry.Value.Stop();
                }

                this.publishedStreams.Remove(publishedStream.PublishName);

            }

        }

    }

}
