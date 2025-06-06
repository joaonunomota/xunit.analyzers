using System.Threading.Tasks;
using Xunit;
using Xunit.Analyzers.Fixes;
using Verify = CSharpVerifier<Xunit.Analyzers.TestClassCannotBeNestedInGenericClass>;

public class TestClassCannotBeNestedInGenericClassFixerTests
{
	[Fact]
	public async Task MovesTestClassOutOfGenericParent()
	{
		const string before = /* lang=c#-test */ """
			public abstract class OpenGenericType<T>
			{
				public class [|NestedTestClass|]
				{
					[Xunit.Fact]
					public void TestMethod() { }
				}
			}
			""";
		const string after = /* lang=c#-test */ """
			public abstract class OpenGenericType<T>
			{
			}

			public class NestedTestClass
			{
				[Xunit.Fact]
				public void TestMethod() { }
			}
			""";

		await Verify.VerifyCodeFix(before, after, TestClassCannotBeNestedInGenericClassFixer.Key_ExtractTestClass);
	}
}
