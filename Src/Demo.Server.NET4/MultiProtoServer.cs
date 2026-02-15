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
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    class MultiProtoServer : IDisposable
    {

        private volatile bool running = true;
        private VAST.Network.StreamingServer server = null;
        private Task pushingTask = null;

#if VAST_FEATURE_WEBRTC
        private VAST.ICE.TurnServer turnServer = null;
#endif

        public MultiProtoServer()
        {

            server = new VAST.Network.StreamingServer(20);

#if VAST_FEATURE_RTMP
            // RTMP
            server.EnableRtmp = true;
            server.RtmpServerParameters.RtmpEndPoints.Add(new IPEndPoint(IPAddress.Any, 1935));
            // uncomment RTMPS end point assignment if you want to run secure protocol
            //server.RtmpServerParameters.RtmpsEndPoints.Add(new IPEndPoint(IPAddress.Any, 1936)),
            server.RtmpServerParameters.RtmpApplication = "live";
#endif

#if VAST_FEATURE_RTSP
            // RTSP
            server.EnableRtsp = true;
            server.RtspServerParameters.AllowedRtpTransports = VAST.RTP.RtpTransportType.Any;
            server.RtspServerParameters.RtspEndPoints.Add(new IPEndPoint(IPAddress.Any, 554));
            // uncomment RTSPS end point assignment if you want to run secure protocol
            //server.RtspServerParameters.RtspsEndPoints.Add(new IPEndPoint(IPAddress.Any, 555));
#endif

#if VAST_FEATURE_SRT
            // SRT
            server.EnableSrt = true;
            VAST.SRT.SrtServerInstanceParameters srtInstancePars = new VAST.SRT.SrtServerInstanceParameters { AllowFileTransfers = true };
            // by default SRT listens on IPAddress.Any and IPAddress.IPv6Any, adjust srtInstancePars.EndPoints if necessary
            server.SrtServerParameters.Instances.Add(srtInstancePars);
#endif

#if VAST_FEATURE_API || VAST_FEATURE_HLS || VAST_FEATURE_TS || VAST_FEATURE_MPEG_DASH || VAST_FEATURE_WEBRTC || VAST_FEATURE_JPEG
            // common HTTP server for WebRTC, MPEG-DASH and HLS
            server.EnableHttp = true;
            server.HttpServerParameters = new VAST.HTTP.HttpServerParameters
            {
                HttpPorts = { 8888 },
                // uncomment HTTPS port assignment if you want to run secure protocol
                //HttpsPorts = { 8889 }
            };
#endif

#if VAST_FEATURE_WEBRTC

            // TODO: replace it with your actual public IPv4 address!
            string publicIPv4 = "YOUR_PUBLIC_IPV4_ADDRESS";

            // TODO: assign your actual public IPv6 address if you have one
            string publicIPv6 = null;

            // WebRTC is supported only on Windows at the moment
            server.EnableWebRtc = true;
            server.WebRtcServerParameters = new VAST.WebRTC.WebRtcServerParameters
            {
                WebRtcPath = "/rtc",
                IceServers = "stun:stun.l.google.com:19302",
                MediaTransport = VAST.WebRTC.WebRtcTransport.Auto,
                VideoTranscoding = VAST.Common.MediaTranscodingMode.TranscodeIncompatible,
            };

            if (!string.IsNullOrEmpty(publicIPv4) && publicIPv4 != "YOUR_PUBLIC_IPV4_ADDRESS")
            {
                // using our own TURN server
                server.WebRtcServerParameters.IceServers = $"turn:user:password@{publicIPv4}:3478";
            }

            if (!string.IsNullOrEmpty(publicIPv4) && publicIPv4 != "YOUR_PUBLIC_IPV4_ADDRESS")
            {

                // TURN server is multi-platform
                var credentials = new Dictionary<string, string>();
                credentials.Add("user", "password");
                var turnPars = new VAST.ICE.TurnServerParameters
                {
                    UserCredentials = credentials,
                    PublicIPv4Address = IPAddress.Parse(publicIPv4)
                };

                if (string.IsNullOrEmpty(publicIPv6))
                {
                    // if IPv6 is not specified then don't start IPv6 end points
                    turnPars.UdpServerIPv6EndPoint = null;
                    turnPars.TcpServerIPv6EndPoint = null;
                }
                else
                {
                    turnPars.PublicIPv6Address = IPAddress.Parse(publicIPv6);
                }

                // uncomment TLS endpoint assignments if you want to run secure TURN server
                //turnPars.TlsServerIPv4EndPoint = new IPEndPoint(IPAddress.Any, 5349);
                //turnPars.TlsServerIPv6EndPoint = new IPEndPoint(IPAddress.IPv6Any, 5349);
                //turnPars.CertificateThumbprint = "YOUR_CERTIFICATE_THUMBPRINT";

                this.turnServer = new VAST.ICE.TurnServer(turnPars);
                this.turnServer.Start();

            }

#endif

#if VAST_FEATURE_MPEG_DASH
            // MPEG-DASH
            server.EnableMpegDash = true;
            server.DashServerParameters = new VAST.DASH.DashServerParameters
            {
                MpegDashPath = "/dash"
            };
#endif

#if VAST_FEATURE_HLS
            // HLS
            server.EnableHls = true;
            server.HlsServerParameters = new VAST.HLS.HlsServerParameters
            {
                HlsPath = "/hls",
                EnableLowLatency = false
            };
#endif

#if VAST_FEATURE_TS
            // TS over HTTP
            // change to true if you want to run this server
            server.EnableTsHttp = false;
            server.TsHttpServerParameters = new VAST.TS.TsHttpServerParameters
            {
                TsHttpPath = "/ts",
            };
#endif

#if VAST_FEATURE_JPEG
            // MJPEG over HTTP
            // change to true if you want to run this server
            server.EnableMjpeg = false;
            server.MjpegServerParameters = new VAST.Image.JPEG.MjpegServerParameters
            {
                MjpegPath = "/mjpeg",
            };
#endif

#if VAST_FEATURE_AUDIO
            // custom PCM server
            // change to true if you want to run this server
            server.EnableWsPcm = false;
            server.WsPcmServerParameters = new VAST.Audio.WsPcmServerParameters
            {
                WsPcmPath = "/pcm",
            };
#endif

#if VAST_FEATURE_API
            // API server
            server.EnableJsonApi = true;
            server.ApiServerParameters.Users.Add("admin", "admin");
#endif

            // proper certificate thumbprint must be assigned if you want to run secure protocols
            server.CertificateThumbprint = "YOUR_CERTIFICATE_THUMBPRINT";

            server.PublishingPointRequested += Server_PublishingPointRequested;
            server.Authorize += Server_Authorize;
            server.PublisherConnected += Server_PublisherConnected;
            server.ClientConnected += Server_ClientConnected;
            server.Error += Server_Error;
            server.Disconnected += Server_Disconnected;
            // this is not an event but a delegate accepting an async function
            server.FileTransferRequestedHandler = Server_FileTransferRequestedHandler;
            server.Start();

            // below listed optional use-cases which could be un-commented if necessary:

            ///////////////////////////////////////////////////////////////////
            // 1. Create publishing point with a pull source
            ///////////////////////////////////////////////////////////////////

            //this.server.CreatePublishingPoint("YOUR_PUBLISHING_POINT_NAME", "YOUR_SOURCE_URI");

            ///////////////////////////////////////////////////////////////////
            // 2. Create VOD publishing point for a single file
            //    NOTE: accessible via HLS http://127.0.0.1:8888/hls/VOD_PUBLISHING_POINT_NAME
            //          or MPEG-DASH http://127.0.0.1:8888/dash/VOD_PUBLISHING_POINT_NAME
            ///////////////////////////////////////////////////////////////////

            //Uri uri = new Uri(@"MP4_FILE_PATH");
            //this.server.CreatePublishingPoint("VOD_PUBLISHING_POINT_NAME", uri.AbsoluteUri, VAST.Common.StreamingMode.Vod);

            ///////////////////////////////////////////////////////////////////
            // 3. Create VOD publishing point for a single file from user stream
            //    NOTE: accessible via HLS http://127.0.0.1:8888/hls/VOD_PUBLISHING_POINT_NAME
            ///////////////////////////////////////////////////////////////////

            //VAST.File.ISO.IsoSource source = new VAST.File.ISO.IsoSource();
            //System.IO.Stream file = System.IO.File.Open(@"MP4_FILE_PATH", System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            //byte[] buffer = new byte[file.Length];
            //file.Read(buffer);
            //source.Stream = new System.IO.MemoryStream(buffer);
            //// if using user stream then publishing point can support only one output protocol
            //this.server.CreatePublishingPoint("VOD_PUBLISHING_POINT_NAME", source, VAST.Common.StreamingMode.Vod,
            //    new VAST.Network.PublishingPointParameters { IsTemporary = true, AllowedProtocols = VAST.Common.StreamingProtocol.HLS });

            ///////////////////////////////////////////////////////////////////
            // 4. Create VOD publishing point for a directory
            //    NOTE: accessible via HLS http://127.0.0.1:8888/hls/VOD_DIRECTORY_PUBLISHING_POINT_NAME/filename.mp4
            //          or MPEG-DASH http://127.0.0.1:8888/dash/VOD_DIRECTORY_PUBLISHING_POINT_NAME/filename.mp4
            //          where filename.mp4 is a file path relative to PATH_TO_FOLDER_WITH_FILES
            ///////////////////////////////////////////////////////////////////

            //Uri uri = new Uri(@"PATH_TO_FOLDER_WITH_FILES");
            //// parameter ?recursive=1 and can be used to access files in subdirectories
            //string wildcardUri = uri.AbsoluteUri + "/*.mp4?recursive=1";
            //this.server.CreatePublishingPoint("VOD_DIRECTORY_PUBLISHING_POINT_NAME", wildcardUri, VAST.Common.StreamingMode.Vod);

            ///////////////////////////////////////////////////////////////////
            // 5. Create publishing point with a file source (endless loop of the same file)
            ///////////////////////////////////////////////////////////////////

            //VAST.File.FileSource source = new VAST.File.FileSource();
            //source.Uri = @"VIDEO_FILE_PATH";
            //source.Loop = true;
            //this.server.CreatePublishingPoint("YOUR_PUBLISHING_POINT_NAME", source);

            ///////////////////////////////////////////////////////////////////
            // 6. Create publishing point with an image source
            ///////////////////////////////////////////////////////////////////

            //VAST.File.ImageSource source = new VAST.File.ImageSource();
            //// source image can be set via file path, stream or Bitmap
            //source.SetImage(@"IMAGE_FILE_PATH");
            //source.Open();
            //// set video encoding parameters
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
            //// create publishing point
            //this.server.CreatePublishingPoint("YOUR_PUBLISHING_POINT_NAME", source);

            ///////////////////////////////////////////////////////////////////
            // 7. Create publishing point with a capture source
            ///////////////////////////////////////////////////////////////////

            //Task.Run(() => createCaptureSource());

            ///////////////////////////////////////////////////////////////////
            // 8. Create publishing point which uses pre-encoded media data of user
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine1());

            ///////////////////////////////////////////////////////////////////
            // 9. Create publishing point which uses ImageSource for encoding video stream
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine2());

            ///////////////////////////////////////////////////////////////////
            // 10. Encode uncompressed user video on the fly and push to the server
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine3());

            ///////////////////////////////////////////////////////////////////
            // 11. Create publishing point with a VirtualRtpSource source and push RTP packets to it.
            ///////////////////////////////////////////////////////////////////

            //this.pushingTask = Task.Run(() => pushingRoutine4());

            ///////////////////////////////////////////////////////////////////
            // 12. Create mixing publishing point waiting for publisher
            ///////////////////////////////////////////////////////////////////

            //this.createMixingPublishingPoint1();

            ///////////////////////////////////////////////////////////////////
            // 13. Create proxy source to forcibly transcode pull stream
            ///////////////////////////////////////////////////////////////////

            //this.createProxySource("transcode", "rtsp://192.168.0.101/stream");

            ///////////////////////////////////////////////////////////////////
            // 14. Create publishing point with a screen/window capture source
            ///////////////////////////////////////////////////////////////////

            //Task.Run(() => this.createScreenCaptureSource());

        }

        ~MultiProtoServer()
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

            if (this.server != null)
            {
                this.server.Stop();
                this.server = null;
            }

        }

        private void Server_PublishingPointRequested(Guid connectionId, VAST.Network.ConnectionInfo connectionInfo, VAST.Network.CreatedPublishingPointParameters createdPublishingPointParameters)
        {
            // newly connected client has requested publishing point which doesn't exist yet
            // user code in this event handler can create on-demand publishing point here
            //if (connectionInfo.PublishingPath == "YOUR_ON_DEMAND_PUBLISHING_POINT_NAME")
            //{
            //    this.server.CreatePublishingPoint("YOUR_ON_DEMAND_PUBLISHING_POINT_NAME", "YOUR_ON_DEMAND_SOURCE_URI");
            //}
        }

        private void Server_Authorize(Guid connectionId, VAST.Network.ConnectionInfo connectionInfo)
        {
            // connection type has been detected as well as all other parameters
            // user code has to choose whether connection is valid and set IsValid property accordingly
            connectionInfo.IsValid = true;
        }

        private void Server_PublisherConnected(Guid connectionId, VAST.Network.PublishingPoint publishingPoint)
        {

            // publisher (or pull source created by user) has been connected and authorized
            // new publishing point has been created and is ready for forwarding, recording etc

            // below is a sample code to start forwarding of your source to another server
            // sample is using pull source but you can apply this logic to any source
            //if (/* TODO: set your condition */)
            {
                //publishingPoint.StartForwarding("YOUR_FORWARDING_URI");
            }

            // below is a sample code to start recording of your source to a local file
            // sample is using pull source but you can apply this logic to any source
            //if (/* TODO: set your condition */)
            {
                //publishingPoint.StartRecording("YOUR_RECORDING_FILE_PATH");
            }

        }

        private void Server_ClientConnected(Guid connectionId, VAST.Network.ConnectedClient client)
        {
            // new client has been connected
            // VAST.Network.ConnectedClient contains additional information about a client
            // including a publishing point this client belongs to
        }

        private Task Server_FileTransferRequestedHandler(object caller, VAST.Media.FileTransferRequestedArgs args)
        {

            return Task.Run(() =>
            {

                // handler can be an async task

                switch (args.RequestedFileTransfer.Direction)
                {

                    case VAST.Common.MediaFlowDirection.Input:
                        {
                            args.IsValid = true;
                            Uri u = new Uri(args.RequestedFileTransfer.Uri);
                            args.RequestedFileTransfer.FilePath = $"C:\\Temp\\{u.LocalPath}";
                            // transfer completion wait must be handled in another task because otherwise server will be locked during the whole transfer
                            Task.Run(async () =>
                            {
                                bool result = await args.RequestedFileTransfer.ExecuteAsync();
                                // TODO: check result and perform further actions if necessary
                            });
                            break;
                        }

                    case VAST.Common.MediaFlowDirection.Output:
                        {
                            Uri u = new Uri(args.RequestedFileTransfer.Uri);
                            string localPath = $"C:\\Temp\\{u.LocalPath}";
                            if (System.IO.File.Exists(localPath))
                            {
                                args.IsValid = true;
                                args.RequestedFileTransfer.FilePath = localPath;
                                // transfer completion wait must be handled in another task because otherwise server will be locked during the whole transfer
                                Task.Run(async () =>
                                {
                                    bool result = await args.RequestedFileTransfer.ExecuteAsync();
                                    // TODO: check result and perform further actions if necessary
                                });
                            }
                            else
                            {
                                // file not found
                                args.IsValid = false;
                            }
                            break;
                        }

                }

            });

        }

        private void Server_Error(Guid connectionId, string errorDescription)
        {
            // session has encountered unrecoverable error and will be closed shortly
        }

        private void Server_Disconnected(Guid connectionId, VAST.Network.ExtendedSocketError socketError)
        {
            // connection has been closed
            // socketError may contain additional information about the disconnection reason
            // if Error event has been raised for this connectionId then it means that server forcibly closed the connection
        }

        private void pushingRoutine1()
        {

            // create virtual network source
            VAST.Network.VirtualNetworkSource source = new VAST.Network.VirtualNetworkSource();

            byte[] videoBuffer = new byte[Properties.Resources.h264_demo.Length];
            Properties.Resources.h264_demo.CopyTo(videoBuffer, 0);
            byte[] audioBuffer = new byte[Properties.Resources.audio.Length];
            Properties.Resources.audio.CopyTo(audioBuffer, 0);

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

            VAST.Common.MediaType metadataMediaType = new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Text,
                CodecId = VAST.Common.Codec.Text,
                Bitrate = 23 * 10 * 8 // 23 bytes per packet, 10 packets per second
            };

            // add audio media type
            int metadataStreamIndex = source.AddStream(metadataMediaType);

            // start the source and add create new publishing point with it
            this.server.CreatePublishingPoint("builtin", source);

            DateTime streamingStarted = DateTime.Now;
            long timestampBase = DateTime.UtcNow.Ticks;
            long videoFrameCount = 0;
            long audioFrameCount = 0;
            long metadataFrameCount = 0;
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
                // send metadata packet every 100 milliseconds
                long metadataTime = metadataFrameCount * 1000000L;

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

                        //VAST.Common.Log.DebugFormat("Read sample {0}, pts {1}, size {2}", VAST.Common.ContentType.Video, videoFileTime, frameSize);
                        source.PushMedia(videoStreamIndex, videoBuffer, h264BitstreamPosition, frameSize, timestampBase + videoFileTime, timestampBase + videoFileTime, VAST.Common.VersatileBufferFlag.AbsoluteTimestamp);

                        h264BitstreamPosition += frameSize;
                        videoFrameCount++;
                        videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;

                    }

                    if (audioFileTime <= playbackTime)
                    {

                        int frameSize = ((audioBuffer[audioBitstreamPosition + 3] & 0x03) << 11) | (audioBuffer[audioBitstreamPosition + 4] << 3) | ((audioBuffer[audioBitstreamPosition + 5] & 0xE0) >> 5);

                        //VAST.Common.Log.DebugFormat("Read sample {0}, pts {1}, size {2}", VAST.Common.ContentType.Audio, videoFileTime, audioMediaType.FrameSize);
                        source.PushMedia(audioStreamIndex, audioBuffer, audioBitstreamPosition + 7, frameSize - 7, timestampBase + audioFileTime, timestampBase + audioFileTime, VAST.Common.VersatileBufferFlag.AbsoluteTimestamp);

                        audioBitstreamPosition += frameSize;
                        audioFrameCount++;
                        audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                    }

                    if (metadataTime <= playbackTime)
                    {
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        source.PushMedia(metadataStreamIndex, data, 0, data.Length, timestampBase + metadataTime, timestampBase + metadataTime, VAST.Common.VersatileBufferFlag.AbsoluteTimestamp);
                        metadataFrameCount++;
                        metadataTime = metadataFrameCount * 1000000L;
                    }

                }
                while (videoFileTime <= playbackTime || audioFileTime <= playbackTime || metadataTime < playbackTime);

                Task.Delay(10).Wait();

            }

        }

