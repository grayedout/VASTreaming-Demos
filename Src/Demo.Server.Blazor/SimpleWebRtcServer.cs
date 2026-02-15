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

    class SimpleWebRtcServer : IDisposable
    {

        private VAST.WebRTC.WebRtcStandaloneSignalingServer server = null;

        public SimpleWebRtcServer()
        {
            this.server = new VAST.WebRTC.WebRtcStandaloneSignalingServer(8888, 0, "/");
            this.server.Authorize += Server_Authorize;
            this.server.Start();
        }

        ~SimpleWebRtcServer()
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

        private void Server_Authorize(object sender, VAST.WebRTC.WebRtcStandaloneSignalingServer.AuthorizeEventArgs e)
        {
            // connection type has been detected as well as all other parameters
            // user code has to choose whether connection is valid and set Accept property accordingly
            e.Accept = true;
        }

    }

}
