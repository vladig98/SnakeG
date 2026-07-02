WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services.AddHostedService<SnakeService>();
builder.Services.AddSingleton<SimulationControl>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("https://localhost:55698")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

WebApplication app = builder.Build();

app.UseCors("AllowReactApp");

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapFallbackToFile("/index.html");
app.MapHub<SnakeHub>("/snake");

app.MapPost("/api/simulate/{count}", (int count, SimulationControl control) =>
{
    control.TargetGeneration += count;
    return Results.Ok(new { control.TargetGeneration });
});

app.MapPost("/api/settings", (SimulationControl newSettings, SimulationControl control) =>
{
    control.MutationRate = newSettings.MutationRate;
    control.TournamentSize = newSettings.TournamentSize;
    control.ElitismCount = newSettings.ElitismCount;
    control.NumberOfParents = newSettings.NumberOfParents;

    control.EatenApplePoints = newSettings.EatenApplePoints;
    control.ExtraApplesMultiplier = newSettings.ExtraApplesMultiplier;
    control.RightDirectionPoints = newSettings.RightDirectionPoints;
    control.WrongDirectionPoints = newSettings.WrongDirectionPoints;
    control.PointForLooping = newSettings.PointForLooping;
    control.DeathPenalty = newSettings.DeathPenalty;
    control.NumberOfRepeats = newSettings.NumberOfRepeats;
    control.HealthOffset = newSettings.HealthOffset;

    return Results.Ok(control);
});

app.MapGet("/api/brain/export", (SimulationControl control) =>
{
    return control.BestBrain is null ? Results.NotFound("No champion brain available yet.") : Results.Ok(control.BestBrain);
});

app.MapPost("/api/brain/import", (NeuralNetwork importedBrain, SimulationControl control) =>
{
    control.InjectedBrain = importedBrain;
    return Results.Ok();
});

app.Run();
