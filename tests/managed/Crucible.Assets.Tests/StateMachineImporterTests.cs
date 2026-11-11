using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;

namespace Crucible.Tests;

// The StateMachine importer (ADR 0081): a .crstates (JSON, its GUID inside)
// checked and laid out as StateMachineFile.h has it, in a .crstatemachine.
public class StateMachineImporterTests
{
    private const string Idle = "5b2d9abd-9735-4e60-a088-9e28ed3d37ea";
    private const string Walk = "10880ff6-b22a-451e-a23a-d6729d1f813d";
    private const string Forward = "9c157526-fac4-4ead-a5f6-ad6bd1cf1e93";

    private sealed class Scratch : IDisposable
    {
        public Project Project { get; } =
            Project.Create(Path.Combine(Path.GetTempPath(), "crucible-states-" + Guid.NewGuid().ToString("N")), "Game");

        public ImportResult Import(string text)
        {
            File.WriteAllText(Path.Combine(Project.AssetsFolder, "Walker.crstates"), text);
            return AssetDatabase.Open(Project, [new StateMachineImporter()]).ImportAll().Single();
        }

        public void Dispose() => Directory.Delete(Project.Root, recursive: true);
    }

    private static T[] Table<T>(ImportedFile file, string tag) where T : unmanaged =>
        MemoryMarshal.Cast<byte, T>(file.Read(file.Find(Tag.Of(tag)))).ToArray();

    [Test]
    public async Task AStateMachineImportsLaidOutForTheRuntime()
    {
        using Scratch scratch = new();
        ImportResult result = scratch.Import($$"""
            {
              "$guid": "154c11ed-0836-4df7-ab30-572bcd3a4db1",
              "Parameters": [
                { "Name": "Speed", "Type": "Float", "Default": 1.5 },
                { "Name": "Hit", "Type": "Trigger" },
                { "Name": "Ready", "Type": "Bool", "Default": true }
              ],
              "States": [
                { "Name": "Idle", "Clip": "{{Idle}}" },
                { "Name": "Move", "Speed": 2, "Loop": false,
                  "Blend": { "Parameters": ["Speed"],
                             "Clips": [ { "Clip": "{{Forward}}", "At": [1.7] }, { "Clip": "{{Walk}}", "At": [0] } ] } }
              ],
              "Start": "Move",
              "Transitions": [
                { "From": "Idle", "To": "Move", "Blend": 0.25,
                  "Conditions": [ { "Parameter": "Speed", "Op": ">", "Value": 0.1 },
                                  { "Parameter": "Ready", "Op": "==", "Value": true } ] },
                { "Any": true, "To": "Idle", "Conditions": [ { "Parameter": "Hit" } ] },
                { "From": "Move", "To": "Idle", "ExitTime": 0.9 }
              ]
            }
            """);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.OutputPath!).EndsWith(".crstatemachine");
        using ImportedFile file = ImportedFile.Open(result.OutputPath!, StateMachineImporter.Kind,
            StateMachineImporter.KindVersion);
        StateMachineInfo info = Table<StateMachineInfo>(file, "INFO").Single();
        await Assert.That(info.Start).IsEqualTo(1u);
        await Assert.That(info.ParameterCount).IsEqualTo(3u);
        StateParameter[] parameters = Table<StateParameter>(file, "PARM");
        await Assert.That(string.Join(" ", parameters.Select(p => p.Type))).IsEqualTo("Float Trigger Bool");
        await Assert.That(string.Join(" ", parameters.Select(p => p.DefaultValue))).IsEqualTo("1.5 0 1");

        StateEntry move = Table<StateEntry>(file, "STAT")[1];
        await Assert.That(move.Dimensions).IsEqualTo(1u);
        await Assert.That(move.BlendParameters[0]).IsEqualTo(0);
        await Assert.That(move.BlendParameters[1]).IsEqualTo(-1);
        await Assert.That(move.Speed).IsEqualTo(2f);
        await Assert.That(move.Loop).IsEqualTo(0u);
        // The blend space's clips sorted by where they sit.
        StateMotion[] motions = Table<StateMotion>(file, "MOTN");
        await Assert.That(motions.Length).IsEqualTo(3);
        await Assert.That(motions[(int)move.FirstMotion].Clip.ToString()).IsEqualTo(Walk);
        await Assert.That(motions[(int)move.FirstMotion + 1].Position[0]).IsEqualTo(1.7f);

        StateTransition[] transitions = Table<StateTransition>(file, "TRAN");
        await Assert.That(string.Join(" ", transitions.Select(t => t.From))).IsEqualTo("0 -1 1");
        await Assert.That(transitions[0].BlendSeconds).IsEqualTo(0.25f);
        await Assert.That(transitions[0].ExitTime).IsLessThan(0f);
        await Assert.That(transitions[2].ExitTime).IsEqualTo(0.9f);
        TransitionCondition[] conditions = Table<TransitionCondition>(file, "COND");
        await Assert.That(string.Join(" ", conditions.Select(c => c.Op))).IsEqualTo("Greater Equal Set");
        await Assert.That(conditions[1].Value).IsEqualTo(1f);
    }

    [Test]
    [Arguments("""{ "Name": "Idle" }""", "state Idle needs a Clip or a Blend")]
    [Arguments("""{ "Name": "Idle", "Clip": "walk" }""", "state Idle: a clip is given by its asset ID")]
    [Arguments("""{ "Name": "Move", "Blend": { "Parameters": ["Speed", "Turn"], "Clips": [] } }""",
        "2D blend spaces aren't supported yet")]
    [Arguments("""{ "Name": "Move", "Blend": { "Parameters": ["Ready"], "Clips": [] } }""", "Ready isn't a Float")]
    public async Task AMistakenStateFailsTheImportSayingWhere(string state, string message)
    {
        using Scratch scratch = new();
        ImportResult result = scratch.Import($$"""
            { "$guid": "154c11ed-0836-4df7-ab30-572bcd3a4db2",
              "Parameters": [ { "Name": "Speed", "Type": "Float" }, { "Name": "Turn", "Type": "Float" },
                              { "Name": "Ready", "Type": "Bool" } ],
              "States": [ {{state}} ] }
            """);
        await Assert.That(result.Error!).Contains(message);
    }

    [Test]
    [Arguments("""{ "From": "Idle", "To": "Run" }""", "transition 1's To: there's no state Run")]
    [Arguments("""{ "To": "Idle" }""", "transition 1 needs a From state, or \"Any\": true")]
    [Arguments("""{ "From": "Idle", "To": "Idle", "Conditions": [ { "Parameter": "Speed", "Op": "==", "Value": 1 } ] }""",
        "Speed is a Float, compared with > or <")]
    [Arguments("""{ "From": "Idle", "To": "Idle", "Conditions": [ { "Parameter": "Hit", "Op": "==" } ] }""",
        "Hit is a Trigger, which needs no Op or Value")]
    [Arguments("""{ "From": "Idle", "To": "Idle", "ExitTime": 2 }""", "ExitTime is a share of the state, 0 to 1")]
    public async Task AMistakenTransitionFailsTheImportSayingWhere(string transition, string message)
    {
        using Scratch scratch = new();
        ImportResult result = scratch.Import($$"""
            { "$guid": "154c11ed-0836-4df7-ab30-572bcd3a4db3",
              "Parameters": [ { "Name": "Speed", "Type": "Float" }, { "Name": "Hit", "Type": "Trigger" } ],
              "States": [ { "Name": "Idle", "Clip": "{{Idle}}" } ],
              "Transitions": [ {{transition}} ] }
            """);
        await Assert.That(result.Error!).Contains(message);
    }
}
