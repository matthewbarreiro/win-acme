﻿using ACMESharp;
using ACMESharp.Protocol;
using Org.BouncyCastle.Crypto;
using PKISharp.WACS.Clients.Acme;
using PKISharp.WACS.DomainObjects;
using PKISharp.WACS.Plugins.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Bc = Org.BouncyCastle;

namespace PKISharp.WACS.Services
{
    internal class CertificateService : ICertificateService
    {
        private readonly IInputService _inputService;
        private readonly ILogService _log;
        private readonly ICacheService _cacheService;
        private readonly AcmeClient _client;
        private readonly PemService _pemService;
        private readonly CertificatePicker _picker;

        public CertificateService(
            ILogService log,
            AcmeClient client,
            PemService pemService,
            IInputService inputService,
            ICacheService cacheService,
            CertificatePicker picker)
        {
            _log = log;
            _client = client;
            _pemService = pemService;
            _cacheService = cacheService;
            _inputService = inputService;
            _picker = picker;
        }

        /// <summary>
        /// Request certificate from the ACME server
        /// </summary>
        /// <param name="csrPlugin">PluginBackend used to generate CSR if it has not been provided in the target</param>
        /// <param name="runLevel"></param>
        /// <param name="renewal"></param>
        /// <param name="target"></param>
        /// <param name="order"></param>
        /// <returns></returns>
        public async Task<ICertificateInfo> RequestCertificate(ICsrPlugin? csrPlugin, Order order)
        {
            if (order.Details == null)
            {
                throw new InvalidOperationException("No order details found");
            }
            // What are we going to get?
            var friendlyName = $"{order.FriendlyNameIntermediate} @ {_inputService.FormatDate(DateTime.Now)}";

            // Generate the CSR here, because we want to save it 
            // in the certificate cache folder even though we might
            // not need to submit it to the server in case of a 
            // cached order
            order.Target.CsrBytes = order.Target.UserCsrBytes;
            if (order.Target.CsrBytes == null)
            {
                if (csrPlugin == null)
                {
                    throw new InvalidOperationException("Missing CsrPlugin");
                }
                var csr = await csrPlugin.GenerateCsr(order.Target, order.KeyPath);
                var keySet = await csrPlugin.GetKeys();
                order.Target.CsrBytes = csr.GetDerEncoded();
                order.Target.PrivateKey = keySet.Private;
            }

            if (order.Target.CsrBytes == null)
            {
                throw new InvalidOperationException("No CsrBytes found");
            }
            await _cacheService.StoreCsr(order, _pemService.GetPem("CERTIFICATE REQUEST", order.Target.CsrBytes));

            // Check order status
            if (order.Details.Payload.Status != AcmeClient.OrderValid)
            {
                // Finish the order by sending the CSR to 
                // the server, which can then generate the
                // certificate.
                _log.Verbose("Submitting CSR");
                order.Details = await _client.SubmitCsr(order.Details, order.Target.CsrBytes);
                if (order.Details.Payload.Status != AcmeClient.OrderValid)
                {
                    _log.Error("Unexpected order status {status}", order.Details.Payload.Status);
                    throw new Exception($"Unable to complete order");
                }
            }

            // Download the certificate from the server
            _log.Information("Downloading certificate {friendlyName}", order.FriendlyNameIntermediate);
            var selected = await DownloadCertificate(order.Details, friendlyName, order.Target.PrivateKey);

            // Do post-processing on the private key. This is a legacy feature
            // to support Microsoft Exchange 2013 and can/should probably be
            // abandoned in a future release.
            if (csrPlugin != null)
            {
                try
                {
                    var collection = selected.WithPrivateKey.Collection;
                    var cert = collection.
                        OfType<X509Certificate2>().
                        Where(x => x.HasPrivateKey).
                        FirstOrDefault();
                    if (cert != null)
                    {
                        var certIndex = collection.IndexOf(cert);
                        var newVersion = await csrPlugin.PostProcess(cert);
                        if (newVersion != cert)
                        {
                            newVersion.FriendlyName = friendlyName;
                            collection[certIndex] = newVersion;
                            selected.WithPrivateKey = new CertificateInfo(collection);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning("Private key conversion error: {ex}", ex.Message);
                }
            }

            // Update LastFriendlyName so that the user sees
            // the most recently issued friendlyName in
            // the WACS GUI
            order.Renewal.LastFriendlyName = order.FriendlyNameBase;

            // Optionally store the certificate in cache
            // for future reuse. Will either return the original
            // in-memory certificate or a new cached instance with
            // pointer to a disk file (which may be used by some
            // installation scripts)
            var info = await _cacheService.StorePfx(order, selected);
            return info;
        }

        /// <summary>
        /// Download all potential certificates and pick the right one
        /// </summary>
        /// <param name="order"></param>
        /// <param name="friendlyName"></param>
        /// <param name="pk"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async Task<CertificateOption> DownloadCertificate(AcmeOrderDetails order, string friendlyName, AsymmetricKeyParameter? pk)
        {
            AcmeCertificate? certInfo;
            try
            {
                certInfo = await _client.GetCertificate(order);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unable to get certificate", ex);
            }
            if (certInfo == default || certInfo.Certificate == null)
            {
                throw new Exception($"Unable to get certificate");
            }
            var alternatives = new List<CertificateOption>
            {
                new CertificateOption(
                    withPrivateKey: ParseCertificate(certInfo.Certificate, friendlyName, pk),
                    withoutPrivateKey: ParseCertificate(certInfo.Certificate, friendlyName)
                )
            };
            if (certInfo.Links != null)
            {
                foreach (var alt in certInfo.Links["alternate"])
                {
                    try
                    {
                        var altCertRaw = await _client.GetCertificate(alt);
                        var altCert = new CertificateOption(
                            withPrivateKey: ParseCertificate(altCertRaw, friendlyName, pk),
                            withoutPrivateKey: ParseCertificate(altCertRaw, friendlyName)
                        );
                        alternatives.Add(altCert);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning("Unable to get alternate certificate: {ex}", ex.Message);
                    }
                }
            }
            return _picker.Select(alternatives);
        }
      
        /// <summary>
        /// Parse bytes to a usable certificate
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="friendlyName"></param>
        /// <param name="pk"></param>
        /// <returns></returns>
        private CertificateInfo ParseCertificate(byte[] bytes, string friendlyName, AsymmetricKeyParameter? pk = null)
        {
            // Build pfx archive including any intermediates provided
            _log.Verbose("Parsing certificate from {bytes} bytes received", bytes.Length);
            var text = Encoding.UTF8.GetString(bytes);
            var pfx = new Bc.Pkcs.Pkcs12Store();
            var startIndex = 0;
            const string startString = "-----BEGIN CERTIFICATE-----";
            const string endString = "-----END CERTIFICATE-----";
            while (true)
            {
                startIndex = text.IndexOf(startString, startIndex);
                if (startIndex < 0)
                {
                    break;
                }
                var endIndex = text.IndexOf(endString, startIndex);
                if (endIndex < 0)
                {
                    break;
                }
                endIndex += endString.Length;
                var pem = text[startIndex..endIndex];
                _log.Verbose("Parsing PEM data at range {startIndex}..{endIndex}", startIndex, endIndex);
                var bcCertificate = _pemService.ParsePem<Bc.X509.X509Certificate>(pem);
                if (bcCertificate != null)
                {
                    var bcCertificateEntry = new Bc.Pkcs.X509CertificateEntry(bcCertificate);
                    var bcCertificateAlias = startIndex == 0 ?
                        friendlyName :
                        bcCertificate.SubjectDN.ToString();
                    pfx.SetCertificateEntry(bcCertificateAlias, bcCertificateEntry);

                    // Assume that the first certificate in the reponse is the main one
                    // so we associate the private key with that one. Other certificates
                    // are intermediates
                    if (startIndex == 0 && pk != null)
                    {
                        var bcPrivateKeyEntry = new Bc.Pkcs.AsymmetricKeyEntry(pk);
                        pfx.SetKeyEntry(bcCertificateAlias, bcPrivateKeyEntry, new[] { bcCertificateEntry });
                    }
                }
                else
                {
                    _log.Warning("PEM data at range {startIndex}..{endIndex} could not be parsed as X509Certificate", startIndex, endIndex);
                }

                // This should never happen, but is a sanity check
                // not to get stuck in an infinite loop
                if (endIndex <= startIndex)
                {
                    _log.Error("Infinite loop detected, aborting");
                    break;
                }
                startIndex = endIndex;
            }

            var pfxStream = new MemoryStream();
            pfx.Save(pfxStream, null, new Bc.Security.SecureRandom());
            pfxStream.Position = 0;
            using var pfxStreamReader = new BinaryReader(pfxStream);

            var tempPfx = new X509Certificate2Collection();
            tempPfx.Import(
                pfxStreamReader.ReadBytes((int)pfxStream.Length),
                null,
                X509KeyStorageFlags.EphemeralKeySet |
                X509KeyStorageFlags.Exportable);
            return new CertificateInfo(tempPfx);
        }

        /// <summary>
        /// Revoke previously issued certificate
        /// </summary>
        /// <param name="binding"></param>
        public async Task RevokeCertificate(Renewal renewal)
        {
            // Delete cached files
            var infos = _cacheService.CachedInfos(renewal);
            var error = false;
            foreach (var info in infos)
            {
                try
                {
                    var certificateDer = info.Certificate.Export(X509ContentType.Cert);
                    await _client.RevokeCertificate(certificateDer);
                    _log.Warning($"Revoked certificate {info.Certificate.FriendlyName}");
                    info.CacheFile.Delete();
                }
                catch (Exception ex)
                {
                    error = true;
                    _log.Error(ex, $"Error revoking certificate {info.Certificate.FriendlyName}, please retry");
                }
            }
            if (!error)
            {
                _cacheService.Delete(renewal);
            }
        }
    }
}