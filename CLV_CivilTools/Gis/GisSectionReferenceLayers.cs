using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Gis
{
    public static class GisSectionReferenceLayers
    {
        private static readonly string[] SectionLayerFiles =
        {
            "CLV_Sections.layer"
        };

        private static readonly string[] SectionLayerNames =
        {
            "CLV_Sections"
        };

        [CommandMethod("CLV-GIS-DISPLAY-SECTIONS")]
        public static void DisplaySectionsCommand()
        {
            DisplaySections();
        }

        [CommandMethod("CLV-GIS-UNLOAD-SECTIONS")]
        public static void UnloadSectionsCommand()
        {
            UnloadSections();
        }

        public static void DisplaySections()
        {
            GisReferenceLayers.LoadReferenceLayerSet(
                SectionLayerFiles,
                SectionLayerNames,
                "PLSS section reference layer files");
        }

        public static void UnloadSections()
        {
            GisReferenceLayers.RemoveReferenceLayerSet(
                SectionLayerNames,
                "PLSS section reference layers");
        }
    }
}
