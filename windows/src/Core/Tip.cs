using System;
using System.Globalization;

namespace DragShift.Core
{
    /// <summary>"Buy me a coffee": where a tip goes and how the About tab asks for one. Nothing here talks to the
    /// internet; the app only shows an address for the person's own wallet app. It never checks whether anyone
    /// paid, and nothing is locked or unlocked by it. Same words and numbers as the Mac app.</summary>
    public static class Tip
    {
        /// <summary>c0ldsheep's tip address. Its capital letters are its own spelling check (EIP-55); the tests run
        /// that check, so a slipped letter fails the tests instead of losing someone's money.</summary>
        public const string Address = "0x81fE1d0b00B3e6100E5e40E13982A3644e46e09A";

        public static readonly string[] Names = { "Small", "Medium", "Large" };
        /// <summary>Suggestions only: an address can't ask for an amount, so any amount works.</summary>
        public static readonly int[] Dollars = { 2, 5, 10 };

        /// <summary>"Medium coffee · $5"</summary>
        public static string Title(int size) => Names[size] + " coffee · $" + Dollars[size];

        /// <summary>The line above the cup. The count is always there, even at 0, so people can see that DragShift
        /// is counting; the ask only comes once it has done some work.</summary>
        public static string Line(long made)
        {
            if (made < 0) made = 0;
            string count = made.ToString("N0", CultureInfo.InvariantCulture) + (made == 1 ? " file" : " files");
            return "DragShift has fixed " + count + (made == 0 ? " for you so far. When it earns a coffee…" : " for you. If that was worth a coffee…");
        }

        /// <summary>The ring under the cup: three slices over the lower half, Small on the left. dx, dy: how far the
        /// cup has been dragged from its saucer (y grows downward). -1 near the middle or above it.</summary>
        public static int Slice(double dx, double dy, double inner = 30)
        {
            if (Math.Sqrt(dx * dx + dy * dy) < inner || dy <= 0) return -1;
            double degrees = Math.Atan2(dy, dx) * 180 / Math.PI; // 0 = right, 90 = down, 180 = left
            return degrees > 120 ? 0 : degrees >= 60 ? 1 : 2;
        }
    }
}
