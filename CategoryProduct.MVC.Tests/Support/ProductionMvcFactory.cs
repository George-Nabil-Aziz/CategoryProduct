namespace CategoryProduct.MVC.Tests;

/// <summary>MVC test host running in the Production environment (real exception handler, no developer page).</summary>
public sealed class ProductionMvcFactory : MvcFactory
{
    protected override string EnvironmentName => "Production";
}
