using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MarmaladeDesktopPet;

internal static class SingleInstanceBootstrap
{
    private const string MutexName = "Local\\ClippyCat.MarmaladeDesktopPet.SingleInstance";
    private static Mutex? instanceMutex;

    [ModuleInitializer]
    internal static void Initialize()
    {
        bool createdNew;
        instanceMutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out createdNew);

        if (createdNew)
            return;

        MessageBox.Show(
            "ClippyCat is already running.\n\nCheck the system tray for the existing cat.",
            "ClippyCat",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );

        Environment.Exit(0);
    }
}
