﻿using PKISharp.WACS.Clients;
using PKISharp.WACS.Clients.DNS;
using PKISharp.WACS.Plugins.Base.Capabilities;
using PKISharp.WACS.Plugins.Interfaces;
using PKISharp.WACS.Services;
using PKISharp.WACS.Services.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.ValidationPlugins.Dns
{
    [IPlugin.Plugin<
        ScriptOptions, ScriptOptionsFactory, 
        DnsValidationCapability, WacsJsonPlugins>
        ("8f1da72e-f727-49f0-8546-ef69e5ecec32", 
        "DnsScript", "Create verification records with your own script", 
        Hidden = true)]
    [IPlugin.Plugin<
        ScriptOptions, ScriptOptionsFactory,
        DnsValidationCapability, WacsJsonPlugins>
        ("8f1da72e-f727-49f0-8546-ef69e5ecec32", 
        "Script", "Create verification records with your own script")]
    internal class Script : DnsValidation<Script>
    {
        private readonly ScriptClient _scriptClient;
        private readonly ScriptOptions _options;
        private readonly DomainParseService _domainParseService;
        private readonly SecretServiceManager _ssm;

        internal const string DefaultCreateArguments = "create {Identifier} {RecordName} {Token}";
        internal const string DefaultDeleteArguments = "delete {Identifier} {RecordName} {Token}";

        public Script(
            ScriptOptions options,
            LookupClientProvider dnsClient,
            ScriptClient client,
            ILogService log,
            DomainParseService domainParseService,
            SecretServiceManager secretServiceManager,
            ISettingsService settings) :
            base(dnsClient, log, settings)
        {
            _options = options;
            _scriptClient = client;
            _domainParseService = domainParseService;
            _ssm = secretServiceManager;
        }

        public override ParallelOperations Parallelism => (ParallelOperations)(_options.Parallelism ?? 0);

        public override async Task<bool> CreateRecord(DnsValidationRecord record)
        {
            var script = _options.Script ?? _options.CreateScript;
            if (!string.IsNullOrWhiteSpace(script))
            {
                var args = DefaultCreateArguments;
                if (!string.IsNullOrWhiteSpace(_options.CreateScriptArguments))
                {
                    args = _options.CreateScriptArguments;
                }
                return await _scriptClient.RunScript(
                    script, 
                    ProcessArguments(
                        record.Context.Identifier, 
                        record.Authority.Domain, 
                        record.Value,
                        args, 
                        script.EndsWith(".ps1"), 
                        false));
            }
            else
            {
                _log.Error("No create script configured");
                return false;
            }
        }

        public override async Task DeleteRecord(DnsValidationRecord record)
        {
            var script = _options.Script ?? _options.DeleteScript;
            if (!string.IsNullOrWhiteSpace(script))
            {
                var args = DefaultDeleteArguments;
                if (!string.IsNullOrWhiteSpace(_options.DeleteScriptArguments))
                {
                    args = _options.DeleteScriptArguments;
                }
                var escapeToken = script.EndsWith(".ps1");
                var actualArguments = ProcessArguments(record.Context.Identifier, record.Authority.Domain, record.Value, args, escapeToken, false);
                var censoredArguments = ProcessArguments(record.Context.Identifier, record.Authority.Domain, record.Value, args, escapeToken, true);
                await _scriptClient.RunScript(script, actualArguments, censoredArguments);
            }
            else
            {
                _log.Warning("No delete script configured, validation record remains");
            }
        }

        private string ProcessArguments(string identifier, string recordName, string token, string args, bool escapeToken, bool censor)
        {
            var ret = args;
            // recordName: _acme-challenge.sub.domain.com
            // zoneName: domain.com
            // nodeName: _acme-challenge.sub

            // recordName: domain.com
            // zoneName: domain.com
            // nodeName: @

            var zoneName = _domainParseService.GetRegisterableDomain(identifier);
            var nodeName = "@";
            if (recordName.Length > zoneName.Length)
            {
                // Offset by one to prevent trailing dot
                var idx = recordName.Length - zoneName.Length - 1;
                if (idx != 0)
                {
                    nodeName = recordName[..idx];
                }
            }

            // Some tokens start with - which confuses Powershell. We did not want to 
            // make a breaking change for .bat or .exe files, so instead escape the 
            // token with double quotes, as Powershell discards the quotes anyway and 
            // thus it's functionally equivalant.
            if (escapeToken && (ret.Contains(" {Token} ") || ret.EndsWith(" {Token}")))
            {
                ret = ret.Replace("{Token}", "\"{Token}\"");
            }

            // Numbered parameters for backwards compatibility only,
            // do not extend for future updates
            return Regex.Replace(ret, "{.+?}", (m) => {
                return m.Value switch
                {
                    "{ZoneName}" => zoneName,
                    "{NodeName}" => nodeName,
                    "{Identifier}" => identifier,
                    "{RecordName}" => recordName,
                    "{Token}" => token,
                    var s when s.StartsWith($"{{{SecretServiceManager.VaultPrefix}") =>
                        censor ? s : _ssm.EvaluateSecret(s.Trim('{', '}')) ?? s,
                    _ => m.Value
                };
            });
        }
    }
}
