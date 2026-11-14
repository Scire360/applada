using Applada.Api.Errors;
using Applada.Api.Security;
using Applada.Modules.Identity;
using Applada.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.AddApladaAuthentication();
builder.Services.AddApladaProblemDetails();
builder.Services.AddIdentityModule(builder.Configuration);

var app = builder.Build();

app.UseApladaProblemDetails();
app.UseAuthentication();
app.UseCurrentUser();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapIdentityEndpoints();

app.Run();

public partial class Program;
