using Applada.Api.Errors;
using Applada.Api.Security;
using Applada.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.AddApladaAuthentication();
builder.Services.AddApladaProblemDetails();

var app = builder.Build();

app.UseApladaProblemDetails();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

app.Run();

public partial class Program;
