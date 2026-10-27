// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Media;
using System.Windows;
using System.Windows.Input;
using FolderManaging.Companion.Localization;
using FolderManaging.Core.Localization;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using Button = Wpf.Ui.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace FolderManaging.Companion;

/// <summary>
/// Used instead of <see cref="System.Windows.MessageBox"/>: the system box labels its buttons in the Windows language
/// and never mirrors, so with another app language a dialog would mix two languages.
/// </summary>
public partial class FmMessageBox : FluentWindow
{
    MessageBoxResult _result;

    FmMessageBox(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        UiDirection.Apply(this);
        Title = caption;
        Bar.Title = caption;
        UiDirection.SetText(Body, text);
        SetGlyph(image);
        AddButtons(buttons);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    internal static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage image) =>
        Show(null, text, caption, buttons, image);

    internal static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption = "Folder Managing",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
            return dispatcher.Invoke(() => Show(owner, text, caption, buttons, image));

        var box = new FmMessageBox(text, caption, buttons, image);
        if (owner is { IsVisible: true })
        {
            box.Owner = owner;
            box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            box.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            box.Topmost = true;
        }

        PlaySound(image);
        box.ShowDialog();
        return box._result;
    }

    void AddButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("Ok", MessageBoxResult.OK, primary: true);
                AddButton("Cancel", MessageBoxResult.Cancel, cancel: true);
                _result = MessageBoxResult.Cancel;
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, primary: true);
                AddButton("No", MessageBoxResult.No, cancel: true);
                _result = MessageBoxResult.No;
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, primary: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel, cancel: true);
                _result = MessageBoxResult.Cancel;
                break;
            default:
                AddButton("Ok", MessageBoxResult.OK, primary: true, cancel: true);
                _result = MessageBoxResult.OK;
                break;
        }
    }

    void AddButton(string key, MessageBoxResult result, bool primary = false, bool cancel = false)
    {
        var button = new Button
        {
            Content = UiText.Get(key),
            MinWidth = 96,
            Margin = new Thickness(Buttons.Children.Count == 0 ? 0 : 8, 0, 0, 0),
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
            IsDefault = primary,
            IsCancel = cancel,
        };
        button.Click += (_, _) =>
        {
            _result = result;
            Close();
        };
        Buttons.Children.Add(button);
        if (primary)
            Loaded += (_, _) => button.Focus();
    }

    void SetGlyph(MessageBoxImage image)
    {
        SymbolRegular symbol;
        string brushKey;
        switch (image)
        {
            case MessageBoxImage.Warning:
                symbol = SymbolRegular.Warning24;
                brushKey = "SystemFillColorCautionBrush";
                break;
            case MessageBoxImage.Error:
                symbol = SymbolRegular.ErrorCircle24;
                brushKey = "SystemFillColorCriticalBrush";
                break;
            case MessageBoxImage.Question:
                symbol = SymbolRegular.QuestionCircle24;
                brushKey = "AccentTextFillColorPrimaryBrush";
                break;
            case MessageBoxImage.Information:
                symbol = SymbolRegular.Info24;
                brushKey = "AccentTextFillColorPrimaryBrush";
                break;
            default:
                return;
        }

        Glyph.Symbol = symbol;
        Glyph.Filled = true;
        Glyph.SetResourceReference(ForegroundProperty, brushKey);
        Glyph.Visibility = Visibility.Visible;
    }

    static void PlaySound(MessageBoxImage image)
    {
        switch (image)
        {
            case MessageBoxImage.Warning:
                SystemSounds.Exclamation.Play();
                break;
            case MessageBoxImage.Error:
                SystemSounds.Hand.Play();
                break;
            case MessageBoxImage.Question:
                SystemSounds.Question.Play();
                break;
            case MessageBoxImage.Information:
                SystemSounds.Asterisk.Play();
                break;
        }
    }

    /// <summary>Ctrl+C copies the message, like the system message box.</summary>
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
            return;
        try
        {
            Clipboard.SetText(Title + Environment.NewLine + Environment.NewLine + Body.Text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // clipboard busy
        }

        e.Handled = true;
    }
}
