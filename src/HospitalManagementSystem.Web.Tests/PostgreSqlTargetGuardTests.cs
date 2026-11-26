using Xunit;

namespace HospitalManagementSystem.Web.Tests;

public sealed class PostgreSqlTargetGuardTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Database=hospital_management_dev;Username=hms_integration_test")]
    [InlineData("Host=127.0.0.1;Database=hospital_management_integration_test;Username=postgres")]
    [InlineData("Host=127.0.0.1,db.example.test;Database=hospital_management_integration_test;Username=hms_integration_test")]
    [InlineData("Host=db.example.test;Database=hospital_management_integration_test;Username=hms_integration_test")]
    [InlineData("Host=127.0.0.1;Database=hospital_management_integration_test;Username=hms_integration_test;Include Error Detail=true")]
    [InlineData("not-a-connection-string")]
    public void UnsafeTargetFailsBeforeConnecting(string connection) =>
        Assert.Throws<InvalidOperationException>(() => PostgreSqlTestDatabase.Validate(connection));

    [Fact]
    public void DedicatedLoopbackTargetPassesLocalValidation()
    {
        var settings = PostgreSqlTestDatabase.Validate("Host=127.0.0.1;Database=hospital_management_integration_test;Username=hms_integration_test");
        Assert.Equal("hospital_management_integration_test", settings.Database);
    }
}

