using System.Numerics;
using Crucible.Audio;
using Crucible.Ecs;

namespace Crucible.Tests;

// The audio contract from C# (ADR 0075), under JIT and NativeAOT: the null
// audio's service answers through the bindings and counts, and its
// components save.
public class AudioContractTests
{
    [Before(Class)]
    public static void Register() => Crucible.Scenes.EngineComponents.Register();

    [Test]
    public async Task TheNullAudioCountsThroughTheBindings()
    {
        NullAudioCounts before = Crucible.Audio.Api.NullAudioCounts();
        uint voice = Crucible.Audio.Api.PlayOneShot(default, 0, Vector3.Zero, 1, 0);
        Crucible.Audio.Api.Stop(voice, 0);
        Crucible.Audio.Api.BlendToSnapshot("Paused", 0.5f);
        Crucible.Audio.Api.PlayMusic(default, new MusicTransition { CrossfadeSeconds = 1, Align = MusicAlign.Beat });
        NullAudioCounts after = Crucible.Audio.Api.NullAudioCounts();
        await Assert.That(voice).IsNotEqualTo(0u);
        await Assert.That(after.Plays - before.Plays).IsEqualTo(1u);
        await Assert.That(after.Stops - before.Stops).IsEqualTo(1u);
        await Assert.That(after.Snapshots - before.Snapshots).IsEqualTo(1u);
        await Assert.That(after.MusicChanges - before.MusicChanges).IsEqualTo(1u);
    }

    [Test]
    public async Task ItsComponentsSave()
    {
        await Assert.That(SavedComponents.Find("Crucible.Audio.AudioSource")).IsNotNull();
        await Assert.That(SavedComponents.Find("Crucible.Audio.AudioListener")).IsNotNull();
    }
}
