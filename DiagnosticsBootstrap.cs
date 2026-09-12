using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MarmaladeDesktopPet;

internal static class DiagnosticsBootstrap
{
    private static System.Windows.Forms.Timer? pollTimer;
    private static Form? hookedPetForm;
    private static Type? petFormType;

    private static string? lastPet;
    private static string? lastState;
    private static int? lastEnergyBucket;
    private static bool? lastDragging;
    private static long lastStateChangeAt;
    private static long dragStartedAt;
    private static long nextSummaryAt;
    private static int clickCount;
    private static int dragCount;

    [ModuleInitializer]
    internal static void InitializeModule()
    {
        PetDiagnostics.Initialize("1.3");
        Application.ThreadException += (_, e) => PetDiagnostics.Error("THREAD_EXCEPTION", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                PetDiagnostics.Error("UNHANDLED_EXCEPTION", ex);
        };

        Application.Idle += StartPollingOnce;
    }

    private static void StartPollingOnce(object? sender, EventArgs e)
    {
        if (pollTimer is not null)
            return;

        Application.Idle -= StartPollingOnce;

        pollTimer = new System.Windows.Forms.Timer { Interval = 250 };
        pollTimer.Tick += (_, _) => PollPet();
        pollTimer.Start();

        nextSummaryAt = Environment.TickCount64 + 30000;
        PetDiagnostics.Log("DIAG_BOOTSTRAP", "pollMs=250 source=reflection");
    }

    private static void PollPet()
    {
        try
        {
            Form? petForm = Application.OpenForms
                .Cast<Form>()
                .FirstOrDefault(form => form.GetType().Name == "PetForm");

            if (petForm is null)
                return;

            if (!ReferenceEquals(hookedPetForm, petForm))
                HookPetForm(petForm);

            Type type = petForm.GetType();
            string pet = GetProperty<string>(type, petForm, "ActivePetName") ?? "unknown";
            string state = GetFieldValue(type, petForm, "state")?.ToString() ?? "unknown";
            int energy = GetField<int>(type, petForm, "energy");
            bool dragging = GetField<bool>(type, petForm, "dragging");
            bool paused = GetField<bool>(type, petForm, "paused");
            int row = GetField<int>(type, petForm, "currentRow");
            int frame = GetField<int>(type, petForm, "frameIndex");
            long now = Environment.TickCount64;

            if (!string.Equals(lastPet, pet, StringComparison.Ordinal))
            {
                PetDiagnostics.Log("PET_SWITCH", $"from={lastPet ?? "startup"} to={pet}");
                lastPet = pet;
                AnalyzeAtlas(type, petForm, pet);
            }

            if (!string.Equals(lastState, state, StringComparison.Ordinal))
            {
                long previousDuration = lastStateChangeAt == 0 ? 0 : Math.Max(0, now - lastStateChangeAt);
                PetDiagnostics.Log(
                    "STATE",
                    $"pet={pet} from={lastState ?? "startup"} to={state} previousMs={previousDuration} energy={energy} row={row} frame={frame} pos=({petForm.Left},{petForm.Top})"
                );
                lastState = state;
                lastStateChangeAt = now;
            }

            int energyBucket = energy / 10;
            if (lastEnergyBucket != energyBucket)
            {
                PetDiagnostics.Log("ENERGY", $"pet={pet} state={state} energy={energy}");
                lastEnergyBucket = energyBucket;
            }

            if (lastDragging != dragging)
            {
                if (dragging)
                {
                    dragStartedAt = now;
                    dragCount++;
                    PetDiagnostics.Log("DRAG_START", $"pet={pet} state={state} pos=({petForm.Left},{petForm.Top})");
                }
                else if (lastDragging == true)
                {
                    long duration = Math.Max(0, now - dragStartedAt);
                    PetDiagnostics.Log("DRAG_END", $"pet={pet} durationMs={duration} pos=({petForm.Left},{petForm.Top})");
                }

                lastDragging = dragging;
            }

            if (now >= nextSummaryAt)
            {
                PetDiagnostics.Log(
                    "SUMMARY",
                    $"pet={pet} state={state} energy={energy} row={row} frame={frame} pos=({petForm.Left},{petForm.Top}) visible={petForm.Visible} paused={paused} clicks={clickCount} drags={dragCount}"
                );
                nextSummaryAt = now + 30000;
            }
        }
        catch (Exception ex)
        {
            PetDiagnostics.Error("POLL_FAILED", ex);
        }
    }

