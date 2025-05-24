using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static GLFWDotNet.GLFW;

namespace FamiStudio
{
    public class LinuxDialog
    {
        public enum DialogMode
        {
            Open = 0,
            Save = 1,
            Folder = 2,
            MessageBox = 3
        }

        private static LinuxDialog dlgInstance;
        private static string desktopEnvironment;
        private static bool isGtkInitialized;
        private static bool isWayland = FamiStudioWindow.Instance.IsWayland;
        private static bool isX11 = !isWayland;

        static LinuxDialog()
        {
            try
            {
                gtk_init(0, IntPtr.Zero);
                isGtkInitialized = true;
                desktopEnvironment = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GTK init failed: " + ex.Message);
            }
        }

        private const string FlatpakInfoPath = "/.flatpak-info";
        private const string FlatpakPrefix = "/app";
        private const string FlatpakIdEnvVar = "FLATPAK_ID";
        private const string DisplayEnvVar = "DISPLAY";

        // GTK Message Types
        private const int GTK_MESSAGE_INFO = 0;
        private const int GTK_MESSAGE_WARNING = 1;
        private const int GTK_MESSAGE_QUESTION = 2;
        private const int GTK_MESSAGE_ERROR = 3;
        private const int GTK_MESSAGE_OTHER = 4;

        // GTK Buttons
        private const int GTK_BUTTONS_OK = 0;
        private const int GTK_BUTTONS_CLOSE = 1;
        private const int GTK_BUTTONS_CANCEL = 2;
        private const int GTK_BUTTONS_YES_NO = 3;
        private const int GTK_BUTTONS_OK_CANCEL = 4;

        // GTK Responses
        private const int GTK_RESPONSE_ACCEPT = -3;
        private const int GTK_RESPONSE_DELETE_EVENT = -4;
        private const int GTK_RESPONSE_OK = -5;
        private const int GTK_RESPONSE_CANCEL = -6;
        private const int GTK_RESPONSE_CLOSE = -7;
        private const int GTK_RESPONSE_YES = -8;
        private const int GTK_RESPONSE_NO = -9;

        private DialogMode dialogMode;
        private string dialogTitle;
        private string dialogText;
        private string dialogExts;
        private string dialogPath;
        private string flatpakPath;
        private bool dialogMulti;

        private MessageBoxButtons dialogButtons;

        public string[] SelectedPaths { get; private set; }
        public DialogResult MessageBoxSelection { get; private set; }

        #region Localization

        // Only needed for zenity. It doesn't have native 3 button question dialogs, so we manually create cancel.
        LocalizedString CancelLabel;

        // Used if kdialog and zenity are not found.
        LocalizedString DialogErrorTitle;
        LocalizedString DialogErrorMessage;

        #endregion

        public static bool IsDialogOpen => dlgInstance != null;

        public LinuxDialog(DialogMode mode, string title, ref string defaultPath, string extensions = "", bool multiselect = false)
        {
            Localization.Localize(this);
            dialogMode  = mode;
            dialogTitle = title;
            dialogExts  = extensions;
            dialogMulti = multiselect;
            dialogPath  = defaultPath;

            SelectedPaths = ShowFileDialog();

            defaultPath = dialogPath;
        }

        public LinuxDialog(string text, string title, MessageBoxButtons buttons)
        {
            Localization.Localize(this);
            dialogMode    = DialogMode.MessageBox;
            dialogTitle   = title;
            dialogText    = text;
            dialogButtons = buttons;
            MessageBoxSelection = ShowMessageBoxDialog();
        }

        // We try GTK for X11, it seems to work much better there.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void GtkResponseCallback(IntPtr dialog, int response_id, IntPtr user_data);

        [DllImport("libgobject-2.0.so.0")]
        public static extern ulong g_signal_connect_data(IntPtr instance, string detailed_signal, GtkResponseCallback handler, IntPtr data, IntPtr destroy_data, int connect_flags);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_init(int argc, IntPtr argv);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_window_set_title(IntPtr dialog, string title);

        [DllImport("libgtk-3.so.0")]
        public static extern IntPtr gtk_file_chooser_dialog_new(
            string title,
            IntPtr parent,
            int action,
            string firstButtonText, int firstResponse,
            string secondButtonText, int secondResponse,
            IntPtr nullTerminator);

        [DllImport("libgtk-3.so.0")]
        public static extern IntPtr gtk_message_dialog_new(
            IntPtr parent,
            int flags,
            int type,
            int buttons,
            string message_format);

        [DllImport("libgtk-3.so.0")]
        public static extern IntPtr gtk_dialog_add_button(IntPtr dialog, string button_text, int response_id);

