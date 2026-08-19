using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ShiftScheduling.Api.Tests.TestInfra;

public sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ShiftScheduling.Api.Tests";

    public string EnvironmentName { get; set; } = "Test";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public string WebRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
