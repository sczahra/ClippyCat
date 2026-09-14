using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
    private static long lastObservedStateEndAt;
    private static long dragStartedAt;
    private static long nextSummaryAt;
    private static int clickCount;
    private static int dragCount;

    [ModuleInitializer]
    internal static void InitializeModule()
    {
        PetDiagnostics.Initialize(UpdateService.CurrentVersionText);
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

        pollTimer = new System.Windows.Forms.Timer { Interval = 200 };
        pollTimer.Tick += (_, _) => PollPet();
        pollTimer.Start();

        nextSummaryAt = Environment.TickCount64 + 30000;
        PetDiagnostics.Log("DIAG_BOOTSTRAP", "pollMs=200 source=reflection atlasCompare=true");
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
            long stateEndsAt = GetField<long>(type, petForm, "stateEndsAt");
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
                long previousRemaining = lastObservedStateEndAt == 0 ? 0 : lastObservedStateEndAt - now;
                bool interrupted = lastStateChangeAt != 0 && previousRemaining > 150;
                long plannedRemaining = Math.Max(0, stateEndsAt - now);

                PetDiagnostics.Log(
                    "STATE",
                    $"pet={pet} from={lastState ?? "startup"} to={state} previousMs={previousDuration} " +
                    $"interruptedPrev={interrupted} prevRemainingMs={Math.Max(0, previousRemaining)} " +
                    $"plannedMs={plannedRemaining} energy={energy} row={row} frame={frame} pos=({petForm.Left},{petForm.Top})"
                );

                lastState = state;
                lastStateChangeAt = now;
                lastObservedStateEndAt = stateEndsAt;
            }
            else
            {
                lastObservedStateEndAt = stateEndsAt;
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
                    $"pet={pet} state={state} energy={energy} row={row} frame={frame} pos=({petForm.Left},{petForm.Top}) " +
                    $"visible={petForm.Visible} paused={paused} clicks={clickCount} drags={dragCount}"
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

            Dictionary<(int Row, int Col), FrameBox> activeBoxes = MeasureAtlas(atlas, pet, logRows: true);

            if (!pet.Equals("Merry", StringComparison.OrdinalIgnoreCase))
                return;

            string marmaladePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Marmalade", "spritesheet.png");
            if (!File.Exists(marmaladePath))
            {
                PetDiagnostics.Log("ATLAS_COMPARE_SKIPPED", $"reason=marmalade_missing path={marmaladePath}");
                return;
            }

            using Bitmap marmaladeAtlas = new(marmaladePath);
            Dictionary<(int Row, int Col), FrameBox> marmaladeBoxes = MeasureAtlas(marmaladeAtlas, "MarmaladeReference", logRows: false);
            CompareAtlases(marmaladeBoxes, activeBoxes);
        }
        catch (Exception ex)
        {
            PetDiagnostics.Error("ATLAS_ANALYSIS_FAILED", ex);
        }
    }

    private static Dictionary<(int Row, int Col), FrameBox> MeasureAtlas(Bitmap atlas, string pet, bool logRows)
    {
        const int cellWidth = 192;
        const int cellHeight = 208;
        int[] framesPerRow = { 6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8 };
        Dictionary<(int Row, int Col), FrameBox> result = new();

        for (int row = 0; row < framesPerRow.Length; row++)
        {
            List<FrameBox> rowBoxes = new();

            for (int col = 0; col < framesPerRow[row]; col++)
            {
                Rectangle source = new(col * cellWidth, row * cellHeight, cellWidth, cellHeight);
                using Bitmap cell = atlas.Clone(source, PixelFormat.Format32bppArgb);
                FrameBox? measured = MeasureFrame(cell);

                if (measured is null)
                    continue;

                FrameBox box = measured.Value;
                result[(row, col)] = box;
                rowBoxes.Add(box);
            }

            if (!logRows || rowBoxes.Count == 0)
                continue;

            int minWidth = rowBoxes.Min(box => box.Width);
            int maxWidth = rowBoxes.Max(box => box.Width);
            int minHeight = rowBoxes.Min(box => box.Height);
            int maxHeight = rowBoxes.Max(box => box.Height);
            int widthSpread = maxWidth - minWidth;
            int heightSpread = maxHeight - minHeight;
            string flag = widthSpread > 24 || heightSpread > 24 ? "POSE_VARIATION" : "OK";

            PetDiagnostics.Log(
                "ATLAS_ROW",
                $"pet={pet} row={row} frames={rowBoxes.Count} w={minWidth}-{maxWidth} h={minHeight}-{maxHeight} flag={flag}"
            );
        }

        return result;
    }

    private static FrameBox? MeasureFrame(Bitmap cell)
    {
        int minX = cell.Width;
        int minY = cell.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < cell.Height; y += 2)
        {
            for (int x = 0; x < cell.Width; x += 2)
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
            return null;

        return new FrameBox(
            minX,
            minY,
            maxX - minX + 1,
            maxY - minY + 1
        );
    }

    private static void CompareAtlases(
        Dictionary<(int Row, int Col), FrameBox> reference,
        Dictionary<(int Row, int Col), FrameBox> merry)
    {
        double worstSizeError = 0;
        int worstRow = -1;
        int worstCol = -1;
        double sizeErrorSum = 0;
        double centerErrorSum = 0;
        double bottomErrorSum = 0;
        int compared = 0;

        foreach (KeyValuePair<(int Row, int Col), FrameBox> pair in reference)
        {
            if (!merry.TryGetValue(pair.Key, out FrameBox merryBox))
                continue;

            FrameBox refBox = pair.Value;
            double widthError = Math.Abs(merryBox.Width - refBox.Width) / (double)Math.Max(1, refBox.Width);
            double heightError = Math.Abs(merryBox.Height - refBox.Height) / (double)Math.Max(1, refBox.Height);
            double sizeError = Math.Max(widthError, heightError);
            double centerError = Math.Abs(merryBox.CenterX - refBox.CenterX);
            double bottomError = Math.Abs(merryBox.Bottom - refBox.Bottom);

            sizeErrorSum += sizeError;
            centerErrorSum += centerError;
            bottomErrorSum += bottomError;
            compared++;

            if (sizeError > worstSizeError)
            {
                worstSizeError = sizeError;
                worstRow = pair.Key.Row;
                worstCol = pair.Key.Col;
            }

            if (sizeError >= 0.10 || centerError >= 8 || bottomError >= 8)
            {
                PetDiagnostics.Log(
                    "ATLAS_FRAME_DIFF",
                    $"row={pair.Key.Row} col={pair.Key.Col} sizeErrorPct={sizeError * 100:F1} " +
                    $"centerDx={merryBox.CenterX - refBox.CenterX:F1} bottomDy={merryBox.Bottom - refBox.Bottom} " +
                    $"merry={merryBox.Width}x{merryBox.Height} ref={refBox.Width}x{refBox.Height}"
                );
            }
        }

        if (compared == 0)
            return;

        PetDiagnostics.Log(
            "ATLAS_COMPARE",
            $"frames={compared} avgSizeErrorPct={(sizeErrorSum / compared) * 100:F1} " +
            $"avgCenterErrorPx={centerErrorSum / compared:F1} avgBottomErrorPx={bottomErrorSum / compared:F1} " +
            $"worstSizeErrorPct={worstSizeError * 100:F1} worstFrame={worstRow}:{worstCol}"
        );
    }

    private readonly record struct FrameBox(int X, int Y, int Width, int Height)
    {
        public double CenterX => X + Width / 2.0;
        public int Bottom => Y + Height;
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
