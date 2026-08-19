using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace CLV_CivilTools.Shared
{
    internal sealed class UserAccessProfile
    {
        public UserAccessProfile(string displayName, string usersFileName, string fallbackPin)
        {
            DisplayName = displayName;
            UsersFileName = usersFileName;
            FallbackPin = fallbackPin;
        }

        public string DisplayName { get; }
        public string UsersFileName { get; }
        public string FallbackPin { get; }
    }

    internal sealed class UserAccessFile
    {
        public List<string> ApprovedUsers { get; } = new();
        public Dictionary<string, string> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal static class UserAccessControl
    {
        internal const string ServerAccessControlFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\AccessControl";

        private static readonly Regex LeadingBulletRegex = new(@"^\s*(?:[-*+]|\d+[.)])\s*", RegexOptions.Compiled);
        private static readonly Regex UsernameRegex = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

        public static string GetCurrentUserName()
        {
            return Environment.UserName?.Trim() ?? string.Empty;
        }

        public static bool Authorize(UserAccessProfile profile, Editor? editor, out string detail)
        {
            UserAccessFile file = LoadOrCreateUsersFile(profile, editor);
            string currentUser = GetCurrentUserName();

            if (!string.IsNullOrWhiteSpace(currentUser) &&
                file.ApprovedUsers.Any(x => string.Equals(x, currentUser, StringComparison.OrdinalIgnoreCase)))
            {
                detail = $"approved user '{currentUser}'";
                return true;
            }

            if (PromptForPin(profile, currentUser))
            {
                detail = "PIN fallback";
                return true;
            }

            detail = string.IsNullOrWhiteSpace(currentUser)
                ? "access denied"
                : $"user '{currentUser}' is not approved and PIN was not accepted";
            return false;
        }

        public static string? TryGetSetting(UserAccessProfile profile, string key, Editor? editor)
        {
            UserAccessFile file = LoadOrCreateUsersFile(profile, editor);
            return file.Settings.TryGetValue(key, out string? value) ? value : null;
        }

        public static string GetUsersFilePath(UserAccessProfile profile)
        {
            return Path.Combine(ServerAccessControlFolder, profile.UsersFileName);
        }

        private static UserAccessFile LoadOrCreateUsersFile(UserAccessProfile profile, Editor? editor)
        {
            string path = GetUsersFilePath(profile);
            if (!File.Exists(path))
            {
                editor?.WriteMessage($"\n[CLV] Access file not found: {path}");
                editor?.WriteMessage("\n[CLV] Add the markdown file on the shared AccessControl server folder.");
                return new UserAccessFile();
            }

            return ParseUsersFile(path);
        }

        private static UserAccessFile ParseUsersFile(string path)
        {
            var result = new UserAccessFile();

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal) ||
                    line.StartsWith(">", StringComparison.Ordinal) ||
                    line.StartsWith("//", StringComparison.Ordinal) ||
                    line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                int colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = line[..colonIndex].Trim();
                    string value = line[(colonIndex + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        result.Settings[key] = value;
                        continue;
                    }
                }

                string candidate = LeadingBulletRegex.Replace(line, string.Empty).Trim();
                candidate = candidate.Trim('`', '"', '\'', '[', ']');
                if (UsernameRegex.IsMatch(candidate))
                {
                    result.ApprovedUsers.Add(candidate);
                }
            }

            return result;
        }

        private static bool PromptForPin(UserAccessProfile profile, string currentUser)
        {
            using var form = new Form
            {
                Text = profile.DisplayName + " Access",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(335, 170),
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false
            };

            string labelText = string.IsNullOrWhiteSpace(currentUser)
                ? $"Enter PIN to open {profile.DisplayName}:"
                : $"User '{currentUser}' is not on the approved list.\nEnter PIN to open {profile.DisplayName}:";

            var lbl = new Label
            {
                AutoSize = false,
                Text = labelText,
                Left = 12,
                Top = 12,
                Width = 305,
                Height = 42
            };

            var txt = new TextBox
            {
                Left = 12,
                Top = 64,
                Width = 305,
                UseSystemPasswordChar = true
            };

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Left = 161,
                Top = 106,
                Width = 75
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = 242,
                Top = 106,
                Width = 75
            };

            form.Controls.Add(lbl);
            form.Controls.Add(txt);
            form.Controls.Add(btnOk);
            form.Controls.Add(btnCancel);
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            return form.ShowDialog() == DialogResult.OK
                && string.Equals(txt.Text.Trim(), profile.FallbackPin, StringComparison.Ordinal);
        }

    }
}
