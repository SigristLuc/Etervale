using PristonTale.Server.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddPristonServer();

var app = builder.Build();
app.MapPristonServices();
app.Run();
