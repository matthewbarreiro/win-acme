﻿using Google.Apis.Auth.OAuth2;
using Google.Apis.Dns.v1;
using Google.Apis.Dns.v1.Data;
using Google.Apis.Http;
using Google.Apis.Services;
using PKISharp.WACS.Clients.DNS;
using PKISharp.WACS.Extensions;
using PKISharp.WACS.Plugins.Base.Capabilities;
using PKISharp.WACS.Plugins.Interfaces;
using PKISharp.WACS.Services;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading.Tasks;

[assembly: SupportedOSPlatform("windows")]

namespace PKISharp.WACS.Plugins.ValidationPlugins.Dns
{
    [IPlugin.Plugin<
        CloudDnsOptions, CloudDnsOptionsFactory, 
        DnsValidationCapability, CloudDnsJson>
        ("B61505E9-1709-43FD-996F-C74C3686286C",
        "GCPDns", 
        "Create verification records in Google Cloud DNS")]
    internal class CloudDns: DnsValidation<CloudDns>
    {
        private readonly CloudDnsOptions _options;
        private readonly CloudDnsService _client;
        private readonly IProxyService _proxy;

        public CloudDns(
            LookupClientProvider dnsClient,
            ILogService log,
            IProxyService proxy,
            ISettingsService settings,
            CloudDnsOptions options) : base(dnsClient, log, settings)
        {
            _options = options;
            _proxy = proxy;
            _client = CreateDnsService();
        }

        private class ProxyFactory : HttpClientFactory
        {
            private readonly IProxyService _proxy;
            public ProxyFactory(IProxyService proxy) => _proxy = proxy;
            protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
            {
                return _proxy.GetHttpMessageHandler();
            }
        }

        private CloudDnsService CreateDnsService()
        {
            GoogleCredential credential;
            if (!_options.ServiceAccountKeyPath.ValidFile(_log))
            {
                throw new Exception("Configuration error");
            }
            using (var stream = new FileStream(_options.ServiceAccountKeyPath!, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream);
            }

            var dnsService = new DnsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                HttpClientFactory = new ProxyFactory(_proxy),
                ApplicationName = $"win-acme {VersionService.SoftwareVersion}",
            });

            return new CloudDnsService(dnsService);
        }

        public override async Task<bool> CreateRecord(DnsValidationRecord record)
        {
            var recordName = record.Authority.Domain;
            var token = record.Value;
            _log.Information("Creating TXT record {recordName} with value {token}", recordName, token);

            var zone = await GetManagedZone(_options.ProjectId, recordName);
            if (zone == null)
            {
                _log.Error("The zone could not be found in Google Cloud DNS.  DNS validation record not created");
                return false;
            }

            try
            {
                _ = await _client.CreateTxtRecord(_options.ProjectId ?? "", zone, recordName, token);
                return true;
            }
            catch(Exception ex)
            {
                _log.Warning("Error creating TXT record, {0}", ex.Message);
                return false;
            }
        }

        public override async Task DeleteRecord(DnsValidationRecord record)
        {

            var recordName = record.Authority.Domain;
            var zone = await GetManagedZone(_options.ProjectId, recordName);
            if (zone == null)
            {
                _log.Warning("Could not find zone '{0}' in project '{1}'", recordName, _options.ProjectId);
                return;
            }

            try
            {
                _ = await _client.DeleteTxtRecord(_options.ProjectId ?? "", zone, recordName);
                _log.Debug("Deleted TXT record");
            }
            catch (Exception ex)
            {
                _log.Warning("Error deleting TXT record, {0}", ex.Message);
                return;
            }
        }

        private async Task<ManagedZone?> GetManagedZone(string? projectId, string recordName)
        {
            var hostedZones = await _client.GetManagedZones(projectId ?? "");
            _log.Debug("Found {count} hosted zones in Google DNS", hostedZones.Count);

            var hostedZoneSets = hostedZones.Where(x => x.Visibility == "public").GroupBy(x => x.DnsName);
            var hostedZone = FindBestMatch(hostedZoneSets.ToDictionary(x => x.Key), recordName);
            if (hostedZone != null)
            {
                return hostedZone.First();
            }
            _log.Error($"Can't find hosted zone for domain {recordName}");
            return null;
        }
    }
}