        [DllImport("libgtk-3.so.0")]
        public static extern int gtk_dialog_run(IntPtr dialog);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_widget_show(IntPtr widget);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_widget_show_all(IntPtr widget);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_dialog_response(IntPtr dialog, int responseId);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_widget_destroy(IntPtr widget);

        [DllImport("libgtk-3.so.0")]
        public static extern bool gtk_window_is_active(IntPtr window);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_window_set_keep_above(IntPtr window, bool setting);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_window_present(IntPtr window);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_window_set_transient_for(IntPtr window, IntPtr parent);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_window_set_modal(IntPtr window, bool modal);

        [DllImport("libgtk-3.so.0")]
        public static extern bool gtk_file_chooser_set_current_folder(IntPtr chooser, string folder);

        [DllImport("libgtk-3.so.0")]
        public static extern IntPtr gtk_file_chooser_get_filename(IntPtr dialog);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_file_chooser_set_select_multiple(IntPtr dialog, bool select_multiple);

        [DllImport("libgtk-3.so.0")]
        public static extern IntPtr gtk_file_filter_new();

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_file_filter_set_name(IntPtr filter, string name);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_file_filter_add_pattern(IntPtr filter, string pattern);

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_file_chooser_add_filter(IntPtr chooser, IntPtr filter);

        [DllImport("libgtk-3.so.0")]
        public static extern IntPtr g_filename_to_utf8(IntPtr filename, IntPtr len, IntPtr bytesRead, IntPtr bytesWritten, IntPtr error);

        [DllImport("libgtk-3.so.0")]
        public static extern void g_free(IntPtr ptr);

        [DllImport("libgtk-3.so.0")]
        public static extern bool gtk_events_pending();

        [DllImport("libgtk-3.so.0")]
        public static extern void gtk_main_iteration();

        [DllImport("libX11.so")]
        public static extern IntPtr XOpenDisplay(IntPtr display);

        [DllImport("libX11.so")]
        public static extern void XCloseDisplay(IntPtr display);

        [DllImport("libX11.so")]
        public static extern void XGetInputFocus(IntPtr display, out IntPtr focusReturn, out int revertToReturn);

