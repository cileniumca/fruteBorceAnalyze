using AnalyzeDomains.Infrastructure;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddOpenApi();

var configuration = builder.Configuration;
builder.Services.AddImportDomainsInfrastructure(configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();



app.Run();
