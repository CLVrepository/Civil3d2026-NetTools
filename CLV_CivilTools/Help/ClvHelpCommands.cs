using System.Diagnostics;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Help
{
    public static class ClvHelpCommands
    {
        private const string KnowledgeHomePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\CLV CAD_KNOWLEDGE_BASE\index.html";

        [CommandMethod("CLVHELP", CommandFlags.Modal)]
        public static void OpenKnowledgeHomePage()
        {
            Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;

            try
            {
                if (!File.Exists(KnowledgeHomePath))
                {
                    ed?.WriteMessage($"\nCLVHELP: Knowledge homepage was not found at: {KnowledgeHomePath}");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = KnowledgeHomePath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                ed?.WriteMessage("\nCLVHELP: Opening CLV Civil Tools Knowledge Base...");
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage($"\nCLVHELP: Unable to open the Knowledge homepage. {ex.Message}");
            }
        }
    }
}
