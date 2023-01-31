﻿using PKISharp.WACS.DomainObjects;
using PKISharp.WACS.Plugins.TargetPlugins;
using PKISharp.WACS.Services.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PKISharp.WACS.Services
{
    internal class RenewalStoreDisk : IRenewalStoreBackend
    {
        private readonly WacsJson _wacsJson;
        private readonly ISettingsService _settings;
        private readonly IDueDateService _dueDate;
        private readonly ILogService _log;

        public RenewalStoreDisk(
            ISettingsService settings,
            IDueDateService dueDate,
            ILogService log,
            WacsJson wacsJson) : base()
        {
            _dueDate = dueDate;
            _settings = settings;
            _log = log;
            _wacsJson = wacsJson;
        }

        /// <summary>
        /// Local cache to prevent superfluous reading and
        /// JSON parsing
        /// </summary>
        internal List<Renewal>? _renewalsCache;

        /// <summary>
        /// Parse renewals from store
        /// </summary>
        public IEnumerable<Renewal> Read()
        {
            if (_renewalsCache == null)
            {
                var list = new List<Renewal>();
                var di = new DirectoryInfo(_settings.Client.ConfigurationPath);
                var postFix = ".renewal.json";
                var renewalFiles = di.EnumerateFiles($"*{postFix}", SearchOption.AllDirectories);
                foreach (var rj in renewalFiles)
                {
                    try
                    {
                        // Just checking if we have write permission
                        using var writeStream = rj.OpenWrite();
                    }
                    catch (Exception ex)
                    {
                        _log.Warning("No write access to all renewals: {reason}", ex.Message);
                        break;
                    }
                }
                foreach (var rj in renewalFiles)
                {
                    try
                    {
                        var text = File.ReadAllText(rj.FullName);
                        var result = JsonSerializer.Deserialize(text, _wacsJson.Renewal);
                        if (result == null)
                        {
                            throw new Exception("result is empty");
                        }
                        if (result.Id != rj.Name.Replace(postFix, ""))
                        {
                            throw new Exception($"mismatch between filename and id {result.Id}");
                        }
                        if (result.TargetPluginOptions == null || result.TargetPluginOptions.Plugin == null)
                        {
                            throw new Exception("missing source plugin options");
                        }
                        if (result.ValidationPluginOptions == null || result.ValidationPluginOptions.Plugin == null)
                        {
                            throw new Exception("missing validation plugin options");
                        }
                        if (result.StorePluginOptions == null)
                        {
                            throw new Exception("missing store plugin options");
                        }
                        if (result.CsrPluginOptions == null && result.TargetPluginOptions is not CsrOptions)
                        {
                            throw new Exception("missing csr plugin options");
                        }
                        if (result.InstallationPluginOptions == null)
                        {
                            throw new Exception("missing installation plugin options");
                        }
                        if (string.IsNullOrEmpty(result.LastFriendlyName))
                        {
                            result.LastFriendlyName = result.FriendlyName;
                        }
                        if (result.History == null)
                        {
                            result.History = new List<RenewResult>();
                        }
                        list.Add(result);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Unable to read renewal {renewal}: {reason}", rj.Name, ex.Message);
                    }
                }
                _renewalsCache = list.OrderBy(_dueDate.DueDate).ToList();
            }
            return _renewalsCache;
        }

        /// <summary>
        /// Serialize renewal information to store
        /// </summary>
        /// <param name="BaseUri"></param>
        /// <param name="Renewals"></param>
        public void Write(IEnumerable<Renewal> Renewals)
        {
            var list = Renewals.ToList();
            list.ForEach(renewal =>
            {
                if (renewal.Deleted)
                {
                    var file = RenewalFile(renewal, _settings.Client.ConfigurationPath);
                    if (file != null && file.Exists)
                    {
                        file.Delete();
                    }
                }
                else if (renewal.Updated || renewal.New)
                {
                    var file = RenewalFile(renewal, _settings.Client.ConfigurationPath);
                    if (file != null)
                    {
                        try
                        {
                            var renewalContent = JsonSerializer.Serialize(renewal, _wacsJson.Renewal);
                            if (string.IsNullOrWhiteSpace(renewalContent))
                            {
                                throw new Exception("Serialization yielded empty result");
                            }
                            if (file.Exists)
                            {
                                File.WriteAllText(file.FullName + ".new", renewalContent);
                                File.Replace(file.FullName + ".new", file.FullName, file.FullName + ".previous", true);
                                File.Delete(file.FullName + ".previous");
                            } 
                            else
                            {
                                File.WriteAllText(file.FullName, renewalContent);
                            }

                        } 
                        catch (Exception ex)
                        {
                            _log.Error(ex, "Unable to write {renewal} to disk", renewal.LastFriendlyName);
                        }
                    }
                    renewal.New = false;
                    renewal.Updated = false;
                }
            });
            _renewalsCache = list.Where(x => !x.Deleted).OrderBy(_dueDate.DueDate).ToList();
        }

        /// <summary>
        /// Determine location and name of the history file
        /// </summary>
        /// <param name="renewal"></param>
        /// <param name="configPath"></param>
        /// <returns></returns>
        private static FileInfo RenewalFile(Renewal renewal, string configPath) => new(Path.Combine(configPath, $"{renewal.Id}.renewal.json"));
    }
}
