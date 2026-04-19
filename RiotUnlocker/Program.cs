using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RiotUnlocker;

class Program
{
    static readonly string[] Levels =
    [
        "TUTORIAL_DIAZ", "TUTORIAL_G8",
        "NOTAV_1", "NOTAV_2", "NOTAV_3", "NOTAV_3_LARGE", "NOTAV_4",
        "INDIGNADOS_1", "INDIGNADOS_2", "INDIGNADOS_3", "INDIGNADOS_4",
        "KERATEA_1", "KERATEA_2", "KERATEA_3", "KERATEA_4",
        "TAHRIR_1", "TAHRIR_2", "TAHRIR_3", "TAHRIR_4", "TAHRIR_5",
        "B_NOMUOS", "B_NOTREDAME", "B_OAKLAND", "B_ROME",
        "B_VENEZUELA", "B_CHILE", "B_BRAZIL", "B_COLOMBIA",
        "B_TURKEY", "B_SYNTAGMA", "B_LIBRARY", "B_CLICHY",
        "B_LONDON", "B_UKRAINE", "B_FOXCONN", "B_KOREA",
        "EASTEREGG_INTIFADA",
    ];

    static int Main(string[] args)
    {
        Console.WriteLine("RIOT - Civil Unrest | All Missions Unlocker");
        Console.WriteLine("============================================");

        string? dllPath = null;
        string? prefsDir = null;

        if (args.Length > 0)
        {
            dllPath = args[0];
        }
        else
        {
            (dllPath, prefsDir) = FindGamePaths();
        }

        if (dllPath == null || !File.Exists(dllPath))
        {
            Error("Could not find Assembly-CSharp.dll");
            Console.WriteLine();
            Console.WriteLine("Usage: RiotUnlocker <path/to/Assembly-CSharp.dll>");
            Console.WriteLine();
            Console.WriteLine("Expected location:");
            Console.WriteLine("  Steam/steamapps/common/RIOT/Riot_Data/Managed/Assembly-CSharp.dll");
            return 1;
        }

        Console.WriteLine($"Game DLL : {dllPath}");
        if (prefsDir != null)
            Console.WriteLine($"Prefs dir: {prefsDir}");
        Console.WriteLine();

        bool dllOk = PatchDll(dllPath);
        bool prefsOk = prefsDir != null && WritePrefs(prefsDir);

        Console.WriteLine();
        if (dllOk)
            Ok("DLL patch applied — EVERYTHING_UNLOCKED = true");
        if (prefsOk)
            Ok("Save file written — all missions marked complete");

        Console.WriteLine();
        Console.WriteLine("Done! Start the game and all missions will be available.");
        return dllOk ? 0 : 1;
    }

    static bool PatchDll(string dllPath)
    {
        var backupPath = dllPath + ".bak";

        try
        {
            if (IsAlreadyPatched(dllPath))
            {
                Warn("DLL is already patched, skipping");
                return true;
            }

            if (!File.Exists(backupPath))
            {
                File.Copy(dllPath, backupPath);
                Console.WriteLine($"Backup: {backupPath}");
            }

            using var module = ModuleDefinition.ReadModule(dllPath, new ReaderParameters { ReadWrite = true });

            var cheatType = module.Types.First(t => t.Name == "Cheat");
            var field = cheatType.Fields.First(f => f.Name == "EVERYTHING_UNLOCKED");

            var cctor = cheatType.Methods.FirstOrDefault(m => m.Name == ".cctor");
            if (cctor == null)
            {
                cctor = new MethodDefinition(".cctor",
                    MethodAttributes.Private | MethodAttributes.HideBySig |
                    MethodAttributes.SpecialName | MethodAttributes.RTSpecialName |
                    MethodAttributes.Static,
                    module.TypeSystem.Void);
                cheatType.Methods.Add(cctor);
                var il = cctor.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldc_I4_1));
                il.Append(il.Create(OpCodes.Stsfld, field));
                il.Append(il.Create(OpCodes.Ret));
            }
            else
            {
                var il = cctor.Body.GetILProcessor();
                var first = cctor.Body.Instructions[0];
                il.InsertBefore(first, il.Create(OpCodes.Stsfld, field));
                il.InsertBefore(cctor.Body.Instructions[0], il.Create(OpCodes.Ldc_I4_1));
            }

            module.Write();
            return true;
        }
        catch (Exception ex)
        {
            Error($"DLL patch failed: {ex.Message}");
            return false;
        }
    }

    static bool IsAlreadyPatched(string dllPath)
    {
        try
        {
            using var module = ModuleDefinition.ReadModule(dllPath);
            var cheatType = module.Types.FirstOrDefault(t => t.Name == "Cheat");
            if (cheatType == null) return false;
            var cctor = cheatType.Methods.FirstOrDefault(m => m.Name == ".cctor");
            if (cctor == null) return false;
            return cctor.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference fr &&
                fr.Name == "EVERYTHING_UNLOCKED");
        }
        catch { return false; }
    }

    static bool WritePrefs(string prefsDir)
    {
        try
        {
            Directory.CreateDirectory(prefsDir);
            var prefsPath = Path.Combine(prefsDir, "prefs");

            var lines = new List<string> { "<unity_prefs version_major=\"1\" version_minor=\"1\">" };

            void AddLevel(string name)
            {
                lines.Add($"\t<pref name=\"{name}_PoliceIsCompleted\" type=\"string\">True</pref>");
                lines.Add($"\t<pref name=\"{name}_RebelsIsCompleted\" type=\"string\">True</pref>");
                lines.Add($"\t<pref name=\"{name}_PoliceIsNeverPlayed\" type=\"string\">False</pref>");
                lines.Add($"\t<pref name=\"{name}_RebelsIsNeverPlayed\" type=\"string\">False</pref>");
                lines.Add($"\t<pref name=\"{name}_PoliceScore\" type=\"float\">100</pref>");
                lines.Add($"\t<pref name=\"{name}_RebelsScore\" type=\"float\">100</pref>");
            }

            foreach (var level in Levels)
            {
                AddLevel(level);
                AddLevel("GLOBAL_" + level);
            }

            lines.Add("</unity_prefs>");
            File.WriteAllLines(prefsPath, lines);
            return true;
        }
        catch (Exception ex)
        {
            Error($"Prefs write failed: {ex.Message}");
            return false;
        }
    }

    static (string? dllPath, string? prefsDir) FindGamePaths()
    {
        foreach (var steam in GetSteamPaths())
        {
            var dll = Path.Combine(steam, "steamapps", "common", "RIOT",
                "Riot_Data", "Managed", "Assembly-CSharp.dll");
            if (File.Exists(dll))
                return (dll, GetPrefsDir());
        }
        return (null, null);
    }

    static IEnumerable<string> GetSteamPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var reg = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (reg?.GetValue("SteamPath") is string sp)
                yield return sp;
            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".steam", "steam");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
    }

    static string? GetPrefsDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "unity3d", "DefaultCompany", "Riot");
        }
        // Windows uses registry — prefs not needed since DLL patch covers it
        // macOS uses plist — not implemented, DLL patch is enough
        return null;
    }

    static void Ok(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[OK]  ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    static void Warn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[!!]  ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    static void Error(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[ERR] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }
}