#if VAST_FEATURE_IMAGE

        private void pushingRoutine2()
        {

            Bitmap bmpBackground = new Bitmap(new System.IO.MemoryStream(Properties.Resources.logo));
            Bitmap bmpImage = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmpImage);
            Font font = new Font("Arial", 48, FontStyle.Bold);
            g.DrawImage(bmpBackground, 0, 0, 1280, 720);

            VAST.Image.ImageSource videoSource = new VAST.Image.ImageSource(true);
            videoSource.SetImage(bmpImage);
            videoSource.Open();
            // set video encoding parameters
            VAST.Common.MediaType videoMediaType = new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Video,
                CodecId = VAST.Common.Codec.H264,
                Bitrate = 1000000,
                Width = 1280,
                Height = 720,
                Framerate = new VAST.Common.Rational(30),
            };
            videoSource.SetDesiredOutputType(0, videoMediaType);

            VAST.Network.VirtualNetworkSource audioSource = new VAST.Network.VirtualNetworkSource();

            byte[] audioBuffer = new byte[Properties.Resources.audio.Length];
            Properties.Resources.audio.CopyTo(audioBuffer, 0);

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
            int audioStreamIndex = audioSource.AddStream(audioMediaType);

            VAST.Network.AggregatedNetworkSource source = new VAST.Network.AggregatedNetworkSource();
            source.AddSource(videoSource);
            source.AddSource(audioSource);

            // start the source and add create new publishing point with it
            this.server.CreatePublishingPoint("builtin", source);

            DateTime streamingStarted = DateTime.UtcNow;
            long videoFrameCount = 0;
            long audioFrameCount = 0;
            int audioBitstreamPosition = 0;

            while (true)
            {

                // playback time in 100ns units
                long playbackTime = (long)(DateTime.UtcNow - streamingStarted).Ticks;
                // video timestamp converted to 100ns units
                long videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;
                // audio timestamp converted to 100ns units
                long audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                do
                {

                    if (audioBitstreamPosition >= audioBuffer.Length)
                    {
                        // EOS
                        audioBitstreamPosition = 0;
                    }

                    if (videoFileTime <= playbackTime)
                    {

                        long started = VAST.Common.MediaTime.Get();
                        g.FillRectangle(Brushes.White, 250, 550, 780, 100);
                        g.DrawString(string.Format("{0:yyyy-MM-dd HH:mm:ss.fff}", DateTime.Now), font, Brushes.Purple, 250, 550);
                        g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
                        videoSource.SetImage(bmpImage);
                        long procTime = VAST.Common.MediaTime.Get() - started;
                        if (procTime > 300000)
                        {
                            VAST.Common.Log.DebugFormat("Long processing time {0}", procTime);
                        }

                        videoFrameCount++;
                        videoFileTime = videoFrameCount * 10000000L * videoMediaType.Framerate.Den / videoMediaType.Framerate.Num;

                    }

                    if (audioFileTime <= playbackTime)
                    {

                        int frameSize = ((audioBuffer[audioBitstreamPosition + 3] & 0x03) << 11) | (audioBuffer[audioBitstreamPosition + 4] << 3) | ((audioBuffer[audioBitstreamPosition + 5] & 0xE0) >> 5);

                        //VAST.Common.Log.DebugFormat("Read sample {0}, pts {1}, size {2}", VAST.Common.ContentType.Audio, videoFileTime, audioMediaType.FrameSize);
                        audioSource.PushMedia(audioStreamIndex, audioBuffer, audioBitstreamPosition + 7, frameSize - 7, audioFileTime + streamingStarted.Ticks, long.MinValue, VAST.Common.VersatileBufferFlag.AbsoluteTimestamp);

                        audioBitstreamPosition += frameSize;
                        audioFrameCount++;
                        audioFileTime = audioFrameCount * 10000000L * 1024 / audioMediaType.SampleRate;

                    }

                }
                while (videoFileTime <= playbackTime || audioFileTime <= playbackTime);

                Task.Delay(10).Wait();

            }

        }

