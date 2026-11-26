using Crucible.Assets;

namespace Crucible.Tests.Assets;

// Plugins in a project (ADR 0118): read, ordered, refused with why, and
// their assets' paths; and the official plugins beside the editor (ADR 0123).
public class PluginTests
{
    private sealed class Scratch : IDisposable
    {
        public Scratch() =>
            Project = Project.Create(Path.Combine(Path.GetTempPath(), "crucible-plugins-" + Guid.NewGuid().ToString("N")), "Game");

        public Project Project { get; }

        // A plugin folder with a manifest; compiled when builtFor is given.
        public string Add(string name, string[]? uses = null, string? builtFor = null, string? manifestName = null)
        {
            string folder = Path.Combine(Project.PluginsFolder, name);
            Directory.CreateDirectory(folder);
            string usesJson = string.Join(", ", (uses ?? []).Select(used => $"\"{used}\""));
            File.WriteAllText(Path.Combine(folder, name + Plugin.Extension),
                $"{{ \"Name\": \"{manifestName ?? name}\", \"Version\": \"1.0.0\", \"Uses\": [{usesJson}], \"Crucible\": \"{builtFor ?? ""}\" }}");
            return folder;
        }

        public void Refresh() => Project.RefreshPlugins();

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static string Names(Project project) => string.Join(", ", project.Plugins.Select(plugin => plugin.Name));

    [Test]
    public async Task PluginsLoadAfterThoseTheyUseThenByName()
    {
        using Scratch scratch = new();
        scratch.Add("Quests", uses: ["Dialogue"]);
        scratch.Add("Dialogue", uses: ["Audio"]);
        scratch.Add("Audio");
        scratch.Add("Banners");
        scratch.Refresh();
        await Assert.That(Names(scratch.Project)).IsEqualTo("Audio, Banners, Dialogue, Quests");
        await Assert.That(scratch.Project.PluginProblems).IsEmpty();
    }

    [Test]
    public async Task APluginUsingAMissingOneIsLeftOutAndSoAreThoseUsingIt()
    {
        using Scratch scratch = new();
        scratch.Add("Quests", uses: ["Inventory"]);
        scratch.Add("Journal", uses: ["Quests"]);
        scratch.Add("Audio");
        scratch.Refresh();
        await Assert.That(Names(scratch.Project)).IsEqualTo("Audio");
        await Assert.That(scratch.Project.PluginProblems).Contains("Quests uses Inventory, which isn't in Plugins/");
        await Assert.That(scratch.Project.PluginProblems).Contains("Journal uses Quests, which can't be used");
    }

    [Test]
    public async Task PluginsUsingEachOtherAreLeftOut()
    {
        using Scratch scratch = new();
        scratch.Add("Dialogue", uses: ["Quests"]);
        scratch.Add("Quests", uses: ["Dialogue"]);
        scratch.Refresh();
        await Assert.That(scratch.Project.Plugins).IsEmpty();
        await Assert.That(string.Join(" | ", scratch.Project.PluginProblems)).IsEqualTo("Dialogue and Quests use each other");
    }

    [Test]
    public async Task ACompiledPluginLoadsOnlyInTheCrucibleLineItWasBuiltFor()
    {
        using Scratch scratch = new();
        scratch.Add("Current", builtFor: CrucibleVersion.Text);
        scratch.Add("Old", builtFor: "0.0.7");
        scratch.Add("Broken", builtFor: "soon");
        scratch.Refresh();
        await Assert.That(Names(scratch.Project)).IsEqualTo("Current");
        await Assert.That(scratch.Project.Plugins[0].IsCompiled).IsTrue();
        string line = CrucibleVersion.Line;
        string old = CrucibleVersion.LineOf("0.0.7")!;
        await Assert.That(scratch.Project.PluginProblems)
            .Contains($"Old was built for Crucible {old}; this is {line}. Rebuild it for {line}");
        await Assert.That(scratch.Project.PluginProblems)
            .Contains("Broken.crplugin has a Crucible version that isn't one: \"soon\"");
    }

    [Test]
    public async Task VersionsMatchOnTheMinorBeforeOneAndTheMajorAfter()
    {
        await Assert.That(CrucibleVersion.LineOf("0.4.2")).IsEqualTo("0.4");
        await Assert.That(CrucibleVersion.LineOf("0.4")).IsEqualTo("0.4");
        await Assert.That(CrucibleVersion.LineOf("2.3.1")).IsEqualTo("2");
        await Assert.That(CrucibleVersion.LineOf("1")).IsNull();
        await Assert.That(CrucibleVersion.LineOf("1.x.0")).IsNull();
        await Assert.That(CrucibleVersion.Accepts(CrucibleVersion.Text)).IsTrue();
        await Assert.That(CrucibleVersion.Accepts($"{CrucibleVersion.Major}.{CrucibleVersion.Minor}.{CrucibleVersion.Patch + 5}")).IsTrue();
    }

