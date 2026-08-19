using System;
using System.IO;
using System.Reflection;

namespace CLV_CivilTools.LegalDescription
{
    internal static class EmbeddedLegalResourceService
    {
        private const string BasicTemplateResource = "CLV_CivilTools.Embedded.BasicTemplate.dotx";
        private const string TextStylesResource = "CLV_CivilTools.Embedded.LegalDescriptionTextStyles.json";
        private const string PhraseLibraryResource = "CLV_CivilTools.Embedded.LegalDescriptionPhraseLibrary.json";

        internal static Stream OpenBasicTemplate()
        {
            return OpenRequiredResource(BasicTemplateResource,
                "The embedded City Surveyor Word template is missing from the Civil Tools DLL. Rebuild the project and verify Basic Template.dotx is configured as an EmbeddedResource.");
        }

        internal static Stream OpenTextStyles()
        {
            return OpenRequiredResource(TextStylesResource,
                "The embedded legal-description text-style library is missing from the Civil Tools DLL. Rebuild the project and verify LegalDescriptionTextStyles.json is configured as an EmbeddedResource.");
        }

        internal static Stream OpenPhraseLibrary()
        {
            return OpenRequiredResource(PhraseLibraryResource,
                "The embedded legal-description phrase library is missing from the Civil Tools DLL. Rebuild the project and verify LegalDescriptionPhraseLibrary.json is configured as an EmbeddedResource.");
        }

        private static Stream OpenRequiredResource(string resourceName, string errorMessage)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new InvalidOperationException(errorMessage);
            return stream;
        }
    }
}
