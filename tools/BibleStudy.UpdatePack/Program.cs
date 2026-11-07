using BibleStudy.Core;

// Used by release.ps1: lists a finished program folder and makes the pack with its new files.
//   BibleStudy.UpdatePack <folder> <rid> <version> <output folder> [manifest of the previous release]
if (args.Length < 4) { Console.Error.WriteLine("usage: <folder> <rid> <version> <output folder> [previous manifest]"); return 2; }
var (folder, rid, version, output) = (args[0], args[1], args[2], args[3]);
var previous = args.Length > 4 && File.Exists(args[4]) ? DeltaUpdate.ParseManifest(File.ReadAllText(args[4])) : null;
var manifest = DeltaUpdate.BuildPack(folder, rid, version, previous, output);
var json = DeltaUpdate.WriteManifest(manifest);
File.WriteAllText(Path.Combine(folder, DeltaUpdate.ManifestName), json);
File.WriteAllText(Path.Combine(output, DeltaUpdate.ManifestAsset(rid, version)), json);
var fresh = manifest.Files.Where(f => f.Tag == "v" + version).ToList();
Console.WriteLine($"{rid}: {manifest.Files.Count} files, {fresh.Count} new, pack {fresh.Sum(f => f.Length) / 1048576.0:0.0} MB{(previous is null ? " (no previous manifest: everything is new)" : "")}");
foreach (var f in fresh.Take(25)) Console.WriteLine("  " + f.Path);
if (fresh.Count > 25) Console.WriteLine($"  … and {fresh.Count - 25} more");
return 0;
