﻿using Microsoft.TemplateEngine.Core.Contracts;

namespace Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Macros.Config
{
    public class GuidMacroConfig : IMacroConfig
    {
        public string VariableName { get; private set; }

        public string Type { get; private set; }

        public string Format { get; private set; }

        public static readonly string DefaultFormats = "ndbpxNDPBX";

        public GuidMacroConfig(string variableName, string format)
        {
            VariableName = variableName;
            Type = "guid";
            Format = format;
        }
    }
}
