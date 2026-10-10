using System;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase Q4 (review board R3, R10): the red countdown chip for a task that is under two real minutes from its deadline, and how the
    /// taskbar shares its width between the window buttons and the task button so the task line never truncates when it matters.
    /// Engine-free.
    /// </summary>
    public static class DeadlineChip
    {
        /// <summary>The chip shows once a deadline is under this many real seconds away.</summary>
        public const float ShowUnderSeconds = 120f;
        /// <summary>From here on the chip alternates red and dark (2 Hz, under the 3 Hz photosensitivity line); Reduce flashing keeps it steady.</summary>
        public const float BlinkUnderSeconds = 15f;
        public const float BlinkHz = 2f;
        public const int ChipWidth = 34, ChipHeight = 16;

        /// <summary>A chip that is up stays until the deadline is this far away (a little past the line it came up at, so a change of the clock's speed there cannot flicker it).</summary>
        public const float HideAtSeconds = 125f;

        /// <summary>True when a deadline <paramref name="realSeconds"/> away (negative: unknown) earns a chip.</summary>
        public static bool Shows(float realSeconds) => realSeconds >= 0f && realSeconds < ShowUnderSeconds;

        /// <summary>The same with hysteresis: when the chip is already up (<paramref name="wasShown"/>) it stays until <see cref="HideAtSeconds"/>.</summary>
        public static bool Shows(float realSeconds, bool wasShown) => realSeconds >= 0f && realSeconds < (wasShown ? HideAtSeconds : ShowUnderSeconds);

        /// <summary>The chip's text as m:ss, counting down in whole seconds ("0:41").</summary>
        public static string Text(float realSeconds)
        {
            int s = (int)Math.Ceiling(Math.Max(0f, realSeconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>The chip is in its alternate (dark) phase now: it blinks only in the last seconds and never with Reduce flashing.</summary>
        public static bool BlinkDark(float realSeconds, bool reduceFlashing, float time)
            => !reduceFlashing && realSeconds >= 0f && realSeconds <= BlinkUnderSeconds && (time * BlinkHz) % 1f >= 0.5f;
    }

    /// <summary>The widths the taskbar uses (the task button, and each window button).</summary>
    public readonly struct TaskbarPlan
    {
        public readonly int TaskWidth, ButtonWidth;
        public readonly bool IconOnly;

        public TaskbarPlan(int taskWidth, int buttonWidth, bool iconOnly)
        {
            TaskWidth = taskWidth;
            ButtonWidth = buttonWidth;
            IconOnly = iconOnly;
        }
    }

    public static class TaskbarLayout
    {
        public const int TaskDefault = 236, TaskMax = 360;
        public const int ButtonMax = 150, ButtonGap = 3;
        /// <summary>A window button narrower than this shows only its icon (with a hover tooltip).</summary>
        public const int IconOnlyBelow = 64;
        public const int IconButton = 24;

        /// <summary>
        /// Shares <paramref name="total"/> pixels (the strip between the NEXUS button and the menu button) among <paramref name="windows"/>
        /// window buttons and the task button. Window buttons are named up to <see cref="ButtonMax"/> wide, become icon-only when they would
        /// be under <see cref="IconOnlyBelow"/>, and are icon-only whenever the deadline chip shows; the task button never drops under
        /// <see cref="TaskDefault"/> and takes the free space up to <see cref="TaskMax"/> while the chip shows.
        /// </summary>
        public static TaskbarPlan Plan(int total, int windows, bool chip)
        {
            int n = Math.Max(1, windows);
            int area = total - TaskDefault;
            int named = Math.Min(ButtonMax, (area - (n - 1) * ButtonGap) / n);
            bool iconOnly = chip || named < IconOnlyBelow;
            int bw = iconOnly ? IconButton : named;
            int used = windows > 0 ? windows * bw + (windows - 1) * ButtonGap : 0;
            int task = chip ? Math.Min(TaskMax, Math.Max(TaskDefault, total - used - ButtonGap)) : TaskDefault;
            return new TaskbarPlan(task, bw, iconOnly);
        }
    }
}
