﻿using Xunit;
using Verify = CSharpVerifier<Xunit.Analyzers.AssertEqualLiteralValueShouldBeFirst>;

public class AssertEqualLiteralValueShouldBeFirstTests
{
	[Fact]
	public async void DoesNotFindWarningWhenConstantOrLiteralUsedForBothArguments()
	{
		var source = @"
class TestClass {
    void TestMethod() {
        Xunit.Assert.Equal(""TestMethod"", nameof(TestMethod));
    }
}";

		await Verify.VerifyAnalyzerAsync(source);
	}

	public static TheoryData<string, string> TypesAndValues = new()
	{
		{ "int", "0" },
		{ "int", "0.0" },
		{ "int", "sizeof(int)" },
		{ "int", "default(int)" },
		{ "string", "null" },
		{ "string", "\"\"" },
		{ "string", "nameof(TestMethod)" },
		{ "System.Type", "typeof(string)" },
		{ "System.AttributeTargets", "System.AttributeTargets.Constructor" },
	};

	[Theory]
	[MemberData(nameof(TypesAndValues))]
	public async void DoesNotFindWarningForExpectedConstantOrLiteralValueAsFirstArgument(
		string type,
		string value)
	{
		var source = $@"
class TestClass {{
    void TestMethod() {{
        var v = default({type});
        Xunit.Assert.Equal({value}, v);
    }}
}}";

		await Verify.VerifyAnalyzerAsync(source);
	}

	[Theory]
	[MemberData(nameof(TypesAndValues))]
	public async void FindsWarningForExpectedConstantOrLiteralValueAsSecondArgument(
		string type,
		string value)
	{
		var source = $@"
class TestClass {{
    void TestMethod() {{
        var v = default({type});
        Xunit.Assert.Equal(v, {value});
    }}
}}";
		var expected =
			Verify
				.Diagnostic()
				.WithLocation(5, 9)
				.WithArguments(value, "Assert.Equal(expected, actual)", "TestMethod", "TestClass");

		await Verify.VerifyAnalyzerAsync(source, expected);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async void DoesNotFindWarningForExpectedConstantOrLiteralValueAsNamedExpectedArgument(bool useAlternateForm)
	{
		var prefix = useAlternateForm ? "@" : "";
		var source = $@"
class TestClass {{
    void TestMethod() {{
        var v = default(int);
        Xunit.Assert.Equal({prefix}actual: v, {prefix}expected: 0);
    }}
}}";

		await Verify.VerifyAnalyzerAsync(source);
	}

	[Theory]
	[MemberData(nameof(TypesAndValues))]
	public async void FindsWarningForExpectedConstantOrLiteralValueAsNamedExpectedArgument(
		string type,
		string value)
	{
		var source = $@"
class TestClass {{
    void TestMethod() {{
        var v = default({type});
        Xunit.Assert.Equal(actual: {value}, expected: v);
    }}
}}";
		var expected =
			Verify
				.Diagnostic()
				.WithLocation(5, 9)
				.WithArguments(value, "Assert.Equal(expected, actual)", "TestMethod", "TestClass");

		await Verify.VerifyAnalyzerAsync(source, expected);
	}

	[Theory]
	[InlineData("Equal", "{|CS1739:act|}", "exp")]
	[InlineData("{|CS1501:Equal|}", "expected", "{|CS1740:expected|}")]
	[InlineData("{|CS1501:Equal|}", "actual", "{|CS1740:actual|}")]
	[InlineData("Equal", "{|CS1739:foo|}", "bar")]
	public async void DoesNotFindWarningWhenArgumentsAreNotNamedCorrectly(
		string methodName,
		string firstArgumentName,
		string secondArgumentName)
	{
		var source = $@"
class TestClass {{
    void TestMethod() {{
        var v = default(int);
        Xunit.Assert.{methodName}({firstArgumentName}: 1, {secondArgumentName}: v);
    }}
}}";

		await Verify.VerifyAnalyzerAsync(source);
	}
}
