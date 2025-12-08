using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Learnit.Server.Data;
using Learnit.Server.Services;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.AspNetCore.Authentication.JwtBearer; 

namespace Learnit.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<AppDbContext>(opt =>
                opt.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

            builder.Services.AddCors(opt =>
            {
                opt.AddPolicy("AllowFrontend", b =>
                    b.WithOrigins("http://localhost:5173", "https://localhost:51338", "http://localhost:51338")
                     .AllowAnyHeader()
                     .AllowAnyMethod()
                     .AllowCredentials());
            });


            builder.Services.AddControllers();
            builder.Services.AddScoped<JwtService>();
            builder.Services.AddScoped<AiContextBuilder>();
            builder.Services.AddScoped<FriendService>();
            builder.Services.AddHttpClient<IAiProvider, OpenAiProvider>();

            // JWT authentication
            builder.Services.AddAuthentication("Bearer")
                .AddJwtBearer("Bearer", opt =>
                {
                    opt.TokenValidationParameters = new()
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = builder.Configuration["Jwt:Issuer"],
                        ValidAudience = builder.Configuration["Jwt:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(
                            System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured")))
                    };
                });

            var app = builder.Build();

            app.UseCors("AllowFrontend");
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