        private bool IsCommandAvailable(string program)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"command -v {program}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                process.WaitForExit();

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDisplayAvailable()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DisplayEnvVar));
        }

        private static bool IsFlatpak()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FlatpakIdEnvVar));
        }

        private void SetFlatpakPaths()
        {
            try
            {
                if (File.Exists(FlatpakInfoPath))
                {
                    var lines = File.ReadAllLines(FlatpakInfoPath);

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("app-path="))
                        {
                            flatpakPath = line.Split('=', 2)[1].Trim(); // System flatpak path.
                            dialogPath = dialogPath.Replace(FlatpakPrefix, flatpakPath);
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private string[] ShowFileDialog()
        {
            if (!IsDisplayAvailable())
                return null;

            // If we're using flatpak and are within the sandboxed "/app" path, the
            // external process can't see it. We use the real system path to it instead.
            if (IsFlatpak() && dialogPath.StartsWith(FlatpakPrefix))
                SetFlatpakPaths();

            if (isGtkInitialized)
            {
                return ShowGtkFileDialog();
            }
            else if (IsCommandAvailable("kdialog"))
            {
                return ShowKDialogFileDialog();
            }
            else if (IsCommandAvailable("zenity"))
            {
                return ShowZenityFileDialog();
            }

            var dlg = new MessageDialog(FamiStudioWindow.Instance, DialogErrorMessage, DialogErrorTitle, MessageBoxButtons.OK);
            dlg.ShowDialog();

            return null;
        }

        private string[] ShowGtkFileDialog()
        {
            IntPtr dialog = gtk_file_chooser_dialog_new(
                dialogTitle,
                IntPtr.Zero,
                (int)dialogMode,
                "_Cancel", GTK_RESPONSE_CANCEL,
                dialogMode == DialogMode.Save ? "_Save" : "_Open", GTK_RESPONSE_ACCEPT,
                IntPtr.Zero);

            if (dialogMulti)
                gtk_file_chooser_set_select_multiple(dialog, true);

            gtk_file_chooser_set_current_folder(dialog, dialogPath);

            // Extensions
            var extPairs = dialogExts.Split("|");
            for (int i = 0; i < extPairs.Length; i += 2)
            {
                IntPtr filter = gtk_file_filter_new();
                var pair = extPairs[i].Split('(');
                gtk_file_filter_set_name(filter, pair[0].Trim());

                foreach (var ext in pair[1].Split(';'))
                {
                    gtk_file_filter_add_pattern(filter, ext.TrimEnd(')'));
                }

                gtk_file_chooser_add_filter(dialog, filter);
            }

            return (string[])ShowGtkDialog(dialog);
        }

        private string[] ShowKDialogFileDialog()
        {
            var args = $"--title \"{dialogTitle}\" ";
            var extPairs = dialogExts.Split("|");

            Debug.Assert(extPairs.Length % 2 == 0);

            var filters = "";
            for (int i = 0; i < extPairs.Length - 1; i += 2)
            {
                var name = extPairs[i].Split(" (")[0]; ;
                var pattern = extPairs[i + 1].Replace(";", " ");
                filters += $"{name} {pattern}|";
            }

            filters = filters.TrimEnd('|');

            switch (dialogMode)
            {
                case DialogMode.Open:
                    args += dialogMulti
                        ? $"--getopenfilename \"{dialogPath}\" \"{filters}\" --multiple --separate-output"
                        : $"--getopenfilename \"{dialogPath}\" \"{filters}\"";
                    break;

                case DialogMode.Save:
                    args += $"--getsavefilename \"{dialogPath}\" \"{filters}\"";
                    break;

                case DialogMode.Folder:
                    args += $"--getexistingdirectory \"{dialogPath}\"";
                    break;
            }

            var result = ShowDialog("kdialog", args);

            if (result.value.Length == 0)
                return null;

            dialogPath = dialogMode == DialogMode.Folder ? result.value[0] : Path.GetDirectoryName(result.value[0]);
            return result.value;
        }

        private string[] ShowZenityFileDialog()
        {
            var filters  = "";
            var extPairs = dialogExts.Split("|");

            if (extPairs.Length > 0)
            {
                Debug.Assert(extPairs.Length % 2 == 0);

                for (int i = 0; i < extPairs.Length - 1; i += 2)
                    filters += "--file-filter=\"" + (extPairs[i] + "|" + extPairs[i + 1]).Replace(";", " ").Trim() + "\" ";
            }

            var args = $"--file-selection --title=\"{dialogTitle}\" --filename=\"{dialogPath.TrimEnd('/')}/\" {filters} ";

            switch (dialogMode)
            {
                case DialogMode.Open:
                    if (dialogMulti) args += "--multiple --separator=\"\n\"";
                    break;

                case DialogMode.Save:
                    args += "--save";
                    break;

                case DialogMode.Folder:
                    args += "--directory";
                    break;
            }

            var result = ShowDialog("zenity", args);

            if (result.value.Length == 0)
                return null;

            dialogPath = dialogMode == DialogMode.Folder ? result.value[0] : Path.GetDirectoryName(result.value[0]);
            return result.value;
        }

        private DialogResult ShowMessageBoxDialog()
        {
            if (!IsDisplayAvailable())
                return DialogResult.None;

            if (isGtkInitialized)
            {
                return ShowGtkMessageBoxDialog();
            }
            else if (IsCommandAvailable("kdialog"))
            {
                return ShowKDialogMessageBoxDialog();
            }
            else if (IsCommandAvailable("zenity"))
            {
                return ShowZenityMessageBoxDialog();
            }

            var dlg = new MessageDialog(FamiStudioWindow.Instance, DialogErrorMessage, DialogErrorTitle, MessageBoxButtons.OK);
            dlg.ShowDialog();

            return DialogResult.None;
        }

        private DialogResult ShowGtkMessageBoxDialog()
        {
            var messageType = dialogButtons == MessageBoxButtons.OK ? GTK_MESSAGE_INFO : GTK_MESSAGE_QUESTION;
            var buttonsType = dialogButtons switch
            {
                MessageBoxButtons.OK          => GTK_BUTTONS_OK,
                MessageBoxButtons.YesNo       => GTK_BUTTONS_YES_NO,
                MessageBoxButtons.YesNoCancel => GTK_BUTTONS_OK_CANCEL,
                _                             => GTK_BUTTONS_OK,
            };
            IntPtr dialog = gtk_message_dialog_new(
                IntPtr.Zero,
                0,
                messageType,
                buttonsType,
                dialogText);

            gtk_window_set_title(dialog, dialogTitle);
            gtk_window_set_modal(dialog, true);

            if (dialogButtons == MessageBoxButtons.YesNoCancel)
            {
                gtk_dialog_add_button(dialog, "_Cancel", GTK_RESPONSE_CANCEL);
            }

            return (DialogResult)ShowGtkDialog(dialog);
        }

        private DialogResult ShowKDialogMessageBoxDialog()
        {
            string args = string.Empty;

            switch (dialogButtons)
            {
                case MessageBoxButtons.OK:
                    args = $"--msgbox \"{dialogText}\" --title \"{dialogTitle}\"";
                    break;

                case MessageBoxButtons.YesNo:
                    args = $"--yesno \"{dialogText}\" --title \"{dialogTitle}\"";
                    break;

                case MessageBoxButtons.YesNoCancel:
                    args = $"--yesnocancel \"{dialogText}\" --title \"{dialogTitle}\"";
                    break;
            }

            var result = ShowDialog("kdialog", args);
            var exitCode = result.exitCode;

            return exitCode switch
            {
                0 => dialogButtons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Yes,
                1 => DialogResult.No,
                2 => DialogResult.Cancel,
                _ => DialogResult.None,
            };
        }

        private DialogResult ShowZenityMessageBoxDialog()
        {
            var args = string.Empty;
            switch (dialogButtons)
            {
                case MessageBoxButtons.OK:
                    args = $"--info --title=\"{dialogTitle}\" --text=\"{dialogText}\"";
                    break;

                case MessageBoxButtons.YesNo:
                    args = $"--question --title=\"{dialogTitle}\" --text=\"{dialogText}\"";
                    break;

                case MessageBoxButtons.YesNoCancel:
                    args = $"--question --title=\"{dialogTitle}\" --text=\"{dialogText}\" --extra-button=\"{CancelLabel}\"";
                    break;
            }

            var result   = ShowDialog("zenity", args);
            var exitCode = result.exitCode;

            if (exitCode == 0)
            {
                return dialogButtons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Yes;
            }
            else if (exitCode == 1)
            {
                // No and Cancel are both exit code 1 on zenity, since it doesn't natively support 3 buttons. "No" returns an empty array.
                if (result.value.Length == 0)
                {
                    return DialogResult.No;
                }
                else if (result.value[0] == CancelLabel)
                {
                    return DialogResult.Cancel;
                }
            }

            return DialogResult.None;
        }

        private object ShowGtkDialog(IntPtr dialog)
        {
            dlgInstance = this;
            gtk_widget_show_all(dialog);

            var response = -1;
            var responseReceived = false;

            GtkResponseCallback callback = (dlg, responseId, userData) =>
            {
                response = responseId;
                responseReceived = true;
            };

            var callbackHandle = GCHandle.Alloc(callback);
            g_signal_connect_data(dialog, "response", callback, IntPtr.Zero, IntPtr.Zero, 0);

            // GTK event loop to keep the main window responsive.
            while (!responseReceived)
            {
                while (gtk_events_pending())
                    gtk_main_iteration();

                var isGnome = desktopEnvironment.Contains("gnome", StringComparison.InvariantCultureIgnoreCase);
                var dialogFocus = FamiStudioWindow.Instance.IsWindowInFocus;

                // GNOME doesn't work very well with keep above, it works better with present.
                if (!isGnome)
                {
                    gtk_window_set_keep_above(dialog, dialogFocus);
                }
                else if (dialogFocus && !gtk_window_is_active(dialog))
                {
                    gtk_window_present(dialog);
                }

                FamiStudioWindow.Instance.RunEventLoop(true);
                System.Threading.Thread.Sleep(16);
            }


            object result = null;

            if (dialogMode == DialogMode.MessageBox)
            {
                result = response switch
                {
                    GTK_RESPONSE_DELETE_EVENT => DialogResult.None,
                    GTK_RESPONSE_OK => DialogResult.OK,
                    GTK_RESPONSE_CANCEL => DialogResult.Cancel,
                    GTK_RESPONSE_YES => DialogResult.Yes,
                    GTK_RESPONSE_NO => DialogResult.No,
                    _ => DialogResult.None
                };
            }
            else if (response == GTK_RESPONSE_ACCEPT)
            {
                IntPtr filenamePtr = gtk_file_chooser_get_filename(dialog);
                if (filenamePtr != IntPtr.Zero)
                {
                    var file = Marshal.PtrToStringUTF8(filenamePtr)!;
                    result = new[] { file };
                    g_free(filenamePtr);
                }
            }

            gtk_widget_destroy(dialog);
            callbackHandle.Free();

            // Wait until the dialog exits fully.
            while (gtk_events_pending())
                gtk_main_iteration();

            dlgInstance = null;
            return result;
        }

        private (string[] value, int exitCode) ShowDialog(string command, string arguments)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            dlgInstance = this;
            process.Start();

            while (!process.HasExited)
            {
                FamiStudioWindow.Instance.RunEventLoop(true);
            }

            dlgInstance = null;
            var results = process.StandardOutput.ReadToEnd().Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // If we're using flatpak, we may have converted the sandbox "/app" path
            // to a system one. If so, convert the results so the sandbox can find them.
            if (!string.IsNullOrEmpty(flatpakPath))
            {
                for (var i = 0; i < results.Length; i++)
                    results[i] = results[i].Replace(flatpakPath, FlatpakPrefix);
            }

            return (results, process.ExitCode);
        }
    }
}
