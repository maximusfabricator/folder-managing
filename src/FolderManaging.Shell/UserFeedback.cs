using System.Runtime.InteropServices;
using FolderManaging.Core;
using FolderManaging.Core.Diagnostics;
using FolderManaging.Core.Storage;

namespace FolderManaging.Shell;

/// <summary>Tells the user why a context-menu action did nothing (otherwise failures are only visible in the log).</summary>
internal static class UserFeedback
{
    const uint MB_OK = 0x0000_0000;
    const uint MB_YESNO = 0x0000_0004;
    const uint MB_ICONWARNING = 0x0000_0030;
    const uint MB_ICONERROR = 0x0000_0010;
    const uint MB_SETFOREGROUND = 0x0001_0000;
    const uint MB_TOPMOST = 0x0004_0000;
    const int IDYES = 6;
    const string Caption = "Folder Manager";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    /// <summary>Shows the failure; for protected folders elevates (auto or after confirm). Returns <c>true</c> if elevated retry succeeded.</summary>
    internal static bool ReportFolderActionFailure(string action, string path, string? value, Exception ex)
    {
        try
        {
            if (IsAccessDenied(ex))
            {
                var autoElevate = false;
                try
                {
                    autoElevate = MenuStateReader.GetMerged().AutoElevateProtectedFolders;
                }
                catch (Exception readEx)
                {
                    ClickTrace.Step($"feedback: could not read AutoElevate setting: {readEx.Message}");
                    autoElevate = true;
                }

                if (!autoElevate)
                {
                    ClickTrace.Step("feedback: access denied dialog shown (auto-elevate off)");
                    var answer = MessageBoxW(
                        0,
                        $"Windows защищает эту папку, поэтому изменить её без прав администратора нельзя:\n{path}\n\n" +
                        "Повторить с правами администратора?\n\n" +
                        "(Можно включить авто-права в Настройках Folder Managing.)",
                        Caption,
                        MB_YESNO | MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST);
                    ClickTrace.Step($"feedback: answer={(answer == IDYES ? "yes" : "no")}");
                    if (answer != IDYES)
                        return false;
                }
                else
                {
                    ClickTrace.Step("feedback: access denied → auto-elevate (setting on)");
                }

                var ok = ElevatedCliInvoker.TryInvoke(action, path, value, out var elevErr);
                if (!ok)
                {
                    var tip = string.Equals(elevErr, "User cancelled UAC.", StringComparison.Ordinal)
                        ? "Нужно один раз подтвердить права администратора в окне Windows. После этого вопросы больше не появятся, пока включена настройка."
                        : elevErr;
                    MessageBoxW(
                        0,
                        $"Не получилось с правами администратора.\n{tip}\n\nПодробности в логе (Настройки → Открыть логи).",
                        Caption,
                        MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST);
                }

                return ok;
            }

            ClickTrace.Step("feedback: error dialog shown");
            MessageBoxW(
                0,
                $"Не удалось изменить папку:\n{path}\n\n{ex.Message}\n\nПодробности в логе (Настройки → Открыть логи).",
                Caption,
                MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST);
        }
        catch (Exception inner)
        {
            ClickTrace.Step($"feedback dialog failed: {inner.Message}");
        }

        return false;
    }

    static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException)
                return true;
        }

        return false;
    }
}
