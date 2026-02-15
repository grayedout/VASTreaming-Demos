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

    class SimpleWebRtcServer : IDisposable
    {

        private VAST.WebRTC.WebRtcStandaloneSignalingServer server = null;
        private VAST.ICE.TurnServer turnServer = null;

        public SimpleWebRtcServer()
        {

            // TODO: replace it with your actual public IPv4 address!
            string publicIPv4 = "YOUR_PUBLIC_IPV4_ADDRESS";

            // TODO: assign your actual public IPv6 address if you have one
            string publicIPv6 = null;

            var parameters = new VAST.WebRTC.WebRtcServerParameters
            {
                IceServers = "stun:stun.l.google.com:19302",
            };

            if (!string.IsNullOrEmpty(publicIPv4) && publicIPv4 != "YOUR_PUBLIC_IPV4_ADDRESS")
            {

                // using our own TURN server
                parameters.IceServers = $"turn:user:password@{publicIPv4}:3478";

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

            this.server = new VAST.WebRTC.WebRtcStandaloneSignalingServer(8888, 0, "/rtc", parameters);
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