#endif

        private void pushingRoutine3()
        {

            Bitmap bmpBackground = new Bitmap(new System.IO.MemoryStream(Properties.Resources.logo));
            Bitmap bmpImage = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Graphics g = Graphics.FromImage(bmpImage);
            Font font = new Font("Arial", 48, FontStyle.Bold);
            g.DrawImage(bmpBackground, 0, 0, 1280, 720);

            // prepare video encoder
            VAST.Common.MediaType uncompressedVideoMediaType = new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Video,
                CodecId = VAST.Common.Codec.Uncompressed,
                PixelFormat = VAST.Common.PixelFormat.BGRA,
                Width = 1280,
                Height = 720,
                Framerate = new VAST.Common.Rational(30),
            };

            VAST.Common.MediaType encodedVideoMediaType = new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Video,
                CodecId = VAST.Common.Codec.H264,
                Bitrate = 4000000,
                Width = 1280,
                Height = 720,
                Framerate = new VAST.Common.Rational(30),
            };

            VAST.Media.EncoderParameters encoderParameters = new VAST.Media.EncoderParameters
            {
                // set VAST.Common.MediaFramework.FFmpeg if you want to utilize FFmpeg for encoding
                PreferredMediaFramework = VAST.Common.MediaFramework.Builtin,
                // set to false if you want to utilize x264 encoder in FFmpeg
                AllowHardwareAcceleration = true,
            };

            VAST.Media.IEncoder encoder = VAST.Media.EncoderFactory.Create(uncompressedVideoMediaType, encodedVideoMediaType, encoderParameters);

            // get the actual video type from encoder
            encodedVideoMediaType = encoder.OutputMediaType;

            // create virtual network source
            VAST.Network.VirtualNetworkSource source = new VAST.Network.VirtualNetworkSource();
            // add video track
            int videoStreamIndex = source.AddStream(encodedVideoMediaType);

            // start the source and add create new publishing point with it
            this.server.CreatePublishingPoint("builtin", source);

            DateTime streamingStarted = DateTime.UtcNow;
            long videoFrameCount = 0;

            while (this.running)
            {

                // playback time in 100ns units
                long playbackTime = (long)(DateTime.UtcNow - streamingStarted).Ticks;
                // video timestamp converted to 100ns units
                long videoFileTime = videoFrameCount * 10000000L * encodedVideoMediaType.Framerate.Den / encodedVideoMediaType.Framerate.Num;

                while (videoFileTime <= playbackTime)
                {

                    // prepare a sample image
                    long started = VAST.Common.MediaTime.Get();
                    g.FillRectangle(Brushes.White, 250, 550, 780, 100);
                    g.DrawString(string.Format("{0:yyyy-MM-dd HH:mm:ss.fff}", DateTime.Now), font, Brushes.Purple, 250, 550);
                    g.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);

                    BitmapData bitmapData = bmpImage.LockBits(new Rectangle(0, 0, uncompressedVideoMediaType.Width, uncompressedVideoMediaType.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    int imageSize = bitmapData.Stride * bitmapData.Height;
                    VAST.Common.VersatileBuffer pixelData = VAST.Media.MediaGlobal.LockBuffer(imageSize);
                    pixelData.Append(bitmapData.Scan0, imageSize);
                    bmpImage.UnlockBits(bitmapData);

                    pixelData.ActualSize = imageSize;
                    pixelData.Dts = videoFileTime;
                    pixelData.Pts = videoFileTime;

                    // start image encoding
                    encoder.Write(pixelData);
                    pixelData.Release();

                    while (true)
                    {
                        // read encoded image
                        VAST.Common.VersatileBuffer encodedData = encoder.Read();
                        if (encodedData == null)
                        {
                            break;
                        }
                        else
                        {
                            // push encoded image to VirtualNetworkSource
                            encodedData.StreamIndex = videoStreamIndex;
                            source.PushMedia(encodedData);
                            encodedData.Release();
                        }
                    }

                    videoFrameCount++;
                    videoFileTime = videoFrameCount * 10000000L * encodedVideoMediaType.Framerate.Den / encodedVideoMediaType.Framerate.Num;

                }

                Task.Delay(10).Wait();

            }

        }

