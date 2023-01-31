﻿using PKISharp.WACS.DomainObjects;
using PKISharp.WACS.Plugins.Base.Capabilities;
using PKISharp.WACS.Plugins.Interfaces;
using PKISharp.WACS.Services.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.TargetPlugins
{
    [IPlugin.Plugin<
        ManualOptions, ManualOptionsFactory, 
        DefaultCapability, WacsJsonPlugins>
        ("e239db3b-b42f-48aa-b64f-46d4f3e9941b", 
        "Manual", ManualOptions.DescriptionText)]
    internal class Manual : ITargetPlugin
    {
        private readonly ManualOptions _options;

        public Manual(ManualOptions options) => _options = options;

        public async Task<Target?> Generate()
        {
            return new Target(
                $"[{nameof(Manual)}] {_options.CommonName}",
                _options.CommonName ?? "",
                new List<TargetPart> {
                    new TargetPart(_options.AlternativeNames.Select(ParseIdentifier))
                });
        }

        internal static Identifier ParseIdentifier(string identifier)
        {
            if (IPAddress.TryParse(identifier, out var address))
            {
                return new IpIdentifier(address);
            }
            return new DnsIdentifier(identifier);
        }
    }
}