using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PKISharp.WACS.Services.Legacy
{
    internal class LegacyScheduledRenewal
    {
        /// <summary>
        /// Next scheduled renew date
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Information about the certificate
        /// </summary>
        public LegacyTarget? Binding { get; set; }

        /// <summary>
        /// Location of the Central SSL store (if not specified, certificate
        /// is stored in the Certificate store instead). This takes priority
        /// over CertificateStore
        /// </summary>
        [JsonPropertyName("CentralSsl")]
        public string? CentralSslStore { get; set; }

        /// <summary>
        /// Name of the certificate store to use
        /// </summary>
        public string? CertificateStore { get; set; }

        /// <summary>
        /// Legacy, replaced by HostIsDns parameter on MainTarget
        /// </summary>
        public bool? San { get; set; }

        /// <summary>
        /// Do not delete previously issued certificate
        /// </summary>
        public bool? KeepExisting { get; set; }

        /// <summary>
        /// Name of the plugins to use for validation, in order of execution
        /// </summary>
        public List<string>? InstallationPluginNames { get; set; }

        /// <summary>
        /// Script to run on succesful renewal
        /// </summary>
        public string? Script { get; set; }

        /// <summary>
        /// Parameters for script
        /// </summary>e
        public string? ScriptParameters { get; set; }

        /// <summary>
        /// Warmup target website (applies to http-01 validation)
        /// TODO: remove
        /// </summary>
        public bool Warmup { get; set; }

        /// <summary>
        /// Pretty format
        /// </summary>
        /// <returns></returns>
        public override string ToString() => $"{Binding?.Host ?? "[unknown]"} - renew after {Date}";
    }
}
