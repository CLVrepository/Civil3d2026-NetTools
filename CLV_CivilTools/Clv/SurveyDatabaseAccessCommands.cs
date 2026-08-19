using System;
using System.Diagnostics;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Clv
{
    public static class SurveyDatabaseAccessCommands
    {
        private const string DefaultFallbackPin = "8495";

        private static readonly UserAccessProfile ProjectsProfile =
            new("PROJECTS", "PROJECTS-USERS.md", DefaultFallbackPin);

        private static readonly UserAccessProfile TechnicalReviewsProfile =
            new("TECHNICAL REVIEWS", "TECHNICAL REVIEWS-USERS.md", DefaultFallbackPin);

        private const string DefaultProjectsLaunchPath = @"F:\PW_Survey_ROW\Job Database\Projects.xlsm";
        private const string DefaultTechnicalReviewsLaunchPath = @"F:\PW_Survey_ROW\Job Database\Technical Reviews.xlsm";

        private static readonly UserAccessProfile CreateDataProfile =
            new("CREATE DATA", "CREATE DATA-USERS.md", DefaultFallbackPin);

        [CommandMethod("CLV-PROJECTS", CommandFlags.Modal)]
        [CommandMethod("PROJECTS", CommandFlags.Modal)]
        public static void OpenProjects()
        {
            RunAuthorizedLaunch(ProjectsProfile);
        }

        [CommandMethod("CLV-TECHNICALREVIEWS", CommandFlags.Modal)]
        [CommandMethod("TECHNICALREVIEWS", CommandFlags.Modal)]
        [CommandMethod("TECHREVIEWS", CommandFlags.Modal)]
        public static void OpenTechnicalReviews()
        {
            RunAuthorizedLaunch(TechnicalReviewsProfile);
        }

        [CommandMethod("CLV-PROJECTS-LAUNCH", CommandFlags.Modal)]
        public static void LaunchProjectsTarget()
        {
            OpenDefaultTarget(ProjectsProfile);
        }

        [CommandMethod("CLV-TECHNICALREVIEWS-LAUNCH", CommandFlags.Modal)]
        public static void LaunchTechnicalReviewsTarget()
        {
            OpenDefaultTarget(TechnicalReviewsProfile);
        }

        internal static bool CanOpenCreateData(Editor? editor, out string detail)
        {
            return UserAccessControl.Authorize(CreateDataProfile, editor, out detail);
        }

        private static void RunAuthorizedLaunch(UserAccessProfile profile)
        {
            Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;

            if (!UserAccessControl.Authorize(profile, ed, out string detail))
            {
                ed?.WriteMessage($"\n{profile.DisplayName} access denied.");
                return;
            }

            if (TryExecuteConfiguredLaunch(profile, doc, ed))
            {
                ed?.WriteMessage($"\n{profile.DisplayName}: access granted via {detail}.");
                return;
            }

            string usersFilePath = UserAccessControl.GetUsersFilePath(profile);
            ed?.WriteMessage(
                $"\n{profile.DisplayName}: access granted via {detail}, but the launch target could not be opened."
              + $"\nEdit this file to override the default target with COMMAND:, PATH:, or URL: {usersFilePath}");
        }

        private static bool TryExecuteConfiguredLaunch(UserAccessProfile profile, Document? doc, Editor? ed)
        {
            string? command = GetLaunchCommand(profile, ed);

            if (!string.IsNullOrWhiteSpace(command) && doc != null)
            {
                doc.SendStringToExecute(command.EndsWith(" ", StringComparison.Ordinal) ? command : command + " ", true, false, false);
                return true;
            }

            string? path = UserAccessControl.TryGetSetting(profile, "PATH", ed) ?? GetDefaultPath(profile);
            if (!string.IsNullOrWhiteSpace(path) && OpenWithShell(path))
            {
                return true;
            }

            string? url = UserAccessControl.TryGetSetting(profile, "URL", ed);
            if (!string.IsNullOrWhiteSpace(url) && OpenWithShell(url))
            {
                return true;
            }

            return false;
        }

        private static string? GetLaunchCommand(UserAccessProfile profile, Editor? ed)
        {
            string? configuredCommand = UserAccessControl.TryGetSetting(profile, "COMMAND", ed);
            return string.IsNullOrWhiteSpace(configuredCommand)
                ? null
                : configuredCommand.Trim();
        }

        private static void OpenDefaultTarget(UserAccessProfile profile)
        {
            string? target = GetDefaultPath(profile);
            if (!string.IsNullOrWhiteSpace(target))
            {
                OpenWithShell(target);
            }
        }

        private static string? GetDefaultPath(UserAccessProfile profile)
        {
            if (ReferenceEquals(profile, ProjectsProfile))
            {
                return DefaultProjectsLaunchPath;
            }

            if (ReferenceEquals(profile, TechnicalReviewsProfile))
            {
                return DefaultTechnicalReviewsLaunchPath;
            }

            return null;
        }

        private static bool OpenWithShell(string target)
        {
            string normalized = Environment.ExpandEnvironmentVariables(target.Trim());
            if (!File.Exists(normalized)
                && !Directory.Exists(normalized)
                && !Uri.IsWellFormedUriString(normalized, UriKind.Absolute))
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = normalized,
                UseShellExecute = true
            };

            Process.Start(psi);
            return true;
        }
    }
}
