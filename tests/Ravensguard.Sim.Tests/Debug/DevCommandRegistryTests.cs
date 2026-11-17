using Ravensguard.Sim.Debug;

namespace Ravensguard.Sim.Tests.Debug;

public class DevCommandRegistryTests
{
    [Fact] public void QuotedArgsParse()
    {
        Assert.True(DevCommandRegistry.TryParse("say \"hello  world\" x \"\" \"a\\\"b\"", out var t, out _));
        Assert.Equal(new[] { "say", "hello  world", "x", "", "a\"b" }, t);
    }
    [Fact] public void UnterminatedQuoteIsError()
    {
        var r = new DevCommandRegistry();
        Assert.StartsWith("error:", r.Execute("help \"oops"));
    }
    [Fact] public void ExecutesHandlerWithArgs()
    {
        var r = new DevCommandRegistry();
        r.Register("echo", "echo", a => string.Join("|", a));
        Assert.Equal("a b|c", r.Execute("echo \"a b\" c"));
    }
    [Fact] public void HelpIsOrderedAndBuiltIn()
    {
        var r = new DevCommandRegistry();
        r.Register("zed", "last", _ => ""); r.Register("alpha", "first", _ => "");
        var lines = r.Execute("help").Split('\n');
        Assert.Equal(new[] { "alpha - first", "help - List commands, or help <command> for one", "zed - last" }, lines);
        Assert.Equal("zed - last", r.Execute("help zed"));
    }
    [Fact] public void UnknownCommandIsHelpful()
    {
        var r = new DevCommandRegistry();
        r.Register("time", "t", _ => "");
        var o = r.Execute("tim");
        Assert.Contains("Unknown command 'tim'", o); Assert.Contains("help", o);
        Assert.Contains("Did you mean: time", r.Execute("timex"));
    }
    [Fact] public void HandlerExceptionBecomesError() 
    {
        var r = new DevCommandRegistry();
        r.Register("boom", "b", _ => throw new InvalidOperationException("bad"));
        Assert.Equal("error: boom failed: bad", r.Execute("boom"));
    }
    [Fact] public void EmptyLineIsEmpty() => Assert.Equal("", new DevCommandRegistry().Execute("   "));
}

public class InspectableTests
{
    sealed class Thing : IInspectable
    {
        public string InspectTitle => "Thing";
        public IEnumerable<InspectSection> Inspect() { yield return InspectSection.Of("Stats", ("hp", "10"), ("mp", "3")); }
    }
    [Fact] public void SectionRows()
    {
        var s = new Thing().Inspect().Single();
        Assert.Equal("Stats", s.Title);
        Assert.Equal(new InspectRow("mp", "3"), s.Rows[1]);
    }
}