    [Test]
    public async Task ACompiledPluginCantUseASourceOne()
    {
        using Scratch scratch = new();
        scratch.Add("Dialogue");
        scratch.Add("Quests", uses: ["Dialogue"], builtFor: CrucibleVersion.Text);
        scratch.Refresh();
        await Assert.That(Names(scratch.Project)).IsEqualTo("Dialogue");
        await Assert.That(scratch.Project.PluginProblems)
            .Contains("Quests is compiled and uses Dialogue, which is source: build Dialogue too");
    }

    [Test]
    public async Task AFolderIsAPluginOnlyWithAManifestOfItsName()
    {
        using Scratch scratch = new();
        Directory.CreateDirectory(Path.Combine(scratch.Project.PluginsFolder, "Loose"));
        scratch.Add("Renamed", manifestName: "Other");
        scratch.Add("Game");
        scratch.Add("Editor");
        scratch.Add("Two_Words");
        scratch.Refresh();
        await Assert.That(Names(scratch.Project)).IsEqualTo("Two_Words");
        await Assert.That(scratch.Project.PluginProblems).Contains("Plugins/Loose has no Loose.crplugin, so it isn't a plugin");
        await Assert.That(scratch.Project.PluginProblems)
            .Contains("Renamed.crplugin names the plugin \"Other\": a plugin's name is its folder's");
        await Assert.That(scratch.Project.PluginProblems).Contains("Game can't be a plugin's name: it's the game's");
        await Assert.That(scratch.Project.PluginProblems).Contains("Editor can't be a plugin's name: it's the editor's");
    }

    [Test]
    public async Task TheEditorsAssetsHaveARootOfTheirOwn()
    {
        using Scratch scratch = new();
        string folder = Path.Combine(scratch.Project.Root, "EditorAssets");
        Directory.CreateDirectory(folder);
        await Assert.That(scratch.Project.AssetRoots().Any(root => root.Root == Project.EditorRoot)).IsFalse();
        scratch.Project.EditorAssetsFolder = folder;
        await Assert.That(scratch.Project.AssetRoots().Last()).IsEqualTo((folder, "Editor:"));
        await Assert.That(scratch.Project.FileOf("Editor:Sphere.gltf")).IsEqualTo(Path.Combine(folder, "Sphere.gltf"));
        await Assert.That(scratch.Project.AssetPathOf(Path.Combine(folder, "Sphere.gltf"))).IsEqualTo("Editor:Sphere.gltf");
        await Assert.That(scratch.Project.IsReadOnly("Editor:Sphere.gltf")).IsTrue();
    }

    [Test]
    public async Task APluginsAssetsArePathedByItsName()
    {
        using Scratch scratch = new();
        string dialogue = scratch.Add("Dialogue");
        scratch.Add("Fonts", builtFor: CrucibleVersion.Text);
        scratch.Refresh();
        Project project = scratch.Project;
        string icon = Path.Combine(dialogue, "Assets", "Icons", "Talk.txt");
        await Assert.That(project.FileOf("Dialogue:Icons/Talk.txt")).IsEqualTo(icon);
        await Assert.That(project.FileOf("Dialogue:")).IsEqualTo(Path.Combine(dialogue, "Assets"));
        await Assert.That(project.FileOf("Models/Walker.gltf")).IsEqualTo(Path.Combine(project.AssetsFolder, "Models", "Walker.gltf"));
        await Assert.That(project.AssetPathOf(icon)).IsEqualTo("Dialogue:Icons/Talk.txt");
        await Assert.That(project.AssetPathOf(Path.Combine(project.AssetsFolder, "a.txt"))).IsEqualTo("a.txt");
        await Assert.That(project.AssetPathOf(Path.Combine(project.Root, "Code", "a.cs"))).IsNull();
        await Assert.That(project.IsReadOnly("Fonts:Sans.ttf")).IsTrue();
        await Assert.That(project.IsReadOnly("Dialogue:Icons/Talk.txt")).IsFalse();
        await Assert.That(project.IsReadOnly("Sans.ttf")).IsFalse();
        await Assert.That(AssetPaths.Join("Dialogue:", "Icons")).IsEqualTo("Dialogue:Icons");
        await Assert.That(AssetPaths.Join("Dialogue:Icons", "Talk.txt")).IsEqualTo("Dialogue:Icons/Talk.txt");
        await Assert.That(AssetPaths.Join("", "Models")).IsEqualTo("Models");
        await Assert.That(AssetPaths.FolderOf("Dialogue:Talk.txt")).IsEqualTo("Dialogue:");
        await Assert.That(AssetPaths.FolderOf("Dialogue:Icons/Talk.txt")).IsEqualTo("Dialogue:Icons");
        await Assert.That(AssetPaths.FolderOf("Talk.txt")).IsEqualTo("");
    }

