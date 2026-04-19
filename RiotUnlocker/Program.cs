using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
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
            using var module = ModuleDefinition.ReadModule(dllPath,
                new ReaderParameters { ReadWrite = true });

            var cheatType = module.Types.FirstOrDefault(t => t.Name == "Cheat")
                ?? throw new InvalidOperationException(
                    "Type 'Cheat' not found — game version may be unsupported");

            var field = cheatType.Fields.FirstOrDefault(f => f.Name == "EVERYTHING_UNLOCKED")
                ?? throw new InvalidOperationException(
                    "Field 'Cheat.EVERYTHING_UNLOCKED' not found — game version may be unsupported");

            if (!field.IsStatic)
                throw new InvalidOperationException(
                    "Field 'Cheat.EVERYTHING_UNLOCKED' is not static — unexpected game version");

            var cctor = cheatType.Methods.FirstOrDefault(m => m.Name == ".cctor");

            if (cctor != null && IsFieldSetInCctor(cctor, field))
            {
                Warn("DLL is already patched, skipping");
                return true;
            }

            if (!File.Exists(backupPath))
            {
                File.Copy(dllPath, backupPath);
                Console.WriteLine($"Backup: {backupPath}");
            }

            if (cctor == null)
            {
                cctor = new MethodDefinition(".cctor",
                    MethodAttributes.Private | MethodAttributes.HideBySig |
                    MethodAttributes.SpecialName | MethodAttributes.RTSpecialName |
                    MethodAttributes.Static,
                    module.TypeSystem.Void);
                cctor.Body = new MethodBody(cctor);
                cheatType.Methods.Add(cctor);

                var il = cctor.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldc_I4_1));
                il.Append(il.Create(OpCodes.Stsfld, field));
                il.Append(il.Create(OpCodes.Ret));
            }
            else
            {
                if (cctor.Body.Instructions.Count == 0)
                    throw new InvalidOperationException(
                        "Cheat..cctor has empty body — unexpected game version");

                var il = cctor.Body.GetILProcessor();
                var first = cctor.Body.Instructions[0];
                il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(first, il.Create(OpCodes.Stsfld, field));
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

    static bool IsFieldSetInCctor(MethodDefinition cctor, FieldDefinition field)
    {
        return cctor.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Stsfld &&
            i.Operand is FieldReference fr &&
            fr.Name == field.Name);
    }

    static bool WritePrefs(string prefsDir)
    {
        try
        {
            Directory.CreateDirectory(prefsDir);
            var prefsPath = Path.Combine(prefsDir, "prefs");

            XDocument doc;
            if (File.Exists(prefsPath))
            {
                var backupPath = prefsPath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(prefsPath, backupPath);
                    Console.WriteLine($"Prefs backup: {backupPath}");
                }

                try
                {
                    doc = XDocument.Load(prefsPath);
                }
                catch (XmlException)
                {
                    Warn("Existing prefs file is not valid XML — rewriting from scratch");
                    doc = NewPrefsDocument();
                }
            }
            else
            {
                doc = NewPrefsDocument();
            }

            if (doc.Root is null || doc.Root.Name.LocalName != "unity_prefs")
            {
                Warn("Existing prefs root is not <unity_prefs> — rewriting from scratch");
                doc = NewPrefsDocument();
            }
            var root = doc.Root!;

            foreach (var level in Levels)
            {
                WriteLevel(root, level);
                WriteLevel(root, "GLOBAL_" + level);
            }

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t",
                OmitXmlDeclaration = true,
            };
            using var writer = XmlWriter.Create(prefsPath, settings);
            doc.Save(writer);
            return true;
        }
        catch (Exception ex)
        {
            Error($"Prefs write failed: {ex.Message}");
            return false;
        }
    }

    static XDocument NewPrefsDocument() =>
        new(new XElement("unity_prefs",
            new XAttribute("version_major", "1"),
            new XAttribute("version_minor", "1")));

    static void WriteLevel(XElement root, string name)
    {
        SetPref(root, $"{name}_PoliceIsCompleted", "string", "True");
        SetPref(root, $"{name}_RebelsIsCompleted", "string", "True");
        SetPref(root, $"{name}_PoliceIsNeverPlayed", "string", "False");
        SetPref(root, $"{name}_RebelsIsNeverPlayed", "string", "False");
        SetPref(root, $"{name}_PoliceScore", "float", "100");
        SetPref(root, $"{name}_RebelsScore", "float", "100");
    }

    static void SetPref(XElement root, string name, string type, string value)
    {
        var existing = root.Elements("pref")
            .FirstOrDefault(e => (string?)e.Attribute("name") == name);
        if (existing != null)
        {
            existing.SetAttributeValue("type", type);
            existing.Value = value;
        }
        else
        {
            root.Add(new XElement("pref",
                new XAttribute("name", name),
                new XAttribute("type", type),
                value));
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
