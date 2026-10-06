// Usage: ApiDump <ManagedDir> <outFile> [focusRegex]
// Lists every type in Assembly-CSharp, then signatures (no method bodies, no game code) of focus types.
using System.Reflection;
using System.Text.RegularExpressions;

var managed = args[0];
var outPath = args[1];
var focus = new Regex(args.Length > 2 && args[2] != "" ? args[2]
    : @"Customiz|Cosmetic|Hat|Passport|Accessor|Skin|Head|Face|Outfit|^Character$|CharacterData|CharacterRef|Avatar|SyncedData|PersistentPlayer|Player$|PlayerHandler|MainCamera|Camera|Renderer|Material|Shader|Plugin|Mod",
    RegexOptions.IgnoreCase);

var dlls = Directory.GetFiles(managed, "*.dll").ToList();
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(dlls), "mscorlib");
var asm = mlc.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
Type[] types;
try { types = asm.GetTypes(); }
catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

string N(Type t) { try { return t.IsGenericType ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(N)) + ">" : t.Name; } catch { return "?"; } }
string Base(Type t) { try { return t.BaseType == null ? "" : N(t.BaseType); } catch { return "?"; } }
string Vis(MethodBase m) => m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private";
string Par(ParameterInfo p) { try { return (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + N(p.ParameterType) + " " + p.Name; } catch { return "?"; } }
const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

using var w = new StreamWriter(outPath);
w.WriteLine("# Managed assemblies"); foreach (var d in dlls.OrderBy(x => x)) w.WriteLine("  " + Path.GetFileName(d));
w.WriteLine("\n# All types in Assembly-CSharp");
foreach (var t in types.OrderBy(t => t.FullName)) w.WriteLine($"  {t.FullName} : {Base(t)}");

w.WriteLine("\n# Focus types (signatures)");
foreach (var t in types.Where(t => focus.IsMatch(t.Name)).OrderBy(t => t.FullName))
{
    w.WriteLine($"\n## {(t.IsEnum ? "enum" : t.IsInterface ? "interface" : "class")} {t.FullName} : {Base(t)}");
    try {
        if (t.IsEnum) { foreach (var n in t.GetFields(BindingFlags.Public | BindingFlags.Static)) w.WriteLine($"  {n.Name}"); continue; }
        foreach (var f in t.GetFields(F)) w.WriteLine($"  field {(f.IsPublic ? "public" : "private")}{(f.IsStatic ? " static" : "")} {N(f.FieldType)} {f.Name}");
        foreach (var p in t.GetProperties(F)) w.WriteLine($"  prop {N(p.PropertyType)} {p.Name}");
        foreach (var m in t.GetMethods(F).Where(m => !m.IsSpecialName))
            w.WriteLine($"  method {Vis(m)}{(m.IsStatic ? " static" : "")}{(m.IsVirtual ? " virtual" : "")} {N(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(Par))})");
    } catch (Exception e) { w.WriteLine($"  !! {e.GetType().Name}"); }
}
Console.WriteLine($"Wrote {outPath} ({types.Length} types)");
