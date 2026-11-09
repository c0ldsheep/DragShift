using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DragShift.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    sealed class TestAttribute : Attribute { }

    /// <summary>A tiny test runner, so the tests need nothing downloaded: every [Test] method in this
    /// assembly runs; a thrown exception is a failure.</summary>
    static class Runner
    {
        static int Main(string[] args)
        {
            var filter = args.FirstOrDefault();
            var tests = typeof(Runner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).Contains(filter))
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name).ToList();
            int failed = 0;
            foreach (var m in tests)
            {
                var name = m.DeclaringType.Name + "." + m.Name;
                var started = DateTime.UtcNow;
                try
                {
                    m.Invoke(null, null);
                    Console.WriteLine("  ✓ " + name + " (" + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms)");
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine("  ✗ " + name + ": " + e.InnerException?.Message);
                    Console.WriteLine("    " + e.InnerException?.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
                }
            }
            Console.WriteLine(failed == 0 ? "All " + tests.Count + " tests passed." : failed + " of " + tests.Count + " tests failed.");
            return failed == 0 ? 0 : 1;
        }
    }

    static class Expect
    {
        public static void True(bool ok, string what)
        {
            if (!ok) throw new Exception("expected: " + what);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) throw new Exception(what + ": expected " + expected + ", got " + actual);
        }

        public static void Throws<TE>(Action a, string what) where TE : Exception
        {
            try { a(); }
            catch (TE) { return; }
            throw new Exception("expected " + typeof(TE).Name + ": " + what);
        }
    }

    /// <summary>A fresh temp folder per test.</summary>
    sealed class Sandbox : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "dragshift-win-tests-" + Guid.NewGuid().ToString("N"));
        public Sandbox() { Directory.CreateDirectory(Dir); }
        public string Path_(string name) => Path.Combine(Dir, name);
        public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
    }
}