    [Test]
    public async Task TheDatabaseFindsAPluginsAssetsUnderItsName()
    {
        using Scratch scratch = new();
        string dialogue = scratch.Add("Dialogue");
        Directory.CreateDirectory(Path.Combine(dialogue, "Assets", "Lines"));
        File.WriteAllText(Path.Combine(dialogue, "Assets", "Lines", "Hello.txt"), "hello");
        File.WriteAllText(Path.Combine(scratch.Project.AssetsFolder, "Game.txt"), "game");
        scratch.Refresh();
        AssetDatabase database = AssetDatabase.Open(scratch.Project, [new TextImporter()]);
        AssetEntry? line = database.Find("Dialogue:Lines/Hello.txt");
        await Assert.That(line).IsNotNull();
        await Assert.That(database.Find("Game.txt")).IsNotNull();
        // Its sidecar beside it, keeping its ID wherever the plugin goes.
        await Assert.That(File.Exists(Path.Combine(dialogue, "Assets", "Lines", "Hello.txt" + Sidecar.Extension))).IsTrue();
        IReadOnlyList<ImportResult> results = database.ImportAll();
        await Assert.That(results.Single(result => result.Path == "Dialogue:Lines/Hello.txt").Outcome).IsEqualTo(ImportOutcome.Imported);
    }

    // An official plugins folder, as the editor's build makes beside it:
    // each plugin a manifest (no Crucible version) and an editor assembly.
    private sealed class OfficialFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "crucible-official-" + Guid.NewGuid().ToString("N"));

        public void Add(string name)
        {
            string folder = Path.Combine(Folder, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + Plugin.Extension), $"{{ \"Name\": \"{name}\", \"Version\": \"0.1.0\", \"Uses\": [] }}");
            File.WriteAllBytes(Path.Combine(folder, name + ".Editor.dll"), [0]);
        }

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    [Test]
    public async Task OfficialPluginsAreAllUsedForStarAndOnlyThoseListedOtherwise()
    {
        using OfficialFolder official = new();
        official.Add("Tilemaps");
        official.Add("Particles");
        List<string> problems = [];
        IReadOnlyList<Plugin> all = OfficialPlugins.Read(official.Folder, [OfficialPlugins.All], problems);
        await Assert.That(string.Join(", ", all.Select(plugin => plugin.Name))).IsEqualTo("Particles, Tilemaps");
        await Assert.That(all.All(plugin => plugin is { IsOfficial: true, IsCompiled: true, HasEditorAssembly: true, HasCode: false })).IsTrue();
        await Assert.That(string.Join(", ", OfficialPlugins.Read(official.Folder, ["Tilemaps"], problems).Select(plugin => plugin.Name)))
            .IsEqualTo("Tilemaps");
        await Assert.That(OfficialPlugins.Read(official.Folder, [], problems)).IsEmpty();
        await Assert.That(problems).IsEmpty();
        await Assert.That(OfficialPlugins.Read(official.Folder, ["Weather"], problems)).IsEmpty();
        await Assert.That(problems).Contains("The project uses the official plugin Weather, which this editor doesn't have");
        await Assert.That(OfficialPlugins.Read(null, [OfficialPlugins.All], problems)).IsEmpty();
    }

    [Test]
    public async Task AProjectUsesEveryOfficialPluginUnlessItsFileListsThem()
    {
        using Scratch scratch = new();
        await Assert.That(Project.Open(scratch.Project.ProjectFilePath).Official.Single()).IsEqualTo(OfficialPlugins.All);
        // A file from before the list, without it: all of them too.
        File.WriteAllText(scratch.Project.ProjectFilePath, "{ \"Name\": \"Game\" }");
        await Assert.That(Project.Open(scratch.Project.ProjectFilePath).Official.Single()).IsEqualTo(OfficialPlugins.All);
        File.WriteAllText(scratch.Project.ProjectFilePath, "{ \"Name\": \"Game\", \"Official\": [] }");
        Project none = Project.Open(scratch.Project.ProjectFilePath);
        await Assert.That(none.Official).IsEmpty();
        // Saving the build settings keeps the list as it is.
        none.SaveBuild(new BuildSettings());
        await Assert.That(Project.Open(scratch.Project.ProjectFilePath).Official).IsEmpty();
    }

    [Test]
    public async Task AProjectsPluginsMayUseOfficialOnesButNotTakeTheirNames()
    {
        using Scratch scratch = new();
        using OfficialFolder official = new();
        official.Add("Tilemaps");
        scratch.Add("Tilemaps");
        scratch.Add("Dungeons", uses: ["Tilemaps"]);
        List<string> problems = [];
        IReadOnlyList<Plugin> used = OfficialPlugins.Read(official.Folder, [OfficialPlugins.All], problems);
        (IReadOnlyList<Plugin> plugins, IReadOnlyList<string> more) = ProjectPlugins.Read(scratch.Project.PluginsFolder, "Game", used);
        await Assert.That(string.Join(", ", plugins.Select(plugin => $"{plugin.Name}{(plugin.IsOfficial ? " (official)" : "")}")))
            .IsEqualTo("Tilemaps (official), Dungeons");
        await Assert.That(more).Contains("Plugins/Tilemaps has the name of an official plugin; rename it");
    }
}
