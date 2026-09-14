using System.Reflection;
using NetArchTest.Rules;
using TaskFlow.Core.Entities;
using TaskFlow.Infrastructure.Data;

namespace TaskFlow.ArchitectureTests;

/// <summary>
/// Enforces the dependency direction the solution already follows.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here was verified to hold before it was written. That distinction
/// matters: a rule describing what the code already does costs nothing to keep green
/// and fails only when someone genuinely regresses it. A rule describing what the
/// code ought to do one day is a wish, and a permanently red test teaches the team to
/// ignore the suite.
/// </para>
/// <para>
/// These rules are cheap - no database, no container, milliseconds to run - and they
/// catch the kind of mistake that is easy to make and hard to notice: a single
/// convenient <c>using</c> that quietly inverts a layer boundary and is only painful
/// to unpick months later.
/// </para>
/// </remarks>
public class LayerDependencyTests
{
    private static readonly Assembly CoreAssembly = typeof(User).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ApplicationDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private const string CoreNamespace = "TaskFlow.Core";
    private const string InfrastructureNamespace = "TaskFlow.Infrastructure";

    /// <summary>Asserts a NetArchTest result, naming the offending types on failure.</summary>
    private static void AssertPasses(TestResult result, string rule)
    {
        Assert.True(result.IsSuccessful,
            $"""
             Architecture rule violated: {rule}

             Offending types:
               {string.Join($"{Environment.NewLine}  ", result.FailingTypeNames ?? new List<string>())}
             """);
    }

    /// <summary>
    /// The domain and application layer must not depend on the persistence layer.
    /// </summary>
    /// <remarks>
    /// This is the rule the whole layout rests on. Core defines the repository
    /// interfaces; Infrastructure implements them. If Core were allowed to reference
    /// Infrastructure the dependency would invert, the interfaces would stop being a
    /// seam, and Core would become untestable without a database.
    /// </remarks>
    [Fact]
    public void Core_does_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        AssertPasses(result, "TaskFlow.Core must not depend on TaskFlow.Infrastructure");
    }

    /// <summary>
    /// Neither inner layer may depend on the web layer.
    /// </summary>
    /// <remarks>
    /// Business rules must not know how they are being invoked. A dependency on the
    /// API project would tie the domain to HTTP and make it unusable from anything
    /// else - a worker, a scheduled job, a console tool.
    /// </remarks>
    [Fact]
    public void Core_and_Infrastructure_do_not_depend_on_the_api_layer()
    {
        AssertPasses(
            Types.InAssembly(CoreAssembly)
                .ShouldNot().HaveDependencyOn("TaskFlowAPI").GetResult(),
            "TaskFlow.Core must not depend on the API layer");

        AssertPasses(
            Types.InAssembly(InfrastructureAssembly)
                .ShouldNot().HaveDependencyOn("TaskFlowAPI").GetResult(),
            "TaskFlow.Infrastructure must not depend on the API layer");
    }

    /// <summary>
    /// Entity Framework must stay inside the persistence layer.
    /// </summary>
    /// <remarks>
    /// Core currently has no EF Core reference at all, which is what lets its
    /// behaviour be tested without a database. The moment an entity carries a
    /// <c>[Column]</c> attribute or a service takes a <c>DbContext</c>, the storage
    /// technology has leaked into the domain and every test of it needs a database.
    /// </remarks>
    [Fact]
    public void Entity_framework_does_not_leak_into_the_core_layer()
    {
        var result = Types.InAssembly(CoreAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        AssertPasses(result, "TaskFlow.Core must not depend on Entity Framework Core");
    }

    /// <summary>
    /// ASP.NET Core types must not appear in the inner layers.
    /// </summary>
    [Fact]
    public void Web_framework_types_do_not_leak_into_the_inner_layers()
    {
        AssertPasses(
            Types.InAssembly(CoreAssembly)
                .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore").GetResult(),
            "TaskFlow.Core must not depend on ASP.NET Core");

        AssertPasses(
            Types.InAssembly(InfrastructureAssembly)
                .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore").GetResult(),
            "TaskFlow.Infrastructure must not depend on ASP.NET Core");
    }

    /// <summary>
    /// Controllers reach persistence through the unit-of-work abstraction, never the
    /// <see cref="ApplicationDbContext"/> directly.
    /// </summary>
    /// <remarks>
    /// A controller holding a DbContext can compose queries inline, which puts
    /// business rules in the presentation layer and makes them reachable only through
    /// HTTP. Going through <c>IUnitOfWork</c> keeps that logic in a layer that can be
    /// unit tested.
    /// </remarks>
    [Fact]
    public void Controllers_do_not_use_the_DbContext_directly()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn(typeof(ApplicationDbContext).FullName)
            .GetResult();

        AssertPasses(result, "Controllers must not depend on ApplicationDbContext directly");
    }

    /// <summary>
    /// Repository implementations live in the persistence layer.
    /// </summary>
    [Fact]
    public void Repository_implementations_live_in_the_infrastructure_layer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .HaveNameEndingWith("Repository")
            .Should()
            .ResideInNamespaceStartingWith(InfrastructureNamespace)
            .GetResult();

        AssertPasses(result, "Repository implementations must reside in TaskFlow.Infrastructure");
    }

    /// <summary>
    /// The repository abstractions stay in the layer that owns them.
    /// </summary>
    [Fact]
    public void Repository_interfaces_live_in_the_core_layer()
    {
        var result = Types.InAssembly(CoreAssembly)
            .That()
            .AreInterfaces()
            .And()
            .HaveNameEndingWith("Repository")
            .Should()
            .ResideInNamespaceStartingWith(CoreNamespace)
            .GetResult();

        AssertPasses(result, "Repository interfaces must reside in TaskFlow.Core");
    }
}