#if VAST_FEATURE_RTSP

        private void pushingRoutine4()
        {

            var source = new VAST.RTP.VirtualRtpSource();
            source.AddStream(new VAST.Common.MediaType
            {
                ContentType = VAST.Common.ContentType.Video,
                CodecId = VAST.Common.Codec.H264,
                Metadata = new Dictionary<string, string> { { "PayloadType", "96" } }
            });

            // start the source and add create new publishing point with it
            this.server.CreatePublishingPoint("builtin", source);

            byte[] dump = new byte[Properties.Resources.h264_rtpdump.Length];
            Properties.Resources.h264_rtpdump.CopyTo(dump, 0);

            long pushingStarted = VAST.Common.MediaTime.Get(1000);
            long timestampBase = long.MinValue;
            int pos = 13;
            while (dump[pos] != 0x0A) ++pos;
            pos += 17;
            int startPos = pos;

            while (this.running)
            {

                long now = VAST.Common.MediaTime.Get(1000);
                long pushingTime = now - pushingStarted;

                int fullLen = (dump[pos] << 8) | dump[pos + 1];
                pos += 2;
                int len = (dump[pos] << 8) | dump[pos + 1];
                if (len == 0)
                {
                    pos += fullLen - 2;
                    continue;
                }

                pos += 2;
                long timestamp = (dump[pos] << 24) | (dump[pos + 1] << 16) | (dump[pos + 2] << 8) | dump[pos + 3];
                pos += 4;

                if (timestampBase == long.MinValue)
                {
                    timestampBase = timestamp - pushingTime;
                }
                timestamp -= timestampBase;

                if (timestamp > pushingTime)
                {
                    int sleep = (int)(timestamp - pushingTime);
                    if (sleep == 0) sleep = 1;
                    Task.Delay(sleep).Wait();
                }

                source.PushRtpPacket(dump, pos, len);
                pos += len;

                if (pos >= dump.Length)
                {
                    pos = startPos;
                    timestampBase = long.MinValue;
                }

            }

        }

