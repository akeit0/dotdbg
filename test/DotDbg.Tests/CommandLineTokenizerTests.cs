using DotDbg.Cli;
using Xunit;

namespace DotDbg.Tests;

public class CommandLineTokenizerTests
{
    [Fact]
    public void Tokenize_RespectsDoubleQuotes()
    {
        var tokens = CommandLineTokenizer.Tokenize("print \"hello world\"");

        Assert.Equal(new[] { "print", "hello world" }, tokens);
    }

    [Fact]
    public void Tokenize_RespectsSingleQuotes()
    {
        var tokens = CommandLineTokenizer.Tokenize("print 'hello world'");

        Assert.Equal(new[] { "print", "hello world" }, tokens);
    }

    [Fact]
    public void Tokenize_EscapesQuotes()
    {
        var tokens = CommandLineTokenizer.Tokenize("print \"a;b\"");

        Assert.Equal(new[] { "print", "a;b" }, tokens);
    }

    [Fact]
    public void Tokenize_EscapesBackslash()
    {
        var tokens = CommandLineTokenizer.Tokenize("print \\\\ \\n");

        Assert.Equal(new[] { "print", "\\", "\\n" }, tokens);
    }

    [Fact]
    public void Tokenize_UnclosedQuote_Throws()
    {
        var ex = Assert.Throws<CommandParseException>(() =>
            CommandLineTokenizer.Tokenize("print \"unclosed")
        );
        Assert.Contains("Unclosed quote", ex.Message);
    }

    [Fact]
    public void SplitScript_DoesNotSplitOnSemicolonInsideQuotes()
    {
        var parts = CommandLineTokenizer.SplitScript("print \"a;b\"; wait");

        Assert.Equal(new[] { "print \"a;b\"", "wait" }, parts);
    }

    [Fact]
    public void SplitScript_EscapesSemicolon()
    {
        var parts = CommandLineTokenizer.SplitScript("print a\\;b; wait");

        Assert.Equal(new[] { "print a;b", "wait" }, parts);
    }

    [Fact]
    public void SplitScript_SourceInline_SplitsIntoCommands()
    {
        var parts = CommandLineTokenizer.SplitScript("file a.dll; break Program.cs:20; run");

        Assert.Equal(new[] { "file a.dll", "break Program.cs:20", "run" }, parts);
    }

    [Fact]
    public void SplitScript_UnclosedQuote_Throws()
    {
        Assert.Throws<CommandParseException>(() =>
            CommandLineTokenizer.SplitScript("print \"unclosed; wait")
        );
    }

    [Fact]
    public void Tokenize_PreservesEmptyQuotedArg()
    {
        var tokens = CommandLineTokenizer.Tokenize("run \"\" arg2");

        Assert.Equal(new[] { "run", "", "arg2" }, tokens);
    }

    [Fact]
    public void Tokenize_SemicolonInUnquotedString_IsPartOfToken()
    {
        var tokens = CommandLineTokenizer.Tokenize("print a;b");

        Assert.Equal(new[] { "print", "a;b" }, tokens);
    }

    [Fact]
    public void SplitScript_DoesNotSplitOnEscapedQuoteInsideQuotes()
    {
        var parts = CommandLineTokenizer.SplitScript("print \"a\\\";b\"; wait");

        Assert.Equal(new[] { "print \"a\\\";b\"", "wait" }, parts);
    }

    [Fact]
    public void SplitScript_DoesNotSplitOnEscapedSingleQuoteInsideQuotes()
    {
        var parts = CommandLineTokenizer.SplitScript("print 'a\\';b'; wait");

        Assert.Equal(new[] { "print 'a\\';b'", "wait" }, parts);
    }
}
