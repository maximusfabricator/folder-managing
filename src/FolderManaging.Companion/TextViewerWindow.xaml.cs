// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Windows;
using Wpf.Ui.Controls;

namespace FolderManaging.Companion;

/// <summary>Read-only text (license, notices). Always left-to-right: the texts are English.</summary>
public partial class TextViewerWindow : FluentWindow
{
    public TextViewerWindow(string title, string text)
    {
        InitializeComponent();
        Title = title;
        Bar.Title = title;
        Body.Text = text;
        Body.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en");
    }

    internal static void Show(Window? owner, string title, string text)
    {
        var window = new TextViewerWindow(title, text);
        if (owner is { IsVisible: true })
            window.Owner = owner;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
    }
}
