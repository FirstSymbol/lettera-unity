using FirstSymbol.Lettera.Editor;
using NUnit.Framework;

/// <summary>EditorPrefs belong to the person at the machine, in every editor of this version. While these tests run, what the inspectors keep per user stays in memory.</summary>
[SetUpFixture]
public sealed class LetteraTestPrefs
{
    [OneTimeSetUp]
    public void Begin() => LtPrefs.InMemory = true;

    [OneTimeTearDown]
    public void End() => LtPrefs.InMemory = false;
}
