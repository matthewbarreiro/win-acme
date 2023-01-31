﻿using PKISharp.WACS.Plugins.Interfaces;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace PKISharp.WACS.Plugins
{
    /// <summary>
    /// Metadata for a specific plugin
    /// </summary>
    [DebuggerDisplay("{Backend.Name}")]
    public class Plugin
    {
        public Guid Id { get; set; }
        public Steps Step { get; set; }
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type Backend { get; set; }
        private IPluginMeta Meta { get; set; }
        public string Name => Meta.Name;
        public string Description => Meta.Description;
        public bool Hidden => Meta.Hidden;
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type Options => Meta.Options;
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type OptionsFactory => Meta.OptionsFactory;
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type OptionsJson => Meta.OptionsJson;
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type Capability => Meta.Capability;

        public Plugin([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type source, IPluginMeta meta, Steps step)
        {
            Id = meta.Id;
            Backend = source;
            Meta = meta;
            Step = step;
        }
    }
}
