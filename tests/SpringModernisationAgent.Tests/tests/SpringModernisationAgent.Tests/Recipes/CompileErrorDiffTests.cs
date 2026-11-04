using SpringModernisationAgent.Recipes;

namespace SpringModernisationAgent.Tests.Recipes;

public class CompileErrorDiffTests
{
    [Fact]
    public void Compare_ProbeFiveToSix_FindsOneFixedErrorBehindAHigherCount()
    {
        string[] before =
        [
            "[ERROR] /home/runner/work/x/repo/src/main/java/a/CustomerOrder.java:[25,24] incompatible types: UserType<?>",
            "[ERROR] /home/runner/work/x/repo/src/main/java/a/MoneyUserType.java:[21,5] method does not override or implement a method from a supertype",
            "[ERROR] /home/runner/work/x/repo/src/main/java/a/MoneyUserType.java:[41,5] method does not override or implement a method from a supertype"
        ];

        string[] after =
        [
            // The same error, moved by the edit: unchanged.
            "[ERROR] /w/orders-service-candidate-0123/src/main/java/a/MoneyUserType.java:[30,5] method does not override or implement a method from a supertype",
            "[ERROR] /w/orders-service-candidate-0123/src/main/java/a/MoneyUserType.java:[42,12] cannot find symbol",
            "  symbol:   class BigDecimal",
            "  location: class a.MoneyUserType",
            "[ERROR] /w/orders-service-candidate-0123/src/main/java/a/MoneyUserType.java:[82,17] cannot find symbol",
            "  symbol:   class BigDecimal",
            "  location: class a.MoneyUserType"
        ];

        var comparison = CompileErrorDiff.Compare(CompileErrorDiff.Errors(before), CompileErrorDiff.Errors(after));

        Assert.Equal(1, comparison.Unchanged);
        Assert.Equal(
            ["CustomerOrder.java: incompatible types: UserType<?>", "MoneyUserType.java: method does not override or implement a method from a supertype"],
            comparison.Removed.Select(error => error.ToString()));

        // Two identical errors on different lines count twice.
        Assert.Equal(2, comparison.Added.Count);
        Assert.All(comparison.Added, error => Assert.Equal("MoneyUserType.java: cannot find symbol | symbol: class BigDecimal | location: class a.MoneyUserType", error.ToString()));
    }

    [Fact]
    public void Errors_TheSymbolDetailDistinguishesOtherwiseIdenticalMessages()
    {
        var errors = CompileErrorDiff.Errors(
        [
            "[ERROR] src/main/java/a/B.java:[1,1] cannot find symbol",
            "  symbol:   class BigDecimal",
            "[ERROR] src/main/java/a/B.java:[2,1] cannot find symbol",
            "  symbol:   variable resultSet"
        ]);

        Assert.Equal(2, errors.Distinct().Count());
        Assert.All(errors, error => Assert.Equal("src/main/java/a/B.java", error.File));
    }

    [Fact]
    public void Compare_NoErrorsAfter_RemovesEverything()
    {
        var before = CompileErrorDiff.Errors(["[ERROR] src/main/java/a/B.java:[1,1] cannot find symbol"]);

        var comparison = CompileErrorDiff.Compare(before, []);

        Assert.Single(comparison.Removed);
        Assert.Empty(comparison.Added);
    }
}
