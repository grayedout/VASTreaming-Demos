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

    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Connections;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Server.Kestrel.Core;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    using System;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography.X509Certificates;

    public class Program
    {

        public static string DisplayText { get; set; } = "Hello World";

        public static void Main(string[] args)
        {

            // example of certificate installation for Linux

            // dotnet tool install --global dotnet-certificate-tool
            // ~/.dotnet/tools/certificate-tool add --file file.pfx --thumbprint thumbprint --password password
            // ~/.dotnet/corefx/cryptography/x509stores/my/

            // security import <password> -f pkcs12
            // first request it will prompt for keychain access

            // first of all let's initialize the log
            string folderName = System.IO.Path.GetDirectoryName(new Uri(AppContext.BaseDirectory).LocalPath);
            VAST.Common.Log.LogFileName = System.IO.Path.Combine(folderName, @"VAST.Demo.Server.log");
            VAST.Common.Log.LogLevel = VAST.Common.Log.Level.Debug;

            // enter the license
            VAST.Common.License.Key = "YOUR_LICENSE_KEY";

            // TODO: uncomment the sample to run it
#if VAST_FEATURE_CORESERVER
            MultiProtoServer sample = new();
#endif
            //SimpleRtmpServer sample = new SimpleRtmpServer();
            //SimpleRtspServer sample = new SimpleRtspServer();
            //SendSample1 sample = new SendSample1();
            //SendSample2 sample = new SendSample2();
            //ReceiveSample sample = new ReceiveSample("rtp://127.0.0.1:10000");
            //SimpleWebRtcServer sample = new SimpleWebRtcServer();
            //FileReaderSample sample = new FileReaderSample(@"C:\path\to\file.mp4");
            //FileWriterSample sample = new FileWriterSample("rtsp://127.0.0.1/test", @"C:\path\to\file.mp4");
            //IsoFragmentWriterSample sample = new IsoFragmentWriterSample("rtsp://127.0.0.1/test");

            var builder = WebApplication.CreateBuilder(args);

            try
            {
                if (Environment.GetEnvironmentVariable("VAST_HTTP_SERVER") == "Kestrel")
                {
                    builder.WebHost.UseKestrel(options =>
                    {
                        options.AllowSynchronousIO = true;
                        options.Listen(System.Net.IPAddress.Any, 8888);
                        // TODO: uncomment below code if you have configured TLS certificate
                        // TODO: TLS connection MUST BE used for WebTransport
                        //try
                        //{
                        //    using (var store = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new X509Store(StoreLocation.LocalMachine) : new X509Store(StoreName.My))
                        //    {
                        //        store.Open(OpenFlags.ReadOnly);
                        //        var certs = store.Certificates.Find(X509FindType.FindByThumbprint, "YOUR_CERTIFICATE_THUMBPRINT", true);
                        //        if (certs.Count > 0)
                        //        {
                        //            var certificate = certs[0];
                        //            options.Listen(System.Net.IPAddress.Any, 8443, listenOptions =>
                        //            {
                        //                listenOptions.UseHttps(certificate);
                        //                listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                        //            });
                        //        }
                        //        else
                        //        {
                        //            Console.WriteLine("No TLS certificates found");
                        //        }
                        //    }
                        //}
                        //catch (Exception ex)
                        //{
                        //    Console.WriteLine($"Cert exception {ex.ToString()}");
                        //}
                    });
                }
            }
            catch { }

            builder.Services.AddControllers();
            builder.Services.AddRazorPages();

            try
            {
                if (Environment.GetEnvironmentVariable("VAST_HTTP_SERVER") == "IIS")
                {
                    builder.Services.Configure<IISServerOptions>(options =>
                    {
                        options.AllowSynchronousIO = true;
                    });
                }
            }
            catch { }

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("Cross-Origin-Embedder-Policy", "require-corp");
                context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin");
                await next();
            });

            app.UseStaticFiles();
            app.UseRouting().UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapRazorPages();
            });

            app.UseWebSockets(new WebSocketOptions());

#if VAST_FEATURE_CORESERVER
            app.Run(async (context) =>
            {
                // dispatch unprocessed HTTP requests to the VASTreaming engine
                await sample.HttpDispatcher.DispatchRequest(new VAST.HTTP.AspHttpRequestContext(context));
            });
#endif

            app.Run();

            // the call is mandatory to stop internal monitoring threads, otherwise the app will not close
            VAST.Media.MediaGlobal.Uninitialize();

        }

    }

}
