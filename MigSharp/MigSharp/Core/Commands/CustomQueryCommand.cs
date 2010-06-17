﻿using System.Collections.Generic;

using MigSharp.Providers;

namespace MigSharp.Core.Commands
{
    internal class CustomQueryCommand : Command, IScriptableCommand
    {
        private readonly string _query;

        public string IfUsing { get; set; }

        public CustomQueryCommand(MigrateCommand parent, string query) : base(parent)
        {
            _query = query;
        }

        public IEnumerable<string> Script(IProvider provider, IProviderMetaData metaData)
        {
            if (!string.IsNullOrEmpty(IfUsing) && metaData.InvariantName != IfUsing) yield break;

            yield return _query;
        }
    }
}