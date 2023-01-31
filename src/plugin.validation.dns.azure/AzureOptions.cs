﻿using PKISharp.WACS.Plugins.Azure.Common;
using PKISharp.WACS.Plugins.Base.Options;
using PKISharp.WACS.Services.Serialization;
using System.Text.Json.Serialization;

namespace PKISharp.WACS.Plugins.ValidationPlugins.Dns
{
    [JsonSerializable(typeof(AzureOptions))]
    internal partial class AzureJson : JsonSerializerContext 
    {
        public AzureJson(WacsJsonPluginsOptionsFactory optionsFactory) : base(optionsFactory.Options) { }
    }

    internal class AzureOptions : ValidationPluginOptions, IAzureOptionsCommon
    {
        public string? AzureEnvironment { get; set; }
        public bool UseMsi { get; set; }
        public string? ClientId { get; set; }
        public string? ResourceGroupName { get; set; }

        [JsonPropertyName("SecretSafe")]
        public ProtectedString? Secret { get; set; }

        public string? SubscriptionId { get; set; }
        public string? TenantId { get; set; }
        public string? HostedZone { get; set; }
    }
}
