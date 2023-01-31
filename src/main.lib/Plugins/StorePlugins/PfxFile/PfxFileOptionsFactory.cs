﻿using PKISharp.WACS.Extensions;
using PKISharp.WACS.Plugins.Base.Factories;
using PKISharp.WACS.Services;
using PKISharp.WACS.Services.Serialization;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.StorePlugins
{
    internal class PfxFileOptionsFactory : PluginOptionsFactory<PfxFileOptions>
    {
        private readonly ILogService _log;
        private readonly ArgumentsInputService _arguments;
        private readonly ISettingsService _settings;

        public PfxFileOptionsFactory(
            ILogService log,
            ISettingsService settings,
            ArgumentsInputService arguments)
        {
            _log = log;
            _arguments = arguments;
            _settings = settings;
        }

        private ArgumentResult<ProtectedString?> Password => _arguments.
            GetProtectedString<PfxFileArguments>(args => args.PfxPassword, true).
            WithDefault(PfxFile.DefaultPassword(_settings).Protect()).
            DefaultAsNull();

        private ArgumentResult<string?> Path => _arguments.
            GetString<PfxFileArguments>(args => args.PfxFilePath).
            WithDefault(PfxFile.DefaultPath(_settings)).
            Required().
            Validate(x => Task.FromResult(x.ValidPath(_log)), "invalid path").
            DefaultAsNull();

        private ArgumentResult<string?> Name => _arguments.
            GetString<PfxFileArguments>(args => args.PfxFileName);

        public override async Task<PfxFileOptions?> Aquire(IInputService input, RunLevel runLevel)
        {
            var path = await Path.Interactive(input, "File path").GetValue();
            var name = await Name.GetValue();
            var password = await Password.Interactive(input).GetValue();
            return Create(path, name, password);
        }

        public override async Task<PfxFileOptions?> Default()
        {
            var path = await Path.GetValue();
            var name = await Name.GetValue();
            var password = await Password.GetValue();
            return Create(path, name, password);
        }

        private static PfxFileOptions Create(string? path, string? name, ProtectedString? password)
        {
            return new PfxFileOptions
            {
                PfxPassword = password,
                Path = path,
                FileName = name
            };
        }
    }

}