//
// Copyright (C) 2010 Jackson Harper (jackson@manosdemono.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
//


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Manos.IO;
using Mono.Unix.Native;
#if !DISABLE_POSIX

#endif

namespace Manos.Tool
{
    public class ServerCommand
    {
        private ManosApp app;
        private string application_assembly;

        private int? port;
        private int? securePort;

        public ServerCommand(Environment env) : this(env, new List<string>())
        {
        }

        public ServerCommand(Environment env, IList<string> args)
        {
            Environment = env;
            Arguments = args;
        }

        public Environment Environment { get; private set; }

        public IList<string> Arguments { get; set; }

        public string ApplicationAssembly
        {
            get
            {
                if (application_assembly == null)
                    return Path.GetFileName(Directory.GetCurrentDirectory()) + ".dll";
                return application_assembly;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");
                application_assembly = value;
            }
        }

        public int Port
        {
            get
            {
                if (port == null)
                    return 8080;
                return (int) port;
            }
            set
            {
                if (port <= 0)
                    throw new ArgumentException("port", "port must be greater than zero.");
                port = value;
            }
        }

        public int? SecurePort
        {
            get { return securePort; }
            set
            {
                if (securePort <= 0)
                    throw new ArgumentException("port", "port must be greater than zero.");
                securePort = value;
            }
        }

        public string User { get; set; }

        public string IPAddress { get; set; }

        public string CertificateFile { get; set; }

        public string KeyFile { get; set; }

        public string Browse { get; set; }

        public string DocumentRoot { get; set; }

        public void Run()
        {
            // Setup the document root
            if (DocumentRoot != null)
            {
                Directory.SetCurrentDirectory(Path.Combine(Directory.GetCurrentDirectory(), DocumentRoot));
            }

            // Load the config.
            ManosConfig.Load();

            app = Loader.LoadLibrary<ManosApp>(ApplicationAssembly, Arguments);

            Console.WriteLine("Running {0} on port {1}.", app, Port);

            if (User != null)
                SetServerUser(User);

            IPAddress listenAddress = IO.IPAddress.Any;

            if (IPAddress != null)
                listenAddress = IO.IPAddress.Parse(IPAddress);

            AppHost.ListenAt(new IPEndPoint(listenAddress, Port));
            if (SecurePort != null)
            {
                AppHost.InitializeTLS("NORMAL");
                AppHost.SecureListenAt(new IPEndPoint(listenAddress, SecurePort.Value), CertificateFile, KeyFile);
                Console.WriteLine("Running {0} on secure port {1}.", app, SecurePort);
            }

            if (Browse != null)
            {
                string hostname = IPAddress == null ? "http://localhost" : "http://" + IPAddress;
                if (Port != 80)
                    hostname += ":" + Port;

                if (Browse == "")
                {
                    Browse = hostname;
                }
                if (Browse.StartsWith("/"))
                {
                    Browse = hostname + Browse;
                }

                if (!Browse.StartsWith("http://") && !Browse.StartsWith("https://"))
                    Browse = "http://" + Browse;

                AppHost.AddTimeout(TimeSpan.FromMilliseconds(10), RepeatBehavior.Single, Browse, DoBrowse);
            }

            AppHost.Start(app);
        }

        private static void DoBrowse(ManosApp app, object user_data)
        {
            var BrowseTo = user_data as string;
            Console.WriteLine("Launching {0}", BrowseTo);
            Process.Start(BrowseTo);
        }

        public void SetServerUser(string user)
        {
            if (user == null)
                throw new ArgumentNullException("user");

            PlatformID pid = System.Environment.OSVersion.Platform;
            if (pid != PlatformID.Unix /* && pid != PlatformID.MacOSX */)
            {
                // TODO: Not sure if this works on OSX yet.

                //
                // Throw an exception here, we don't want to silently fail
                // otherwise people might be unknowingly running as root
                //

                throw new InvalidOperationException("User can not be set on Windows platforms.");
            }

            AppHost.AddTimeout(TimeSpan.Zero, RepeatBehavior.Single, user, DoSetUser);
        }


        private void DoSetUser(ManosApp app, object user_data)
        {
#if DISABLE_POSIX
			throw new InvalidOperationException ("Attempt to set user on a non-posix build.");
#else
            var user = user_data as string;

            Console.WriteLine("setting user to: '{0}'", user);

            if (user == null)
            {
                AppHost.Stop();
                throw new InvalidOperationException(String.Format("Attempting to set user to null."));
            }

            Passwd pwd = Syscall.getpwnam(user);
            if (pwd == null)
            {
                AppHost.Stop();
                throw new InvalidOperationException(String.Format("Unable to find user '{0}'.", user));
            }

            int error = Syscall.seteuid(pwd.pw_uid);
            if (error != 0)
            {
                AppHost.Stop();
                throw new InvalidOperationException(String.Format("Unable to switch to user '{0}' error: '{1}'.", user,
                                                                  error));
            }

#endif
        }
    }
}