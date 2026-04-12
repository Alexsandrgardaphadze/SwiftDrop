using Microsoft.EntityFrameworkCore;
using SwiftDrop.Server.Data;
using SwiftDrop.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("SwiftDrop.Server")
    ));

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = 500 * 1024 * 1024;
});

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.Run();