#endif

        private async void createCaptureSource()
        {

            VAST.Network.AggregatedNetworkSource source = null;

            try
            {

                source = new VAST.Network.AggregatedNetworkSource();

                List<VAST.Capture.VideoCaptureDeviceDescriptor> videoDevices = await VAST.Capture.VideoDeviceEnumerator.Enumerate();
                if (videoDevices.Count > 0)
                {

                    VAST.Capture.VideoCaptureDeviceDescriptor device = videoDevices[0];
                    VAST.Capture.VideoCaptureMode captureMode = null;
                    if (device.CaptureModes != null && device.CaptureModes.Count > 0)
                    {
                        captureMode = (device.DefaultCaptureMode >= 0) ? device.CaptureModes[device.DefaultCaptureMode] : device.CaptureModes[0];
                    }

                    VAST.Capture.IVideoCaptureSource2 videoSource = VAST.Media.SourceFactory.CreateVideoCapture(device.DeviceId, captureMode);

                    int width = (captureMode != null) ? captureMode.Width : 1280;
                    int height = (captureMode != null) ? captureMode.Height : 720;
                    VAST.Common.Rational framerate = new VAST.Common.Rational((captureMode != null) ? captureMode.Framerate : 30.0);

                    // set output parameters
                    VAST.Common.MediaType mt = new VAST.Common.MediaType
                    {
                        ContentType = VAST.Common.ContentType.Video,
                        CodecId = VAST.Common.Codec.H264,
                        Bitrate = width * height * 3,
                        Width = width,
                        Height = height,
                        Framerate = framerate,
                    };
                    mt.Metadata.Add("KeyframeInterval", "30");
                    // force baseline profile
                    mt.Metadata.Add("Profile", "66");

                    await videoSource.SetDesiredOutputType(0, mt);

                    // by default built-in Media Foundation framework will be used for encoding
                    // uncomment the below lines if you want to utilize FFmpeg instead
                    //videoSource.EncoderParameters = new VAST.Media.EncoderParameters { PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg, AllowHardwareAcceleration = false };

                    source.AddSource(videoSource);

                }

                List<VAST.Capture.AudioCaptureDeviceDescriptor> audioDevices = await VAST.Capture.AudioDeviceEnumerator.Enumerate();
                if (audioDevices.Count > 0)
                {

                    VAST.Capture.AudioCaptureDeviceDescriptor device = audioDevices[0];
                    VAST.Capture.AudioCaptureMode captureMode = null;
                    if (device.CaptureModes != null && device.CaptureModes.Count > 0)
                    {
                        captureMode = (device.DefaultCaptureMode >= 0) ? device.CaptureModes[device.DefaultCaptureMode] : device.CaptureModes[0];
                    }

                    VAST.Capture.IAudioCaptureSource2 audioSource = VAST.Media.SourceFactory.CreateAudioCapture(device.DeviceId, captureMode);

                    VAST.Common.MediaType mt = new VAST.Common.MediaType
                    {
                        ContentType = VAST.Common.ContentType.Audio,
                        CodecId = VAST.Common.Codec.AAC,
                        SampleRate = 44100,
                        Channels = 2,
                        Bitrate = 128000,
                    };

                    await audioSource.SetDesiredOutputType(0, mt);

                    // by default built-in Media Foundation framework will be used for encoding
                    // uncomment the below lines if you want to utilize FFmpeg instead
                    //audioSource.EncoderParameters = new VAST.Media.EncoderParameters { PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg, AllowHardwareAcceleration = false };

                    source.AddSource(audioSource);

                }

                // start the source and add create new publishing point with it
                this.server.CreatePublishingPoint("capture", source);
                source = null;

            }
            catch
            {
                // capture library is not present
            }
            finally
            {
                if (source != null) source.Dispose();
            }

        }

        private void createMixingPublishingPoint1()
        {

#if VAST_FEATURE_MIXING

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                VAST.Media.DecoderParameters.DefaultPreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                VAST.Media.DecoderParameters.DefaultAllowHardwareAcceleration = true;
                VAST.Media.EncoderParameters.DefaultPreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg;
                VAST.Media.EncoderParameters.DefaultAllowHardwareAcceleration = true;
            }

            VAST.Network.MixingSource source = new VAST.Network.MixingSource(this.server);

            source.Update(new VAST.Network.ApiPublishingPointRequest
            {
                Path = $"mixing",
                StreamingMode = VAST.Common.StreamingMode.Live,
                AllowVideoProcessing = false,
                NoSamplesTimeoutMs = 500,
                Sources = new List<VAST.Network.ApiSource>
                (
                    new VAST.Network.ApiSource[]
                    {
                    new VAST.Network.ApiSource { Uri = "vast://publishing-point/ingest" },
                    }
                ),
                Processing = new VAST.Network.ApiProcessing
                {
                    VideoProcessing = new VAST.Network.ApiVideoProcessing
                    {
                        Discard = false,
                        Transcoding = true,
                        Mixing = new VAST.Network.ApiVideoMixing
                        {
                            Type = VAST.Image.Mixing.VideoMixingType.Single,
                            SourceIndex = 0,
                        },
                        Tracks = new List<VAST.Network.ApiVideoTrack>
                        (
                            new VAST.Network.ApiVideoTrack[]
                            {
                            new VAST.Network.ApiVideoTrack
                            {
                                Index = 0,
                                Width = 1280,
                                Height = 720,
                                Framerate = new VAST.Common.Rational(30),
                                KeyframeInterval = 30,
                                Bitrate = 3000000,
                                Codec = VAST.Common.Codec.H264,
                                Profile = 0x100 | 66, // enforce constrained baseline profile
                            },
                            new VAST.Network.ApiVideoTrack
                            {
                                Index = 1,
                                Width = 848,
                                Height = 480,
                                Framerate = new VAST.Common.Rational(30),
                                KeyframeInterval = 30,
                                Bitrate = 1500000,
                                Codec = VAST.Common.Codec.H264,
                                Profile = 0x100 | 66, // enforce constrained baseline profile
                            },
                            new VAST.Network.ApiVideoTrack
                            {
                                Index = 2,
                                Width = 416,
                                Height = 240,
                                Framerate = new VAST.Common.Rational(30),
                                KeyframeInterval = 30,
                                Bitrate = 400000,
                                Codec = VAST.Common.Codec.H264,
                                Profile = 0x100 | 66, // enforce constrained baseline profile
                            },
                            }
                        )
                    },
                    AudioProcessing = new VAST.Network.ApiAudioProcessing
                    {
                        Discard = false,
                        Transcoding = true,
                        Mixing = new VAST.Network.ApiAudioMixing
                        {
                            Type = VAST.Image.Mixing.AudioMixingType.Single,
                            SourceIndex = 0,
                        },
                        Tracks = new List<VAST.Network.ApiAudioTrack>
                        (
                            new VAST.Network.ApiAudioTrack[]
                            {
                            new VAST.Network.ApiAudioTrack
                            {
                                Index = 3,
                                SampleRate = 44100,
                                Channels = 2,
                                Bitrate = 128000,
                                Codec = VAST.Common.Codec.AAC,
                            }
                            }
                        )
                    }
                }
            });

            this.server.CreatePublishingPoint($"mixing", source, new VAST.Network.PublishingPointParameters { InactivityTimeout = new TimeSpan(1, 0, 0) });

