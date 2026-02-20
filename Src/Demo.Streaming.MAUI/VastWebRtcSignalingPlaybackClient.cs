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
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using VAST.Common;
    using VAST.Media;
    using VAST.WebRTC;

    public class VastSignalingPlaybackClient : IWebRtcSignalling, IDisposable
    {

        public enum State
        {
            Inactive,
            Starting,
            Started,
            Stopping,
            Stopped,
            Error,
        }

        private Guid uniqueId = Guid.NewGuid();
        private volatile State state = State.Inactive;
        private volatile bool stopping = false;
        private string signallingServerUri = null;
        private string publishingPath = null;
        private ClientWebSocket ws = null;
        private CancellationTokenSource recvCancellationToken = new CancellationTokenSource();
        private CancellationTokenSource sendCancellationToken = new CancellationTokenSource();
        private Task receiveTask = null;
        private volatile bool receiveTaskFinished = false;
        private Task sendPingTask = null;
        private long ourPeerId = 0;
        private long serverPeerId = 0;
        private WebRtcClientSource webRtcSource = null;
        private IMediaPlayer player = null;

        public VastSignalingPlaybackClient(IMediaPlayer player, string signallingServerUri, string publishingPath)
        {
            this.player = player;
            this.signallingServerUri = signallingServerUri;
            this.publishingPath = publishingPath;
        }

        ~VastSignalingPlaybackClient()
        {
            this.Dispose();
        }

        public void Dispose()
        {
            this.Stop();
        }

        /// <summary>
        /// Error occurred
        /// </summary>
        public event EventHandler<ErrorEventArgs> Error;

        public string IceServers { get; set; } = "stun:stun.l.google.com:19302";

        public State CurrentState { get { return this.state; } }

        public void Start()
        {
            Monitor.Enter(this);
            try
            {
                if (this.state >= State.Starting) return;
                this.state = State.Starting;
                this.receiveTask = Task.Factory.StartNew(() => receiveRoutine(), TaskCreationOptions.LongRunning);
            }
            finally
            {
                Monitor.Exit(this);
            }
        }

        public void Stop()
        {

            this.stopping = true;
            Monitor.Enter(this);

            try
            {

                if (this.state < State.Stopping)
                {
                    this.state = State.Stopping;
                }

                if (this.recvCancellationToken != null && !this.recvCancellationToken.IsCancellationRequested)
                {
                    try { this.recvCancellationToken.Cancel(); } catch { }
                }

                if (this.sendCancellationToken != null && !this.sendCancellationToken.IsCancellationRequested)
                {
                    try { this.sendCancellationToken.Cancel(); } catch { }
                }

                if (this.receiveTask != null)
                {
                    while (!this.receiveTaskFinished)
                    {
                        Task.Delay(10).Wait();
                    }
                    try { this.receiveTask.Wait(); } catch { }
                    this.receiveTask = null;
                }

                if (this.sendPingTask != null)
                {
                    try { this.sendPingTask.Wait(); } catch { }
                    this.sendPingTask = null;
                }

                this.stop().Wait();

                if (this.recvCancellationToken != null)
                {
                    this.recvCancellationToken.Dispose();
                    this.recvCancellationToken = null;
                }

                if (this.sendCancellationToken != null)
                {
                    this.sendCancellationToken.Dispose();
                    this.sendCancellationToken = null;
                }

                this.player = null;

                if (this.state < State.Stopped)
                {
                    this.state = State.Stopped;
                }

            }
            catch (Exception ex)
            {
                Log.ErrorFormat(this.uniqueId, "Stop exception: {0}", ex);
            }
            finally
            {
                Monitor.Exit(this);
            }

        }

        // IWebRtcSignalling implementation
        public void SendToPeer(object caller, long peerId, string data)
        {
            this.sendToPeer("message", peerId, data);
        }

        private async void receiveRoutine()
        {

            try
            {

                Log.DebugFormat(this.uniqueId, "Started receive task");

                // 100k is enough to receive any kind of message from signalling server
                byte[] recvBuffer = new byte[102400];
                int recvBufferPos = 0;

                while (!this.stopping)
                {

                    try
                    {

                        if (this.ws == null)
                        {
                            if (!await this.start())
                            {
                                break;
                            }
                        }

                        var arr = new ArraySegment<byte>(recvBuffer, recvBufferPos, recvBuffer.Length - recvBufferPos);
                        WebSocketReceiveResult result = await this.ws.ReceiveAsync(arr, this.recvCancellationToken.Token);
                        if (this.stopping) break;

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Log.DebugFormat("Web socket has been closed");
                            break;
                        }
                        else if (result.EndOfMessage)
                        {

                            // normally completed
                            //Log.DebugFormat(this.uniqueId, "Receive from {0} completed", client.PeerId);
                            recvBufferPos += result.Count;

                            string dataString = null;
                            GeneralMessage data = null;
                            if (recvBufferPos > 0)
                            {
                                dataString = Encoding.UTF8.GetString(recvBuffer, 0, recvBufferPos);
                                //Log.DebugFormat(this.uniqueId, "Web socket message:\n{0}", s);
                                if (!string.IsNullOrEmpty(dataString))
                                {
                                    data = GeneralMessage.Deserialize(dataString);
                                }
                                recvBufferPos = 0;
                            }

                            if (data != null)
                            {

                                bool lockAcquired = true;
                                while (!Monitor.TryEnter(this, 10))
                                {
                                    if (this.stopping)
                                    {
                                        lockAcquired = false;
                                        break;
                                    }
                                }

                                if (lockAcquired)
                                {

                                    try
                                    {

                                        switch (data.DataType)
                                        {

                                            case "peer-list":
                                                {

                                                    List<PeerDescriptor> peers = data.PeerList;
                                                    this.ourPeerId = peers[0].Id;
                                                    Log.DebugFormat(this.uniqueId, "Local peer id: {0}", this.ourPeerId);

                                                    this.serverPeerId = peers[1].Id;
                                                    Log.DebugFormat(this.uniqueId, "Server peer id: {0}", this.serverPeerId);

                                                    this.sendPingTask = Task.Run(async () =>
                                                    {
                                                        while (!this.stopping)
                                                        {
                                                            await Task.Delay(10000, this.sendCancellationToken.Token);
                                                            if (this.stopping) break;
                                                            this.sendToPeer("ping", 1, null);
                                                        }
                                                    });

                                                    break;

                                                }

                                            case "message":
                                                {

                                                    if (string.IsNullOrEmpty(data.Data))
                                                    {
                                                        Log.WarnFormat(this.uniqueId, "Peer {0} empty message", data.From);
                                                        break;
                                                    }

                                                    long peerId = data.From;
                                                    if (peerId != this.serverPeerId)
                                                    {
                                                        Log.WarnFormat(this.uniqueId, "Message from unknown peer {0}", peerId);
                                                    }

                                                    Log.DebugFormat(this.uniqueId, "Peer {0} received message: {1}", peerId, data.Data);
                                                    PeerMessage peerMessage = PeerMessage.Deserialize(data.Data);
                                                    if (!string.IsNullOrEmpty(peerMessage.MessageType) && peerMessage.MessageType == "offer")
                                                    {
                                                        // offer from remote peer, create new WebRTC source for it
                                                        this.createConnection(peerMessage, data.Data);
                                                    }
                                                    else if (!string.IsNullOrEmpty(peerMessage.Candidate))
                                                    {
                                                        this.webRtcSource.ProcessPeerMessage(peerId, data.Data);
                                                    }
                                                    else if (!string.IsNullOrEmpty(peerMessage.Action))
                                                    {
                                                        Log.WarnFormat(this.uniqueId, "Unsupported action: {0}", peerMessage.Action);
                                                    }
                                                    else
                                                    {
                                                        Log.WarnFormat(this.uniqueId, "Unsupported peer message: {0}", data.Data);
                                                    }

                                                    break;

                                                }

                                            case "action":
                                                {

                                                    PeerMessage peerMessage = PeerMessage.Deserialize(data.Data);
                                                    if (string.IsNullOrEmpty(peerMessage.Action))
                                                    {
                                                        Log.WarnFormat(this.uniqueId, "Peer {0} empty action", data.From);
                                                        break;
                                                    }

                                                    if (peerMessage.Action == "disconnect")
                                                    {
                                                        Log.InfoFormat(this.uniqueId, "Received server initiated disconnection");
                                                        Task t = Task.Run(() => this.Stop());
                                                    }

                                                    break;

                                                }

                                            default:
                                                {
                                                    Log.WarnFormat(this.uniqueId, "Received unsupported message:{0}", dataString);
                                                    break;
                                                }

                                        }

                                    }
                                    finally
                                    {
                                        Monitor.Exit(this);
                                    }

                                }

                            }

                        }
                        else
                        {
                            // incomplete message
                            recvBufferPos += result.Count;
                        }

                    }
                    catch (Exception ex)
                    {

                        Log.ErrorFormat(this.uniqueId, "Receive exception: {0}, re-connecting...", ex);

                        if (!this.stopping)
                        {
                            var t = Task.Factory.StartNew(() =>
                            {
                                // unexpected receive error, need to restart everything from scratch from another thread
                                IMediaPlayer player = this.player;
                                this.Stop();
                                this.player = player;
                                this.state = State.Inactive;
                                this.stopping = false;
                                this.recvCancellationToken = new CancellationTokenSource();
                                this.sendCancellationToken = new CancellationTokenSource();
                                this.Start();
                            }, TaskCreationOptions.LongRunning);
                        }

                    }

                }

                try
                {
                    await this.stop();
                }
                catch (Exception ex)
                {
                    Log.DebugFormat("Unexpected exception {0}", ex);
                }

            }
            finally
            {
                this.receiveTaskFinished = true;
                Log.DebugFormat(this.uniqueId, "Finished receive task");
            }

        }

        private async Task<bool> start()
        {
            try
            {
                // connect to signalling server
                this.ws = new ClientWebSocket();
                Uri uri = new Uri($"{this.signallingServerUri}/sign-in?channel={this.publishingPath}");
                await this.ws.ConnectAsync(uri, this.recvCancellationToken.Token);
                this.state = State.Started;
                return true;
            }
            catch (Exception ex)
            {
                Log.ErrorFormat(this.uniqueId, "Start exception: {0}", ex);
                this.state = State.Error;
                this.Error?.Invoke(this, new ErrorEventArgs { ErrorDescription = ex.Message, IsCritical = true });
                return false;
            }
        }

        private async void createConnection(PeerMessage peerMessage, string unparsedMessage)
        {

            Log.DebugFormat(this.uniqueId, "Received SDP offer, creating WebRTC source...");

            this.webRtcSource = new WebRtcClientSource();
            this.webRtcSource.Signalling = this;
            this.webRtcSource.OurPeerId = this.ourPeerId;
            this.webRtcSource.RemotePeerId = this.serverPeerId;
            this.webRtcSource.IceServers = this.IceServers;

            if (!string.IsNullOrEmpty(peerMessage.Sdp))
            {

                if (peerMessage.Sdp.Contains("H264/90000"))
                {
                    // dirty hack to prefer H.264 to VP8/VP9 when available
                    unparsedMessage = unparsedMessage.Replace("VP8/90000", "NONE8/90000").Replace("VP9/90000", "NONE9/90000");
                }

                if (peerMessage.Sdp.Contains("m=audio"))
                {
                    this.webRtcSource.IsAudioExpected = true;
                }

                if (peerMessage.Sdp.Contains("m=video"))
                {
                    this.webRtcSource.IsVideoExpected = true;
                }

            }

            this.webRtcSource.Open();

            while (!this.stopping && this.state == State.Starting)
            {

                if (this.webRtcSource.State < MediaState.Opened)
                {
                    await Task.Delay(10);
                    continue;
                }
                else if (this.webRtcSource.State >= MediaState.Stopped)
                {
                    Log.ErrorFormat(this.uniqueId, "Failed to open WebRTC source");
                    this.webRtcSource.Dispose();
                    this.webRtcSource = null;
                    return;
                }

                // if we're here then source is opened
                break;

            }

            if (this.stopping || this.state == State.Error)
            {
                return;
            }

            // source is opened, now we can set offer
            this.webRtcSource.ProcessPeerMessage(this.serverPeerId, unparsedMessage);

            // assign source to player
            this.player.SourceMedia = this.webRtcSource;
            this.player.Play();

        }

        private void sendToPeer(string dataType, long peerId, string data)
        {

            try
            {

                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("{{\"data-type\":\"{0}\",\"from\":{1},\"to\":{2}", dataType, this.ourPeerId, peerId);

                if (data != null)
                {
                    sb.AppendFormat(",\"data\":{0}", data);
                }

                sb.Append("}");
                Log.Debug(this.uniqueId, "Sending: " + sb.ToString().Replace("{", "{{").Replace("}", "}}"));
                this.ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(sb.ToString())), WebSocketMessageType.Text, true, this.sendCancellationToken.Token);

            }
            catch (Exception ex)
            {
                Log.ErrorFormat(this.uniqueId, "Send to peer exception: {0}", ex);
            }

        }

        private async Task stop()
        {

            if (this.player != null)
            {
                this.player.Stop();
                this.player.SourceMedia = null;
            }

            if (this.webRtcSource != null)
            {
                this.webRtcSource.Dispose();
                this.webRtcSource = null;
            }

            if (this.sendCancellationToken != null && !this.sendCancellationToken.IsCancellationRequested)
            {
                try { this.sendCancellationToken.Cancel(); } catch { }
            }

            if (this.ws != null)
            {
                if (this.ws.State == WebSocketState.Open)
                {
                    try { await this.ws.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None); } catch { }
                    try { await this.ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                }
                try { this.ws.Dispose(); } catch { }
                this.ws = null;
            }

        }

    }

}
