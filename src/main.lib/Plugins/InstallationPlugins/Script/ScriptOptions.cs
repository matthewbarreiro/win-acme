﻿using PKISharp.WACS.Plugins.Base;
using PKISharp.WACS.Plugins.Base.Options;
using PKISharp.WACS.Services;

namespace PKISharp.WACS.Plugins.InstallationPlugins
{
    internal class ScriptOptions : InstallationPluginOptions
    {
        public string? Script { get; set; }
        public string? ScriptParameters { get; set; }

        /// <summary>
        /// Show details to the user
        /// </summary>
        /// <param name="input"></param>
        public override void Show(IInputService input)
        {
            base.Show(input);
            input.Show("Script", Script, level: 2);
            if (!string.IsNullOrEmpty(ScriptParameters))
            {
                input.Show("ScriptParameters", ScriptParameters, level: 2);
            }
        }
    }
}