    private static void HookPetForm(Form petForm)
    {
        hookedPetForm = petForm;
        petFormType = petForm.GetType();

        petForm.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            clickCount++;
            PetDiagnostics.Log(
                "MOUSE_DOWN",
                $"pet={GetProperty<string>(petFormType!, petForm, "ActivePetName") ?? "unknown"} x={e.X} y={e.Y} clicks={clickCount}"
            );
        };

        petForm.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            PetDiagnostics.Log(
                "MOUSE_UP",
                $"pet={GetProperty<string>(petFormType!, petForm, "ActivePetName") ?? "unknown"} x={e.X} y={e.Y}"
            );
        };

        PetDiagnostics.Log("PETFORM_HOOKED", $"type={petFormType.FullName}");
    }

    private static void AnalyzeAtlas(Type type, object instance, string pet)
    {
        try
        {
            Bitmap? atlas = GetFieldValue(type, instance, "atlas") as Bitmap;
            if (atlas is null)
                return;

            PetDiagnostics.Log("ATLAS_LOAD", $"pet={pet} size={atlas.Width}x{atlas.Height}");

            const int cellWidth = 192;
            const int cellHeight = 208;
            int[] framesPerRow = { 6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8 };

            for (int row = 0; row < framesPerRow.Length; row++)
            {
                int minWidth = int.MaxValue;
                int maxWidth = 0;
                int minHeight = int.MaxValue;
                int maxHeight = 0;
                int measured = 0;

                for (int col = 0; col < framesPerRow[row]; col++)
                {
                    Rectangle source = new(col * cellWidth, row * cellHeight, cellWidth, cellHeight);
                    using Bitmap cell = atlas.Clone(source, PixelFormat.Format32bppArgb);

                    int minX = cellWidth;
                    int minY = cellHeight;
                    int maxX = -1;
                    int maxY = -1;

                    for (int y = 0; y < cellHeight; y += 3)
                    {
                        for (int x = 0; x < cellWidth; x += 3)
                        {
                            if (cell.GetPixel(x, y).A < 24)
                                continue;

                            minX = Math.Min(minX, x);
                            minY = Math.Min(minY, y);
                            maxX = Math.Max(maxX, x);
                            maxY = Math.Max(maxY, y);
                        }
                    }

                    if (maxX < minX || maxY < minY)
                        continue;

                    int width = maxX - minX + 1;
                    int height = maxY - minY + 1;
                    minWidth = Math.Min(minWidth, width);
                    maxWidth = Math.Max(maxWidth, width);
                    minHeight = Math.Min(minHeight, height);
                    maxHeight = Math.Max(maxHeight, height);
                    measured++;
                }

                if (measured == 0)
                    continue;

                int widthSpread = maxWidth - minWidth;
                int heightSpread = maxHeight - minHeight;
                string flag = widthSpread > 24 || heightSpread > 24 ? "VARIATION" : "OK";

                PetDiagnostics.Log(
                    "ATLAS_ROW",
                    $"pet={pet} row={row} frames={measured} w={minWidth}-{maxWidth} h={minHeight}-{maxHeight} flag={flag}"
                );
            }
        }
        catch (Exception ex)
        {
            PetDiagnostics.Error("ATLAS_ANALYSIS_FAILED", ex);
        }
    }

    private static T GetField<T>(Type type, object instance, string name)
    {
        object? value = GetFieldValue(type, instance, name);
        return value is T typed ? typed : default!;
    }

    private static object? GetFieldValue(Type type, object instance, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static T? GetProperty<T>(Type type, object instance, string name)
    {
        object? value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        return value is T typed ? typed : default;
    }
}
