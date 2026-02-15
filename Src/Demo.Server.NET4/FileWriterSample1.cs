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

    class FileWriterSample1 : IDisposable
    {

        private VAST.Media.MediaSession writerSession = null;

        public FileWriterSample1(string sourceUri, string filePath)
        {

            VAST.Media.IMediaSource source = VAST.Media.SourceFactory.Create(sourceUri);
            if (source == null)
            {
                throw new Exception("Unsupported protocol");
            }

            VAST.Media.IMediaSink fileSink = new VAST.File.ISO.IsoSink();
            fileSink.Uri = filePath;

            this.writerSession = new VAST.Media.MediaSession();
            this.writerSession.AddSource(source);
            this.writerSession.AddSink(fileSink);
            this.writerSession.Error += (object sender, Media.ErrorEventArgs e) =>
            {
                // TODO: process error
            };

            this.writerSession.Start();

        }

        ~FileWriterSample1()
        {
            Dispose();
        }

        public void Dispose()
        {
            lock (this)
            {
                if (this.writerSession != null)
                {
                    this.writerSession.Dispose();
                    this.writerSession = null;
                }
            }
        }

    }

}
