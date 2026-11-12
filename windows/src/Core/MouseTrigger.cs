using System;
using System.Linq;

namespace DragShift.Core
{
    /// <summary>The mouse buttons that open the ring while the left button is holding a file.</summary>
    [Flags]
    public enum MouseTrigger
    {
        None = 0,
        Right = 1,
        /// <summary>The wheel click.</summary>
        Middle = 2,
        /// <summary>The side button nearest the wrist ("back").</summary>
        Back = 4,
        /// <summary>The other side button ("forward").</summary>
        Forward = 8,
        All = Right | Middle | Back | Forward,
    }

    /// <summary>Saves the chosen buttons as plain words in settings.ini ("right,middle"), easy to fix by hand.</summary>
    public static class MouseTriggers
    {
        static readonly string[] Names = { "right", "middle", "back", "forward" };

        public static string Format(MouseTrigger buttons) =>
            string.Join(",", Names.Where((n, i) => ((int)buttons & (1 << i)) != 0));

        /// <summary>Nothing saved yet gives <paramref name="fallback"/>. A saved empty list means no button at all.
        /// Words it doesn't know are ignored; a list with no known word counts as "nothing saved", so a typo in the file
        /// never switches the trigger off.</summary>
        public static MouseTrigger Parse(string saved, MouseTrigger fallback)
        {
            if (saved == null) return fallback;
            if (saved.Trim().Length == 0) return MouseTrigger.None;
            var found = MouseTrigger.None;
            foreach (var word in saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = Array.IndexOf(Names, word.Trim().ToLowerInvariant());
                if (i >= 0) found |= (MouseTrigger)(1 << i);
            }
            return found == MouseTrigger.None ? fallback : found;
        }
    }
}