#endif

        }

        private void createProxySource(string publishingPath, string uri)
        {

            VAST.Media.IMediaSource originalSource = VAST.Media.SourceFactory.Create(uri);
            originalSource.StateChanged += (object sender, Media.MediaState e) =>
            {

                switch (e)
                {

                    case VAST.Media.MediaState.Opened:
                        {

                            var proxySource = new VAST.Media.ProxySource(originalSource);
                            proxySource.Open(); // ProxySource.Open() runs synchronously if original source is already opened

                            // initialize transcoders
                            for (int i = 0; i < proxySource.StreamCount; ++i)
                            {

                                VAST.Common.MediaType inputMediaType = proxySource.GetMediaType(i);
                                switch (inputMediaType.ContentType)
                                {

                                    case VAST.Common.ContentType.Audio:
                                        proxySource.SetDesiredOutputType(i, new VAST.Common.MediaType
                                        {
                                            ContentType = VAST.Common.ContentType.Audio,
                                            CodecId = VAST.Common.Codec.AAC,
                                            Bitrate = 128000,
                                            SampleRate = 44100,
                                            Channels = 2,
                                        }).Wait();
                                        break;

                                    case VAST.Common.ContentType.Video:
                                        VAST.Common.MediaType outputMediaType = new VAST.Common.MediaType
                                        {
                                            ContentType = VAST.Common.ContentType.Video,
                                            CodecId = VAST.Common.Codec.H264,
                                            Bitrate = inputMediaType.Width * inputMediaType.Height * 3,
                                            Width = inputMediaType.Width,
                                            Height = inputMediaType.Height,
                                            Framerate = inputMediaType.Framerate,
                                        };
                                        outputMediaType.Metadata.Add("KeyframeInterval", ((int)Math.Ceiling(inputMediaType.Framerate.ToDouble())).ToString());
                                        outputMediaType.Metadata.Add("Profile", (0x100 | 66).ToString()); // enforce constrained baseline profile
                                        proxySource.SetDesiredOutputType(i, outputMediaType).Wait();
                                        break;

                                }

                            }

                            this.server.CreatePublishingPoint(publishingPath, proxySource);
                            break;

                        }

                    case VAST.Media.MediaState.Error:
                        {
                            // TODO: process error
                            break;
                        }

                }

            };

            // initiate async open
            originalSource.Open();

        }

        private async void createScreenCaptureSource()
        {

            VAST.Capture.IScreenCaptureSource source = null;
            bool captureWholeScreen = true;

            try
            {

                // TODO: choose the display you need
                var display = VAST.Capture.DisplayHelper.EnumerateDisplays()[0];

                source = VAST.Media.SourceFactory.CreateScreenCapture();

                if (captureWholeScreen)
                {
                    // below initialization captures the whole selected screen
                    source.DeviceId = display.DeviceId;
                    source.Region = new VAST.Common.Rect { Left = display.Location.Left, Top = display.Location.Top, Right = display.Location.Right, Bottom = display.Location.Bottom };
                    source.ShowMouse = true;
                }
                else
                {
                    // capture a particular window
                    var window = VAST.Capture.DisplayHelper.FindWindow(display.DeviceId, "VLC")[0];
                    source.WindowHandle = window;
                    source.WindowCaptureMode = Capture.WindowCaptureMode.Resize;
                    source.ShowMouse = true;
                }

                int width = 1280;
                int height = 720;
                VAST.Common.Rational framerate = new VAST.Common.Rational(30);

                // set output parameters
                VAST.Common.MediaType mt = new VAST.Common.MediaType
                {
                    ContentType = VAST.Common.ContentType.Video,
                    CodecId = VAST.Common.Codec.H264,
                    Bitrate = width * height * 3,
                    Width = width,
                    Height = height,
                    Framerate = framerate,
                };
                mt.Metadata.Add("KeyframeInterval", "30");
                // force baseline profile
                mt.Metadata.Add("Profile", "66");

                // by default built-in Media Foundation framework will be used for encoding
                // uncomment the below lines if you want to utilize FFmpeg instead
                //videoSource.EncoderParameters = new VAST.Media.EncoderParameters { PreferredMediaFramework = VAST.Common.MediaFramework.FFmpeg, AllowHardwareAcceleration = false };

                await source.SetDesiredOutputType(0, mt);

                // start the source and add create new publishing point with it
                this.server.CreatePublishingPoint("screen", source);
                source = null;

            }
            catch
            {
                // capture library is not present
            }
            finally
            {
                if (source != null) source.Dispose();
            }

        }

    }

}
