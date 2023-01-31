﻿using ACMESharp.Authorizations;
using PKISharp.WACS.Context;
using PKISharp.WACS.Plugins.Interfaces;
using PKISharp.WACS.Services;
using PKISharp.WACS.Services.Serialization;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.ValidationPlugins.Http
{
    [IPlugin.Plugin<
        SelfHostingOptions, SelfHostingOptionsFactory, 
        SelfHostingCapability, WacsJsonPlugins>
        ("c7d5e050-9363-4ba1-b3a8-931b31c618b7", 
        "SelfHosting", "Serve verification files from memory")]
    internal class SelfHosting : Validation<Http01ChallengeValidationDetails>
    {
        internal const int DefaultHttpValidationPort = 80;
        internal const int DefaultHttpsValidationPort = 443;

        private readonly object _listenerLock = new();
        private HttpListener? _listener;
        private readonly ConcurrentDictionary<string, string> _files;
        private readonly SelfHostingOptions _options;
        private readonly ILogService _log;
        private readonly IUserRoleService _userRoleService;

        /// <summary>
        /// We can answer requests for multiple domains
        /// </summary>
        public override ParallelOperations Parallelism => ParallelOperations.Answer | ParallelOperations.Prepare;

        private bool HasListener => _listener != null;
        private HttpListener Listener
        {
            get
            {
                if (_listener == null)
                {
                    throw new InvalidOperationException("Listener not present");
                }
                return _listener;
            }
            set => _listener = value;
        }

        public SelfHosting(ILogService log, SelfHostingOptions options, IUserRoleService userRoleService)
        {
            _log = log;
            _options = options;
            _files = new ConcurrentDictionary<string, string>();
            _userRoleService = userRoleService;
        }

        private async Task ReceiveRequests()
        {
            while (HasListener && Listener.IsListening)
            {
                var ctx = await Listener.GetContextAsync();
                var path = ctx.Request.Url?.LocalPath ?? "";
                if (_files.TryGetValue(path, out var response))
                {
                    _log.Verbose("SelfHosting plugin serving file {name}", path);
                    using var writer = new StreamWriter(ctx.Response.OutputStream);
                    writer.Write(response);
                }
                else
                {
                    _log.Warning("SelfHosting plugin couldn't serve file {name}", path);
                    ctx.Response.StatusCode = 404;
                }
            }
        }

        public override Task PrepareChallenge(ValidationContext context, Http01ChallengeValidationDetails challenge)
        {
            // Add validation file
            _files.GetOrAdd("/" + challenge.HttpResourcePath, challenge.HttpResourceValue);
            return Task.CompletedTask;
        }

        public override Task Commit()
        {
            // Create listener if it doesn't exist yet
            lock (_listenerLock)
            {
                if (_listener == null)
                {
                    var protocol = _options.Https == true ? "https" : "http";
                    var port = _options.Port ?? (_options.Https == true ?
                        DefaultHttpsValidationPort :
                        DefaultHttpValidationPort);
                    var prefix = $"{protocol}://+:{port}/.well-known/acme-challenge/";
                    try
                    {
                        Listener = new HttpListener();
                        Listener.Prefixes.Add(prefix);
                        Listener.Start();
                        Task.Run(ReceiveRequests);
                    }
                    catch
                    {
                        _log.Error("Unable to activate listener, this may be because of insufficient rights or a non-Microsoft webserver using port {port}", port);
                        throw;
                    }
                }
            }
            return Task.CompletedTask;
        }

        public override Task CleanUp()
        {
            // Cleanup listener if nobody else has done it yet
            lock (_listenerLock)
            {
                if (HasListener)
                {
                    try
                    {
                        Listener.Stop();
                        Listener.Close();
                    }
                    finally
                    {
                        _listener = null;
                    }
                }
            }

            return Task.CompletedTask;
        }

        public (bool, string?) Disabled => IsDisabled(_userRoleService);

        internal static (bool, string?) IsDisabled(IUserRoleService userRoleService)
        {
            if (!userRoleService.AllowSelfHosting)
            {
                return (true, "Run as administrator to allow use of the built-in web listener.");
            }
            else
            {
                return (false, null);
            }
        }
    }
}
